using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;
using Microsoft.Azure.Management.Dns;
using Microsoft.Azure.Management.Dns.Models;
using Microsoft.Rest.Azure.Authentication;

namespace Certify.Providers.DNS.Azure
{
    public class DnsProviderAzure : IDnsProvider
    {
        private DnsManagementClient _dnsClient;

        private Dictionary<string, string> _credentials;

        public int PropagationDelaySeconds => 60;

        public string ProviderId => "DNS01.API.Azure";

        public string ProviderTitle => "Azure DNS API";

        public string ProviderDescription => "Validates via Azure DNS APIs using credentials";

        public string ProviderHelpUrl => "https://certifytheweb.com/docs/dns/azure";

        public List<ProviderParameter> ProviderParameters => new List<ProviderParameter>{
                    new ProviderParameter{Key="tenantid", Name="Tenant Id", IsRequired=false },
                    new ProviderParameter{Key="clientid", Name="Application Id", IsRequired=false },
                    new ProviderParameter{Key="secret",Name="Svc Principal Secret", IsRequired=true , IsPassword=true},
                    new ProviderParameter{Key="subscriptionid",Name="DNS Subscription Id", IsRequired=true , IsPassword=false},
                    new ProviderParameter{Key="resourcegroupname",Name="Resource Group Name", IsRequired=true , IsPassword=false}
                };

        public DnsProviderAzure(Dictionary<string, string> credentials)
        {
            _credentials = credentials;
        }

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await this.GetZones();

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

        public async Task<bool> InitProvider()
        {
            // https://docs.microsoft.com/en-us/dotnet/api/overview/azure/dns?view=azure-dotnet

            var serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(
                _credentials["tenantid"],
                _credentials["clientid"],
                _credentials["secret"]
                );

            _dnsClient = new DnsManagementClient(serviceCreds);

            _dnsClient.SubscriptionId = _credentials["subscriptionid"];
            return true;
        }

        /// <summary>
        /// Where a record name is in the form _acme-challenge.www.subdomain.domain.com, determine
        /// the root domain (i.e domain.com or subdomain.domain.com) info
        /// </summary>
        /// <param name="recordName"></param>
        /// <returns></returns>
        public async Task<DnsRecord> DetermineDomainRoot(string recordName)
        {
            var zones = await _dnsClient.Zones.ListAsync();

            DnsRecord info = new DnsRecord { RecordType = "TXT" };

            foreach (var z in zones)
            {
                if (recordName.EndsWith(z.Name) && (info.RootDomain == null || z.Name.Length < info.RootDomain.Length))
                {
                    info.RootDomain = z.Name;
                    info.ZoneId = z.Id;
                }
            }
            return info;
        }

        public string NormaliseRecordName(DnsRecord info, string recordName)
        {
            var result = recordName.Replace(info.RootDomain, "");
            result = result.TrimEnd('.');
            return result;
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            var domainInfo = await DetermineDomainRoot(request.RecordName);

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
            var domainInfo = await DetermineDomainRoot(request.RecordName);

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

        public async Task<List<DnsZone>> GetZones()
        {
            var results = new List<DnsZone>();
            var list = await _dnsClient.Zones.ListAsync();
            foreach (var z in list)
            {
                results.Add(new DnsZone { ZoneId = z.Name, Name = z.Name });
            }
            return results;
        }
    }
}
