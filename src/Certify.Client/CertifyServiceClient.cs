using Certify.Models;
using Microsoft.AspNet.SignalR.Client;
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
#if DEBUG
        private string _baseUri = Certify.Locales.ConfigResources.LocalServiceBaseURIDebug + "/api/";
#else
        private string _baseUri = Certify.Locales.ConfigResources.LocalServiceBaseURI + "/api/";
#endif

        #region Status (SignalR)

        public event Action<RequestProgressState> OnRequestProgressStateUpdated;

        public event Action<ManagedSite> OnManagedSiteUpdated;

        public event Action<string, string> OnMessageFromService;

        public event Action OnConnectionReconnecting;

        public event Action OnConnectionReconnected;

        public event Action OnConnectionClosed;

        private IHubProxy hubProxy;
        private HubConnection connection;

#if DEBUG
        private string url = Certify.Locales.ConfigResources.LocalServiceBaseURIDebug + "/api/status";
#else
        private string url = Certify.Locales.ConfigResources.LocalServiceBaseURI + "/api/status";
#endif

        public async Task ConnectStatusStreamAsync()
        {
            connection = new HubConnection(url);
            hubProxy = connection.CreateHubProxy("StatusHub");

            hubProxy.On<ManagedSite>("ManagedSiteUpdated", (u) => OnManagedSiteUpdated?.Invoke(u));
            hubProxy.On<RequestProgressState>("SendRequestProgressState", (s) => OnRequestProgressStateUpdated?.Invoke(s));
            hubProxy.On<string, string>("SendMessage", (a, b) => OnMessageFromService?.Invoke(a, b));

            connection.Reconnecting += OnConnectionReconnecting;
            connection.Reconnected += OnConnectionReconnected;
            connection.Closed += OnConnectionClosed;

            await connection.Start();
        }

        #endregion Status (SignalR)

        private async Task<string> FetchAsync(string endpoint)
        {
            var response = await _client.GetAsync(_baseUri + endpoint);
            return await response.Content.ReadAsStringAsync();
        }

        private async Task<HttpResponseMessage> PostAsync(string endpoint, object data)
        {
            if (data != null)
            {
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, System.Text.UnicodeEncoding.UTF8, "application/json");
                return await _client.PostAsync(_baseUri + endpoint, content);
            }
            else
            {
                return await _client.PostAsync(_baseUri + endpoint, new StringContent(""));
            }
        }

        private async Task<HttpResponseMessage> DeleteAsync(string endpoint)
        {
            return await _client.DeleteAsync(_baseUri + endpoint);
        }

        #region System

        public async Task<string> GetAppVersion()
        {
            return await FetchAsync("system/appversion");
        }

        public async Task<UpdateCheck> CheckForUpdates()
        {
            try
            {
                var result = await FetchAsync("system/updatecheck");
                return JsonConvert.DeserializeObject<UpdateCheck>(result);
            }
            catch (Exception)
            {
                //could not check for updates
                return null;
            }
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

        public async Task<System.Version> GetServerVersion(StandardServerTypes serverType)
        {
            var result = await FetchAsync($"server/version/{serverType}");

            var versionString = JsonConvert.DeserializeObject<string>(result, new Newtonsoft.Json.Converters.VersionConverter());
            var version = Version.Parse(versionString);
            return version;
        }

        public async Task<List<DomainOption>> GetServerSiteDomains(StandardServerTypes serverType, string serverSiteId)
        {
            var result = await FetchAsync($"server/sitedomains/{serverType}/{serverSiteId}");
            return JsonConvert.DeserializeObject<List<DomainOption>>(result);
        }

        #endregion Server

        #region Preferences

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

        #endregion Preferences

        #region Managed Sites

        public async Task<List<ManagedSite>> GetManagedSites(ManagedSiteFilter filter)
        {
            var response = await PostAsync("managedsites/search/", filter);
            var sites = await response.Content.ReadAsStringAsync();
            var serializer = new JsonSerializer();
            using (StreamReader sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (JsonTextReader reader = new JsonTextReader(sr))
            {
                var managedSiteList = serializer.Deserialize<List<ManagedSite>>(reader);
                foreach (var s in managedSiteList) s.IsChanged = false;
                return managedSiteList;
            }
        }

        public async Task<ManagedSite> GetManagedSite(string managedSiteId)
        {
            var result = await FetchAsync($"managedsites/{managedSiteId}");
            var site = JsonConvert.DeserializeObject<ManagedSite>(result);
            site.IsChanged = false;
            return site;
        }

        public async Task<ManagedSite> UpdateManagedSite(ManagedSite site)
        {
            var response = await PostAsync("managedsites/update", site);
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ManagedSite>(json);
        }

        public async Task<bool> DeleteManagedSite(string managedSiteId)
        {
            var response = await DeleteAsync($"managedsites/delete/{managedSiteId}");
            return JsonConvert.DeserializeObject<bool>(await response.Content.ReadAsStringAsync());
        }

        public async Task<APIResult> RevokeManageSiteCertificate(string managedSiteId)
        {
            var response = await FetchAsync($"managedsites/revoke/{managedSiteId}");
            return JsonConvert.DeserializeObject<APIResult>(response);
        }

        public async Task<List<CertificateRequestResult>> BeginAutoRenewal()
        {
            var response = await PostAsync("managedsites/autorenew", null);
            var serializer = new JsonSerializer();
            using (StreamReader sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (JsonTextReader reader = new JsonTextReader(sr))
            {
                var results = serializer.Deserialize<List<CertificateRequestResult>>(reader);
                return results;
            }
        }

        public async Task<bool> BeginCertificateRequest(string managedSiteId)
        {
            var response = await FetchAsync($"managedsites/renewcert/{managedSiteId}");
            return JsonConvert.DeserializeObject<bool>(response);
        }

        public async Task<RequestProgressState> CheckCertificateRequest(string managedSiteId)
        {
            string json = await FetchAsync($"managedsites/requeststatus/{managedSiteId}");
            return JsonConvert.DeserializeObject<RequestProgressState>(json);
        }

        public async Task<APIResult> TestChallengeConfiguration(ManagedSite site)
        {
            var response = await PostAsync($"managedsites/testconfig", site);
            return JsonConvert.DeserializeObject<APIResult>(await response.Content.ReadAsStringAsync());
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