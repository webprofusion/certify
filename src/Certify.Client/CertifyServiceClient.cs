using Certify.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Certify.Client
{
    // This version of the client communicates with the Certify.Service instance on the local machine
    public class CertifyServiceClient : ICertifyClient
    {
        private HttpClient _client = new HttpClient();
        private string baseUri = "http://localhost:9696/api/";

        private async Task<string> FetchAsync(string endpoint)
        {
            var response = await _client.GetAsync(baseUri + endpoint);
            return await response.Content.ReadAsStringAsync();
        }

        private async Task<HttpResponseMessage> PostAsync(string endpoint, object data)
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, System.Text.UnicodeEncoding.UTF8, "application/json");
            return await _client.PostAsync(baseUri + endpoint, content);
        }

        #region System

        public async Task<string> GetAppVersion()
        {
            return await FetchAsync("system/appversion");
        }

        public async Task<UpdateCheck> CheckForUpdates()
        {
            var result = await FetchAsync("system/updatecheck");
            return JsonConvert.DeserializeObject<UpdateCheck>(result);
        }

        #endregion System

        #region Server

        public async Task<bool> IsServerAvailable(StandardServerTypes serverType)
        {
            var result = await FetchAsync($"server/isavailable/{serverType}");
            return bool.Parse(result);
        }

        public async Task<List<SiteBindingItem>> GetServerSiteList(StandardServerTypes serverType)
        {
            var result = await FetchAsync($"server/sitelist/{serverType}");
            return JsonConvert.DeserializeObject<List<SiteBindingItem>>(result);
        }

        public async Task<Version> GetServerVersion(StandardServerTypes serverType)
        {
            var result = await FetchAsync($"server/version/{serverType}");
            return JsonConvert.DeserializeObject<Version>(result);
        }

        public async Task<List<DomainOption>> GetServerSiteDomains(StandardServerTypes serverType, string serverSiteId)
        {
            var result = await FetchAsync($"server/sitedomains/{serverType}/{serverSiteId}");
            return JsonConvert.DeserializeObject<List<DomainOption>>(result);
        }

        #endregion Server

        public async Task<Preferences> GetPreferences()
        {
            var result = await FetchAsync("preferences/");
            return JsonConvert.DeserializeObject<Preferences>(result);
        }

        public async Task<bool> SetPreferences(Preferences preferences)
        {
            var response = await PostAsync("preferences/", preferences);
            return true;
        }

        #region Managed Sites

        public async Task<List<ManagedSite>> GetManagedSites(ManagedSiteFilter filter)
        {
            var response = await PostAsync("managedsites/search/", filter);

            var serializer = new JsonSerializer();
            using (StreamReader sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (JsonTextReader reader = new JsonTextReader(sr))
            {
                var managedSiteList = serializer.Deserialize<List<ManagedSite>>(reader);
                return managedSiteList;
            }
        }

        public async Task<List<ManagedSite>> GetManagedSite(string managedSiteId)
        {
            throw new NotImplementedException();
        }

        public Task<ManagedSite> UpdateManagedSite(ManagedSite site)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> DeleteManagedSite(string managedSiteId)
        {
            throw new NotImplementedException();
        }

        public Task<APIResult> RevokeManageSiteCertificate(string managedSiteId)
        {
            throw new NotImplementedException();
        }

        public Task<List<ManagedSite>> BeginAutoRenewal()
        {
            throw new NotImplementedException();
        }

        public Task BeginCertificateRequest(string managedSiteId)
        {
            throw new NotImplementedException();
        }

        public Task<string> CheckCertificateRequest(string managedSiteId)
        {
            throw new NotImplementedException();
        }

        public Task<APIResult> TestChallengeConfiguration(ManagedSite site)
        {
            throw new NotImplementedException();
        }

        #endregion Managed Sites

        #region Contacts

        public async Task<string> GetPrimaryContact()
        {
            var result = await FetchAsync("contacts/primary");
            return JsonConvert.DeserializeObject<string>(result);
        }

        public async Task<bool> SetPrimaryContact(ContactRegistration contact)
        {
            var result = await PostAsync("contacts/primary", contact);
            return JsonConvert.DeserializeObject<bool>(await result.Content.ReadAsStringAsync());
        }

        #endregion Contacts
    }
}