using Certify.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Certify.Client
{
    // This version of the client communicates with the Certify.Service instance on the local machine
    public class CertifyServiceClient : ICertifyClient
    {
        private HttpClient _client = new HttpClient();

        private async Task<string> FetchAsync(string endpoint)
        {
            var response = await _client.GetAsync("http://localhost:9696/api/" + endpoint);
            var json = await response.Content.ReadAsStringAsync();
            return json;
        }

        public Task<ManagedSite> AddOrUpdateManagedSite(ManagedSite site)
        {
            throw new NotImplementedException();
        }

        public Task<bool> DeleteManagedSite(ManagedSite site)
        {
            throw new NotImplementedException();
        }

        public async Task<List<ManagedSite>> GetManagedSites(string filter, int maxresults)
        {
            var json = await FetchAsync("managedsites?filter=&maxresults=" + maxresults);
            return JsonConvert.DeserializeObject<List<ManagedSite>>(json);
        }

        public Task<object> GetRequestsInProgress()
        {
            throw new NotImplementedException();
        }

        public Task<CertificateRequestResult> PerformCertificateRequest(ManagedSite site)
        {
            throw new NotImplementedException();
        }

        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites()
        {
            throw new NotImplementedException();
        }

        public async Task<string> GetAppVersion()
        {
            return await FetchAsync("system/getappversion");
        }
    }
}