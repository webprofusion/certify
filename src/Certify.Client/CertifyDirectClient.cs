using Certify.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Client
{
    /// <summary>
    /// This version of the client is a direct reference to Certify.Core, for gradual code migration
    /// to CertifyServiceClient
    /// </summary>
    public class CertifyDirectClient : ICertifyClient
    {
        public Task<List<ManagedSite>> GetManagedSites(string filter, int maxresults)
        {
            throw new NotImplementedException();
        }

        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites()
        {
            throw new NotImplementedException();
        }

        public async Task<ManagedSite> AddOrUpdateManagedSite(ManagedSite site)
        {
            throw new NotImplementedException();
        }

        public Task<CertificateRequestResult> PerformCertificateRequest(ManagedSite site)
        {
            throw new NotImplementedException();
        }

        public Task<object> GetRequestsInProgress()
        {
            throw new NotImplementedException();
        }

        public Task<bool> DeleteManagedSite(ManagedSite site)
        {
            throw new NotImplementedException();
        }
    }
}