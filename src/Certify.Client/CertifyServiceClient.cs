using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;

namespace Certify.Client
{
    public class ServiceCommsException : Exception
    {
        public ServiceCommsException()
        {
        }

        public ServiceCommsException(string message)
            : base(message)
        {
        }

        public ServiceCommsException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    // This version of the client communicates with the Certify.Service instance on the local machine
    public class CertifyServiceClient : ICertifyClient
    {
        private HttpClient _client;

        public event Action<RequestProgressState> OnRequestProgressStateUpdated;

        public event Action<ManagedCertificate> OnManagedCertificateUpdated;

        public event Action<string, string> OnMessageFromService;

        public event Action OnConnectionReconnecting;

        public event Action OnConnectionReconnected;

        public event Action OnConnectionClosed;

        private IHubProxy hubProxy;
        private HubConnection connection;

        private string _statusHubUri = "/api/status";
        private string _baseUri = "/api/";

        public CertifyServiceClient()
        {
            var serviceConfig = Certify.SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            _baseUri = $"http://{serviceConfig.Host}:{serviceConfig.Port}" + _baseUri;
            _statusHubUri = $"http://{serviceConfig.Host}:{serviceConfig.Port}" + _statusHubUri;

            // use windows authentication
            _client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            _client.DefaultRequestHeaders.Add("User-Agent", "Certify/App");
            _client.Timeout = new TimeSpan(0, 20, 0); // 20 min timeout on service api calls
        }

        public Shared.ServiceConfig GetAppServiceConfig()
        {
            return Certify.SharedUtils.ServiceConfigManager.GetAppServiceConfig();
        }

        public async Task ConnectStatusStreamAsync()
        {
            connection = new HubConnection(_statusHubUri);
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

        private async Task<string> FetchAsync(string endpoint)
        {
            var response = await _client.GetAsync(_baseUri + endpoint);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
            else
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new ServiceCommsException($"Internal Service Error: {endpoint}: {error} ");
            }
        }

        private async Task<HttpResponseMessage> PostAsync(string endpoint, object data)
        {
            if (data != null)
            {
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, System.Text.UnicodeEncoding.UTF8, "application/json");
                var response = await _client.PostAsync(_baseUri + endpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    throw new ServiceCommsException($"Internal Service Error: {endpoint}: {error}");
                }
            }
            else
            {
                var response = await _client.PostAsync(_baseUri + endpoint, new StringContent(""));
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    throw new ServiceCommsException($"Internal Service Error: {endpoint}: {error}");
                }
            }
        }

        private async Task<HttpResponseMessage> DeleteAsync(string endpoint)
        {
            var response = await _client.DeleteAsync(_baseUri + endpoint);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }
            else
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new ServiceCommsException($"Internal Service Error: {endpoint}: {error}");
            }
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

        public async Task<List<BindingInfo>> GetServerSiteList(StandardServerTypes serverType)
        {
            var result = await FetchAsync($"server/sitelist/{serverType}");
            return JsonConvert.DeserializeObject<List<BindingInfo>>(result);
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

        public async Task<List<ActionStep>> RunConfigurationDiagnostics(StandardServerTypes serverType, string serverSiteId)
        {
            var results = await FetchAsync($"server/diagnostics/{serverType}/{serverSiteId}");
            return JsonConvert.DeserializeObject<List<ActionStep>>(results);
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

        public async Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused)
        {
            try
            {
                var response = await FetchAsync($"managedcertificates/renewcert/{managedItemId}/{resumePaused}");
                return JsonConvert.DeserializeObject<CertificateRequestResult>(response);
            }
            catch (Exception exp) {
                return new CertificateRequestResult
                {
                    IsSuccess = false,
                    Message = exp.ToString(),
                    Result = exp
                };
            }
        }

        public async Task<RequestProgressState> CheckCertificateRequest(string managedItemId)
        {
            string json = await FetchAsync($"managedcertificates/requeststatus/{managedItemId}");
            return JsonConvert.DeserializeObject<RequestProgressState>(json);
        }

        public async Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate site)
        {
            var response = await PostAsync($"managedcertificates/testconfig", site);
            return JsonConvert.DeserializeObject<List<StatusMessage>>(await response.Content.ReadAsStringAsync());
        }

        public async Task<List<Models.Providers.DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId)
        {
            string json = await FetchAsync($"managedcertificates/dnszones/{providerTypeId}/{credentialsId}");
            return JsonConvert.DeserializeObject<List<Models.Providers.DnsZone>>(json);
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

        public async Task<List<ProviderDefinition>> GetChallengeAPIList()
        {
            var response = await FetchAsync($"managedcertificates/challengeapis/");
            return JsonConvert.DeserializeObject<List<ProviderDefinition>>(response);
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

        public async Task<ActionResult> TestCredentials(string credentialKey)
        {
            var result = await PostAsync($"credentials/{credentialKey}/test", new { });
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        #endregion Contacts
    }
}
