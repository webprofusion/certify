using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;
using Azure.ResourceManager.Resources;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Plugins;

namespace Certify.Providers.DNS.Azure
{
    public class DnsProviderAzureProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    public class DnsProviderAzure : DnsProviderBase, IDnsProvider
    {
        private ILog _log;

        private ArmClient _azureClient = null;
        private SubscriptionResource _subscription = null;

        private Dictionary<string, string> _credentials;
        private Dictionary<string, string> _parameters;

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

        internal static Uri MapServiceToAuthorityHost(string service)
        {
            if (string.IsNullOrEmpty(service))
            {
                return AzureAuthorityHosts.AzurePublicCloud;
            }

            switch (service.Trim())
            {
                case "global":
                    return AzureAuthorityHosts.AzurePublicCloud;
                case "usgov":
                    return AzureAuthorityHosts.AzureGovernment;
                case "china":
                    return AzureAuthorityHosts.AzureChina;
                case "germany":
                    return AzureAuthorityHosts.AzureGermany;
                default:
                    return AzureAuthorityHosts.AzurePublicCloud;
            }
        }

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            _log = log;

            _credentials = credentials;
            _parameters = parameters;

            // https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ResourceManager.Dns-readme?view=azure-dotnet

            _credentials.TryGetValue("service", out var azureServiceEnvironment);

            var azureCred = new ClientSecretCredential(credentials["tenantid"], credentials["clientid"], credentials["secret"], new ClientSecretCredentialOptions { AuthorityHost = MapServiceToAuthorityHost(azureServiceEnvironment) });
            _azureClient = new ArmClient(azureCred, credentials["subscriptionid"], new ArmClientOptions { Environment = MapAzureServiceToAzureEnvironment(azureServiceEnvironment) });
            _subscription = await _azureClient.GetDefaultSubscriptionAsync();

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out var customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            return true;
        }

        /// <summary>
        /// Map our azureServiceEnvironment selection to the configuration for an Azure Environment, default is standard Azure Cloud
        /// </summary>
        /// <param name="service"></param>
        /// <returns></returns>
        private ArmEnvironment MapAzureServiceToAzureEnvironment(string service)
        {
            if (string.IsNullOrEmpty(service))
            {
                return ArmEnvironment.AzurePublicCloud;
            }

            switch (service.Trim())
            {
                case "global":
                    return ArmEnvironment.AzurePublicCloud;
                case "usgov":
                    return ArmEnvironment.AzureGovernment;
                case "china":
                    return ArmEnvironment.AzureChina;
                case "germany":
                    return ArmEnvironment.AzureGermany;
                default:
                    return ArmEnvironment.AzurePublicCloud;
            }
        }

        private ResourceGroupResource _resourceGroup = null;

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            var domainInfo = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);

            if (string.IsNullOrEmpty(domainInfo.RootDomain))
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to determine root domain in zone." };
            }

            var recordName = NormaliseRecordName(domainInfo, request.RecordName);

            try
            {
                var zone = _zones.FirstOrDefault(z => z.Data.Name == request.ZoneId);

                var currentRecords = zone.GetDnsTxtRecords();

                if (currentRecords.Any(t => t.Data.Name == recordName))
                {
                    // update existing
                    var existing = currentRecords.FirstOrDefault(t => t.Data.Name == recordName);

                    if (existing.Data.DnsTxtRecords.Any(t => t.Values.Any(v => v == request.RecordValue)))
                    {
                        // already exists
                        return new ActionResult
                        {
                            IsSuccess = true,
                            Message = $"DNS TXT Record Already Exists: {recordName} in root domain {domainInfo.RootDomain} with value: {request.RecordValue} "
                        };
                    }
                    else
                    {
                        existing.Data.DnsTxtRecords.FirstOrDefault()?.Values.Add(request.RecordValue);

                    }

                    var result = await existing.UpdateAsync(existing.Data);
                    if (result?.Value?.HasData == true)
                    {
                        return new ActionResult
                        {
                            IsSuccess = true,
                            Message = $"DNS TXT Record Created: {recordName} in root domain {domainInfo.RootDomain} with value: {request.RecordValue} "
                        };
                    }
                    else
                    {
                        return new ActionResult
                        {
                            IsSuccess = true,
                            Message = "DNS TXT record creation failed."
                        };
                    }
                }
                else
                {
                    var newTxtRecord = new DnsTxtRecordData { TtlInSeconds = 5 };
                    var newTxtRecordValue = new DnsTxtRecordInfo();
                    newTxtRecordValue.Values.Add(request.RecordValue);
                    newTxtRecord.DnsTxtRecords.Add(newTxtRecordValue);

                    var result = await currentRecords.CreateOrUpdateAsync(WaitUntil.Completed, recordName, newTxtRecord);

                    if (result != null)
                    {
                        return new ActionResult
                        {
                            IsSuccess = true,
                            Message = $"DNS TXT Record Created: {recordName} in root domain {domainInfo.RootDomain} with value: {request.RecordValue} "
                        };
                    }
                    else
                    {
                        return new ActionResult
                        {
                            IsSuccess = true,
                            Message = "DNS TXT record creation failed."
                        };
                    }
                }
            }
            catch (Exception exp)
            {
                // failed
                _log.Warning($"Azure DNS create recrod failed: {exp.Message}");
                return new ActionResult { IsSuccess = false, Message = $"DNS TXT Record create failed {exp.Message}" };
            }
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
                var zone = _zones.FirstOrDefault(z => z.Data.Name == request.ZoneId);

                var currentRecords = zone.GetDnsTxtRecords();

                var existing = currentRecords.FirstOrDefault(t => t.Data.Name == recordName);

                // if a TXT record existis with the same name and multiple values either remove the target value or remove the whole record is no more values present
                if (existing.HasData)
                {
                    // delete existing
                    existing.Data.DnsTxtRecords.FirstOrDefault()?.Values.Remove(request.RecordValue);
                    if (existing.Data.DnsTxtRecords.FirstOrDefault()?.Values.Any() == false)
                    {
                        // no more values, delete the record
                        await existing.DeleteAsync(WaitUntil.Completed);
                    }
                    else
                    {
                        // update the record
                        await existing.UpdateAsync(existing.Data);
                    }

                    return new ActionResult { IsSuccess = true, Message = $"DNS TXT Record '{recordName}' Deleted" };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = "DNS TXT Record '{recordName}' Delete failed: " + exp.InnerException.Message };
            }

            return new ActionResult { IsSuccess = true, Message = $"DNS TXT Record '{recordName}' delete not required" };

        }

        private List<DnsZoneResource> _zones = new List<DnsZoneResource>();
        public override async Task<List<DnsZone>> GetZones()
        {
            if (_zones.Any())
            {
                return _zones.Select(z => new DnsZone { ZoneId = z.Data.Name, Name = z.Data.Name }).ToList();
            }
            else
            {
                var results = new List<DnsZone>();

                var zones = _subscription.GetDnsZonesAsync(1000);

                var zonesEnumerator = zones.GetAsyncEnumerator();

                while (await zonesEnumerator.MoveNextAsync())
                {
                    var z = zonesEnumerator.Current;
                    results.Add(new DnsZone { ZoneId = z.Data.Name, Name = z.Data.Name });

                    _zones.Add(z);
                }

                return results;
            }
        }
    }
}
