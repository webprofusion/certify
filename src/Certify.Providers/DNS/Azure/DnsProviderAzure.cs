using Certify.Models.Config;
using Certify.Models.Providers;
using Microsoft.Azure.Management.Dns;
using Microsoft.Azure.Management.Dns.Models;
using Microsoft.Rest.Azure.Authentication;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

        public async Task<bool> InitProvider()
        {
            // https://docs.microsoft.com/en-us/dotnet/api/overview/azure/dns?view=azure-dotnet

            Microsoft.Rest.ServiceClientCredentials serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(
                _credentials["tenantid"],
                _credentials["clientid"],
                _credentials["secret"]
                );

            _dnsClient = new DnsManagementClient(serviceCreds);

            _dnsClient.SubscriptionId = _credentials["subscriptionid"];
            return true;
        }

        public async Task<ActionResult> CreateRecord(DnsCreateRecordRequest request)
        {
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
                       request.RecordName,
                       RecordType.TXT,
                       recordSetParams
               );

                if (result != null)
                {
                    return new ActionResult
                    {
                        IsSuccess = true,
                        Message = $"DNS TXT Record Created: {request.RecordName} with value: {request.RecordValue} "
                    };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = (exp.InnerException != null ? exp.InnerException.Message : exp.Message) };
            }

            return new ActionResult { IsSuccess = false, Message = "DNS TXT Record create failed" };
        }

        public async Task<ActionResult> DeleteRecord(DnsDeleteRecordRequest request)
        {
            try
            {
                await _dnsClient.RecordSets.DeleteAsync(
                       _credentials["resourcegroupname"],
                       request.ZoneId,
                       request.RecordName,
                       RecordType.TXT
               );

                return new ActionResult { IsSuccess = true, Message = "DNS TXT Record Deleted" };
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = "DNS TXT Record Delete failed: " + exp.InnerException.Message };
            }
        }

        public async Task<List<DnsZone>> GetZones()
        {
            List<DnsZone> results = new List<DnsZone>();
            var list = await _dnsClient.Zones.ListAsync();
            foreach (var z in list)
            {
                results.Add(new DnsZone { ZoneId = z.Name, Description = z.Name });
            }
            return results;
        }
    }
}