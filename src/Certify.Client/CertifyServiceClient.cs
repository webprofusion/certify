using Certify.Models;
using Certify.Models.Config;
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
        private HttpClient _client;
#if DEBUG
        private string _baseUri = Certify.Locales.ConfigResources.LocalServiceBaseURIDebug + "/api/";
#else
        private string _baseUri = Certify.Locales.ConfigResources.LocalServiceBaseURI + "/api/";
#endif

        #region Status (SignalR)

        public event Action<RequestProgressState> OnRequestProgressStateUpdated;

        public event Action<ManagedCertificate> OnManagedCertificateUpdated;

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

        public CertifyServiceClient()
        {
            // use windows authentication
            _client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            _client.Timeout = new TimeSpan(0, 20, 0); // 20 min timeout on service api calls
        }

        public async Task ConnectStatusStreamAsync()
        {
            connection = new HubConnection(url);
            connection.Credentials = System.Net.CredentialCache.DefaultCredentials;
            hubProxy = connection.CreateHubProxy("StatusHub");

            hubProxy.On<ManagedCertificate>("ManagedCertificateUpdated", (u) => OnManagedCertificateUpdated?.Invoke(u));
            hubProxy.On<RequestProgressState>("RequestProgressStateUpdated", (s) => OnRequestProgressStateUpdated?.Invoke(s));
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

        #region Managed Certificates

        public async Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter)
        {
            var response = await PostAsync("managedcertificates/search/", filter);
            var sites = await response.Content.ReadAsStringAsync();
            var serializer = new JsonSerializer();
            using (StreamReader sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (JsonTextReader reader = new JsonTextReader(sr))
            {
                var managedCertificateList = serializer.Deserialize<List<ManagedCertificate>>(reader);
                foreach (var s in managedCertificateList)
                {
                    s.IsChanged = false;
                }
                return managedCertificateList;
            }
        }

        public async Task<ManagedCertificate> GetManagedCertificate(string managedItemId)
        {
            var result = await FetchAsync($"managedcertificates/{managedItemId}");
            var site = JsonConvert.DeserializeObject<ManagedCertificate>(result);
            if (site != null) site.IsChanged = false;
            return site;
        }

        public async Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site)
        {
            var response = await PostAsync("managedcertificates/update", site);
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ManagedCertificate>(json);
        }

        public async Task<bool> DeleteManagedCertificate(string managedItemId)
        {
            var response = await DeleteAsync($"managedcertificates/delete/{managedItemId}");
            return JsonConvert.DeserializeObject<bool>(await response.Content.ReadAsStringAsync());
        }

        public async Task<StatusMessage> RevokeManageSiteCertificate(string managedItemId)
        {
            var response = await FetchAsync($"managedcertificates/revoke/{managedItemId}");
            return JsonConvert.DeserializeObject<StatusMessage>(response);
        }

        public async Task<List<CertificateRequestResult>> BeginAutoRenewal()
        {
            var response = await PostAsync("managedcertificates/autorenew", null);
            var serializer = new JsonSerializer();
            using (StreamReader sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (JsonTextReader reader = new JsonTextReader(sr))
            {
                var results = serializer.Deserialize<List<CertificateRequestResult>>(reader);
                return results;
            }
        }

        public async Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId)
        {
            var response = await FetchAsync($"managedcertificates/renewcert/{managedItemId}");
            return JsonConvert.DeserializeObject<CertificateRequestResult>(response);
        }

        public async Task<RequestProgressState> CheckCertificateRequest(string managedItemId)
        {
            string json = await FetchAsync($"managedcertificates/requeststatus/{managedItemId}");
            return JsonConvert.DeserializeObject<RequestProgressState>(json);
        }

        public async Task<StatusMessage> TestChallengeConfiguration(ManagedCertificate site)
        {
            var response = await PostAsync($"managedcertificates/testconfig", site);
            return JsonConvert.DeserializeObject<StatusMessage>(await response.Content.ReadAsStringAsync());
        }

        public async Task<List<ActionStep>> PreviewActions(ManagedCertificate site)
        {
            var response = await PostAsync($"managedcertificates/preview", site);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await response.Content.ReadAsStringAsync());
        }

        public async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly)
        {
            var response = await FetchAsync($"managedcertificates/reapply/{managedItemId}/{isPreviewOnly}");
            return JsonConvert.DeserializeObject<CertificateRequestResult>(response);
        }

        #endregion Managed Certificates

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

        public async Task<List<StoredCredential>> GetCredentials()
        {
            var result = await FetchAsync("credentials");
            return JsonConvert.DeserializeObject<List<StoredCredential>>(result);
        }

        public async Task<StoredCredential> UpdateCredentials(StoredCredential credential)
        {
            var result = await PostAsync("credentials", credential);
            return JsonConvert.DeserializeObject<StoredCredential>(await result.Content.ReadAsStringAsync());
        }

        public async Task<bool> DeleteCredential(string credentialKey)
        {
            var result = await DeleteAsync($"credentials/{credentialKey}");
            return JsonConvert.DeserializeObject<bool>(await result.Content.ReadAsStringAsync());
        }

        #endregion Contacts
    }
}