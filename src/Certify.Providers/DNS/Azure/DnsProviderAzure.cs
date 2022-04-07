using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Plugins;
using Microsoft.Azure.Management.Dns;
using Microsoft.Azure.Management.Dns.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Rest.Azure.Authentication;

namespace Certify.Providers.DNS.Azure
{
    public class DnsProviderAzureProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    public class DnsProviderAzure : DnsProviderBase, IDnsProvider
    {
        private ILog _log;

        private DnsManagementClient _dnsClient;

        private Dictionary<string, string> _credentials;

        private int? _customPropagationDelay = null;

        public int PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.API.Azure",
            Title = "Azure DNS API",
            Description = "Validates via Azure DNS APIs using credentials",
            HelpUrl = "https://docs.certifytheweb.com/docs/dns/providers/azuredns",
            PropagationDelaySeconds = 60,
            ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="service",Name="Azure Service", IsRequired=true, IsPassword=false, IsCredential=true, Value="global", OptionsList="global=Azure Cloud; china=Azure China; germany=Azure Germany; usgov=Azure US Government" },
                        new ProviderParameter{ Key="tenantid", Name="Tenant Id", IsRequired=false },
                        new ProviderParameter{ Key="clientid", Name="Application Id", IsRequired=false },
                        new ProviderParameter{ Key="secret",Name="Svc Principal Secret", IsRequired=true , IsPassword=true},
                        new ProviderParameter{ Key="subscriptionid",Name="DNS Subscription Id", IsRequired=true , IsPassword=false},
                        new ProviderParameter{ Key="resourcegroupname",Name="Resource Group Name", IsRequired=true , IsPassword=false},
                        new ProviderParameter{ Key="zoneid",Name="DNS Zone Name", IsRequired=true, IsPassword=false, IsCredential=false }
                    },
            ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            Config = "Provider=Certify.Providers.DNS.Azure",
            HandlerType = ChallengeHandlerType.INTERNAL
        };

        public DnsProviderAzure()
        {
        }

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await GetZones();

                if (zones != null && zones.Any())
                {
                    return new ActionResult { IsSuccess = true, Message = "Test Completed OK." };
                }
                else
                {
                    return new ActionResult { IsSuccess = true, Message = "Test completed, but no zones returned." };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = true, Message = $"Test Failed: {exp.Message}" };
            }
        }

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            _log = log;

            _credentials = credentials;

            // https://docs.microsoft.com/en-us/dotnet/api/overview/azure/dns?view=azure-dotnet

            _credentials.TryGetValue("service", out var service);

            var azureEnvironment = MapAzureServiceToAzureEnvironment(service);
            var azureAdSettings = MapAzureEnvironmentToADSettings(azureEnvironment);

            var serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(
                _credentials["tenantid"],
                _credentials["clientid"],
                _credentials["secret"],
                azureAdSettings
                );

            _dnsClient = new DnsManagementClient(serviceCreds)
            {
                SubscriptionId = _credentials["subscriptionid"],
                BaseUri = new Uri(azureEnvironment.ResourceManagerEndpoint)
            };

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out int customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }
            return true;
        }

        /// <summary>
        /// Map our service selection to the configuration for an Azure Environment, default is standard Azure Cloud
        /// </summary>
        /// <param name="service"></param>
        /// <returns></returns>
        private AzureEnvironment MapAzureServiceToAzureEnvironment(string service)
        {
            if (string.IsNullOrEmpty(service))
            {
                return AzureEnvironment.AzureGlobalCloud;
            }

            switch (service.Trim())
            {
                case "global":
                    return AzureEnvironment.AzureGlobalCloud;
                case "usgov":
                    return AzureEnvironment.AzureUSGovernment;
                case "china":
                    return AzureEnvironment.AzureChinaCloud;
                case "germany":
                    return AzureEnvironment.AzureGermanCloud;
                default:
                    return AzureEnvironment.FromName(service);
            }
        }

        /// <summary>
        /// Map an Azure environment to the corresponding Active Directory settings
        /// </summary>
        /// <param name="env"></param>
        /// <returns></returns>

        private ActiveDirectoryServiceSettings MapAzureEnvironmentToADSettings(AzureEnvironment env)
        {
            if (env == null)
            {
                return ActiveDirectoryServiceSettings.Azure;
            }
            else if (env.Name == AzureEnvironment.AzureUSGovernment.Name)
            {
                return ActiveDirectoryServiceSettings.AzureUSGovernment;
            }
            else if (env.Name == AzureEnvironment.AzureGermanCloud.Name)
            {
                return ActiveDirectoryServiceSettings.AzureGermany;
            }
            else if (env.Name == AzureEnvironment.AzureChinaCloud.Name)
            {
                return ActiveDirectoryServiceSettings.AzureChina;
            }
            else
            {
                return ActiveDirectoryServiceSettings.Azure;
            }

        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            var domainInfo = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);

            if (string.IsNullOrEmpty(domainInfo.RootDomain))
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to determine root domain in zone." };
            }

            var recordName = NormaliseRecordName(domainInfo, request.RecordName);

            var recordSetParams = new RecordSet
            {
                TTL = 5,
                TxtRecords = new List<TxtRecord>
                {
                    new TxtRecord(new[] {
                        request.RecordValue
                    })
                }
            };

            try
            {
                var currentRecord = await _dnsClient.RecordSets.GetAsync(_credentials["resourcegroupname"], request.ZoneId, recordName, RecordType.TXT);

                if (currentRecord != null && currentRecord.TxtRecords.Any())
                {
                    foreach (var record in currentRecord.TxtRecords)
                    {
                        recordSetParams.TxtRecords.Add(record);
                    }
                }

            }
            catch
            {
                // No record exist
            }

            try
            {
                var result = await _dnsClient.RecordSets.CreateOrUpdateAsync(
                       _credentials["resourcegroupname"],
                       request.ZoneId,
                       recordName,
                       RecordType.TXT,
                       recordSetParams
                );

                if (result != null)
                {
                    return new ActionResult
                    {
                        IsSuccess = true,
                        Message = $"DNS TXT Record Created: {recordName} in root domain {domainInfo.RootDomain} with value: {request.RecordValue} "
                    };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = (exp.InnerException != null ? exp.InnerException.Message : exp.Message) };
            }

            return new ActionResult { IsSuccess = false, Message = "DNS TXT Record create failed" };
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            var domainInfo = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);

            if (string.IsNullOrEmpty(domainInfo.RootDomain))
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to determine root domain in zone." };
            }

            var recordName = NormaliseRecordName(domainInfo, request.RecordName);

            try
            {
                await _dnsClient.RecordSets.DeleteAsync(
                       _credentials["resourcegroupname"],
                       request.ZoneId,
                       recordName,
                       RecordType.TXT
               );

                return new ActionResult { IsSuccess = true, Message = $"DNS TXT Record '{recordName}' Deleted" };
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = "DNS TXT Record '{recordName}' Delete failed: " + exp.InnerException.Message };
            }
        }

        public override async Task<List<DnsZone>> GetZones()
        {
            var results = new List<DnsZone>();

            // azure defaults to returning only the first 100 zones, and the max that can be listed in one call is 1000
            // TODO: move to paging API.
            var list = await _dnsClient.Zones.ListAsync(top: 999);
            foreach (var z in list)
            {
                results.Add(new DnsZone { ZoneId = z.Name, Name = z.Name });
            }
            return results;
        }
    }
}
