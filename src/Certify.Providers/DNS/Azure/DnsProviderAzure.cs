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
                TTL = 1300,
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
                return new ActionResult { IsSuccess = false, Message = exp.InnerException.Message };
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