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

        /*public static ProviderDefinition GetDefinition()
        {
        }*/

        public DnsProviderAzure(Dictionary<string, string> credentials)
        {
            _credentials = credentials;
        }

        public async Task InitProvider()
        {
            // https://docs.microsoft.com/en-us/dotnet/api/overview/azure/dns?view=azure-dotnet

            Microsoft.Rest.ServiceClientCredentials serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(
                _credentials["tenantid"],
                _credentials["clientid"],
                _credentials["secret"]
                );

            _dnsClient = new DnsManagementClient(serviceCreds);

            _dnsClient.SubscriptionId = _credentials["subscriptionid"];
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
                       _credentials["zoneid"],
                       request.TargetDomainName,
                       RecordType.TXT,
                       recordSetParams
               );

                if (result != null)
                {
                    return new ActionResult { IsSuccess = true, Message = "DNS TXT Record Created" };
                }
            }
            catch (Exception exp)
            {
                new ActionResult { IsSuccess = false, Message = exp.InnerException.Message };
            }

            return new ActionResult { IsSuccess = false, Message = "DNS TXT Record create failed" };
        }

        public async Task<ActionResult> DeleteRecord(DnsDeleteRecordRequest request)
        {
            throw new NotImplementedException();
        }
    }
}