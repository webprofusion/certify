using Certify.Models.Providers;
using System;
using System.Threading.Tasks;

namespace Certify.Providers.DNS.Azure
{
    public class DnsProviderAzure : IDnsProvider
    {
        public Task<DnsRequestResult> CreateRecord(DnsCreateRecordRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<DnsRequestResult> DeleteRecord(DnsDeleteRecordRequest request)
        {
            throw new NotImplementedException();
        }
    }
}