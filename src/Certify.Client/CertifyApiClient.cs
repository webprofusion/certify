using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config.Migration;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Utils;
using Certify.Shared;
using Newtonsoft.Json;
using Polly;

namespace Certify.Client
{
    public class ServiceCommsException : Exception
    {
        public bool IsAccessDenied { get; set; } = false;

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

    public class HttpRetryMessageHandler : DelegatingHandler
    {
        public HttpRetryMessageHandler(HttpClientHandler handler) : base(handler) { }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Policy
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .OrResult<HttpResponseMessage>(x => !x.IsSuccessStatusCode && x.StatusCode != HttpStatusCode.BadRequest)
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(1))
                .ExecuteAsync(() =>
                    base.SendAsync(request, cancellationToken)
                );
    }

    // This version of the client communicates with the Certify.Service instance on the local machine
    public class CertifyApiClient : ICertifyInternalApiClient
    {
        private HttpClient _client;
        private readonly string _baseUri = "/api/";
        internal Shared.ServerConnection _connectionConfig;
        internal Providers.IServiceConfigProvider _configProvider;

        internal string _authKey { get; set; } = "";
        internal string _accessToken { get; set; } = "";
        internal string _refreshToken { get; set; } = "";

        public CertifyApiClient(Providers.IServiceConfigProvider configProvider, Shared.ServerConnection config = null)
        {
            _configProvider = configProvider;
            _connectionConfig = config ?? GetDefaultServerConnection();

            _baseUri = $"{(_connectionConfig.UseHTTPS ? "https" : "http")}://{_connectionConfig.Host}:{_connectionConfig.Port}" + _baseUri;

            CreateHttpClient();

        }

        private void CreateHttpClient()
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            var _httpClientHandler = new HttpClientHandler();

            if (_connectionConfig.UseHTTPS && _connectionConfig.AllowUntrusted)
            {
                // ignore all cert errors when validating URL response
                _httpClientHandler.ServerCertificateCustomValidationCallback =
                   (message, certificate, chain, sslPolicyErrors) => true;
            }

            if (_connectionConfig.Authentication == "default")
            {
                // use windows authentication
                _httpClientHandler.UseDefaultCredentials = true;

                _client = new HttpClient(new HttpRetryMessageHandler(_httpClientHandler));
            }
            else
            {
                //alternative auth (jwt)
                _client = new HttpClient(new HttpRetryMessageHandler(_httpClientHandler));

                if (!string.IsNullOrEmpty(_accessToken))
                {
                    SetClientAuthorizationBearerToken();
                }
            }

            _client.DefaultRequestHeaders.Add("User-Agent", "Certify/App");
            _client.Timeout = new TimeSpan(0, 20, 0); // 20 min timeout on service api calls
        }

        private void SetClientAuthorizationBearerToken()
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        public ServerConnection GetDefaultServerConnection()
        {
            var serviceCfg = _configProvider.GetServiceConfig();
            return new ServerConnection(serviceCfg);
        }

        public void SetConnectionAuthMode(string mode)
        {
            _connectionConfig.Authentication = mode;

            CreateHttpClient();
        }

        private async Task<string> FetchAsync(string endpoint)
        {
            try
            {
                var response = await _client.GetAsync(_baseUri + endpoint);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new ServiceCommsException($"Internal Service Error: {endpoint}: {error} ");
                }
            }
            catch (HttpRequestException exp)
            {
                throw new ServiceCommsException($"Failed to communicate with service: {_baseUri}{endpoint}: {exp} ");
            }
        }

        private async Task<HttpResponseMessage> PostAsync(string endpoint, object data)
        {
            if (data != null)
            {
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, System.Text.UnicodeEncoding.UTF8, "application/json");
                try
                {

                    var response = await _client.PostAsync(_baseUri + endpoint, content);
                    if (response.IsSuccessStatusCode)
                    {
                        return response;
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            throw new ServiceCommsException($"API Access Denied: {endpoint}: {error}");
                        }
                        else
                        {
                            throw new ServiceCommsException($"Internal Service Error: {endpoint}: {error}");
                        }
                    }
                }
                catch (HttpRequestException exp)
                {
                    throw new ServiceCommsException($"Internal Service Error: {endpoint}: {exp}");
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
                    var error = await response.Content.ReadAsStringAsync();
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
                var error = await response.Content.ReadAsStringAsync();
                throw new ServiceCommsException($"Internal Service Error: {endpoint}: {error}");
            }
        }

        #region System

        public async Task<string> GetAppVersion() => await FetchAsync("system/appversion");

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

        public async Task<List<ActionResult>> PerformServiceDiagnostics()
        {
            var result = await FetchAsync("system/diagnostics");
            return JsonConvert.DeserializeObject<List<ActionResult>>(result);
        }

        public async Task<ImportExportPackage> PerformExport(ExportRequest exportRequest)
        {
            var result = await PostAsync("system/migration/export", exportRequest);
            return JsonConvert.DeserializeObject<ImportExportPackage>(await result.Content.ReadAsStringAsync());
        }

        public async Task<List<ActionStep>> PerformImport(ImportRequest importRequest)
        {
            var result = await PostAsync("system/migration/import", importRequest);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await result.Content.ReadAsStringAsync());
        }
        public async Task<List<ActionStep>> SetDefaultDataStore(string dataStoreId)
        {
            var result = await PostAsync($"system/datastores/setdefault/{dataStoreId}", null);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await result.Content.ReadAsStringAsync());
        }
        public async Task<List<ProviderDefinition>> GetDataStoreProviders()
        {
            var result = await FetchAsync("system/datastores/providers");
            return JsonConvert.DeserializeObject<List<ProviderDefinition>>(result);
        }
        public async Task<List<DataStoreConnection>> GetDataStoreConnections()
        {
            var result = await FetchAsync("system/datastores/");
            return JsonConvert.DeserializeObject<List<DataStoreConnection>>(result);
        }
        public async Task<List<ActionStep>> CopyDataStore(string sourceId, string targetId)
        {
            var result = await PostAsync($"system/datastores/copy/{sourceId}/{targetId}", null);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await result.Content.ReadAsStringAsync());
        }

        public async Task<List<ActionStep>> UpdateDataStoreConnection(DataStoreConnection dataStoreConnection)
        {
            var result = await PostAsync($"system/datastores/update", dataStoreConnection);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await result.Content.ReadAsStringAsync());
        }

        public async Task<List<ActionStep>> TestDataStoreConnection(DataStoreConnection dataStoreConnection)
        {
            var result = await PostAsync($"system/datastores/test", dataStoreConnection);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await result.Content.ReadAsStringAsync());
        }
        #endregion System

        #region Server

        public async Task<bool> IsServerAvailable(StandardServerTypes serverType)
        {
            var result = await FetchAsync($"server/isavailable/{serverType}");
            return bool.Parse(result);
        }

        public async Task<List<SiteInfo>> GetServerSiteList(StandardServerTypes serverType, string itemId = null)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                var result = await FetchAsync($"server/sitelist/{serverType}");
                return JsonConvert.DeserializeObject<List<SiteInfo>>(result);
            }
            else
            {
                var result = await FetchAsync($"server/sitelist/{serverType}/{itemId}");
                return JsonConvert.DeserializeObject<List<SiteInfo>>(result);
            }
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
            _ = await PostAsync("preferences/", preferences);
            return true;
        }

        #endregion Preferences

        #region Managed Certificates

        public async Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter)
        {
            var response = await PostAsync("managedcertificates/search/", filter);
            var serializer = new JsonSerializer();

            using (var sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (var reader = new JsonTextReader(sr))
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
            if (site != null)
            {
                site.IsChanged = false;
            }

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

        public async Task<List<CertificateRequestResult>> BeginAutoRenewal(RenewalSettings settings)
        {
            var response = await PostAsync("managedcertificates/autorenew", settings);
            var serializer = new JsonSerializer();
            using (var sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (var reader = new JsonTextReader(sr))
            {
                var results = serializer.Deserialize<List<CertificateRequestResult>>(reader);
                return results;
            }
        }

        public async Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused, bool isInteractive)
        {
            try
            {
                var response = await FetchAsync($"managedcertificates/renewcert/{managedItemId}/{resumePaused}/{isInteractive}");
                return JsonConvert.DeserializeObject<CertificateRequestResult>(response);
            }
            catch (Exception exp)
            {
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
            var json = await FetchAsync($"managedcertificates/requeststatus/{managedItemId}");
            return JsonConvert.DeserializeObject<RequestProgressState>(json);
        }

        public async Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate site)
        {
            var response = await PostAsync($"managedcertificates/testconfig", site);
            return JsonConvert.DeserializeObject<List<StatusMessage>>(await response.Content.ReadAsStringAsync());
        }

        public async Task<List<Models.Providers.DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId)
        {
            var json = await FetchAsync($"managedcertificates/dnszones/{providerTypeId}/{credentialsId}");
            return JsonConvert.DeserializeObject<List<Models.Providers.DnsZone>>(json);
        }

        public async Task<List<ActionStep>> PreviewActions(ManagedCertificate site)
        {
            var response = await PostAsync($"managedcertificates/preview", site);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await response.Content.ReadAsStringAsync());
        }

        public async Task<List<CertificateRequestResult>> RedeployManagedCertificates(bool isPreviewOnly, bool includeDeploymentTasks)
        {
            var response = await FetchAsync($"managedcertificates/redeploy/{isPreviewOnly}/{includeDeploymentTasks}");
            return JsonConvert.DeserializeObject<List<CertificateRequestResult>>(response);
        }

        public async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly, bool includeDeploymentTasks)
        {
            var response = await FetchAsync($"managedcertificates/reapply/{managedItemId}/{isPreviewOnly}/{includeDeploymentTasks}");
            return JsonConvert.DeserializeObject<CertificateRequestResult>(response);
        }

        public async Task<CertificateRequestResult> RefetchCertificate(string managedItemId)
        {
            var response = await FetchAsync($"managedcertificates/fetch/{managedItemId}/{false}");
            return JsonConvert.DeserializeObject<CertificateRequestResult>(response);
        }

        public async Task<List<ChallengeProviderDefinition>> GetChallengeAPIList()
        {
            var response = await FetchAsync($"managedcertificates/challengeapis/");
            return JsonConvert.DeserializeObject<List<ChallengeProviderDefinition>>(response);
        }

        public async Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallenges(string type, string key)
        {
            var result = await FetchAsync($"managedcertificates/currentchallenges/{type}/{key}");
            return JsonConvert.DeserializeObject<List<SimpleAuthorizationChallengeItem>>(result);
        }

        public async Task<List<DeploymentProviderDefinition>> GetDeploymentProviderList()
        {
            var response = await FetchAsync($"managedcertificates/deploymentproviders/");
            return JsonConvert.DeserializeObject<List<DeploymentProviderDefinition>>(response);
        }

        public async Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, Config.DeploymentTaskConfig config)
        {
            var response = await PostAsync($"managedcertificates/deploymentprovider/{id}", config);
            return JsonConvert.DeserializeObject<DeploymentProviderDefinition>(await response.Content.ReadAsStringAsync());
        }

        public async Task<List<ActionStep>> PerformDeployment(string managedCertificateId, string taskId, bool isPreviewOnly, bool forceTaskExecute)
        {
            if (!forceTaskExecute)
            {
                if (string.IsNullOrEmpty(taskId))
                {
                    var response = await FetchAsync($"managedcertificates/performdeployment/{isPreviewOnly}/{managedCertificateId}");
                    return JsonConvert.DeserializeObject<List<ActionStep>>(response);
                }
                else
                {
                    var response = await FetchAsync($"managedcertificates/performdeployment/{isPreviewOnly}/{managedCertificateId}/{taskId}");
                    return JsonConvert.DeserializeObject<List<ActionStep>>(response);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(taskId))
                {
                    var response = await FetchAsync($"managedcertificates/performforceddeployment/{isPreviewOnly}/{managedCertificateId}");
                    return JsonConvert.DeserializeObject<List<ActionStep>>(response);
                }
                else
                {
                    var response = await FetchAsync($"managedcertificates/performforceddeployment/{isPreviewOnly}/{managedCertificateId}/{taskId}");
                    return JsonConvert.DeserializeObject<List<ActionStep>>(response);
                }
            }
        }

        public async Task<List<ActionResult>> ValidateDeploymentTask(DeploymentTaskValidationInfo info)
        {
            var result = await PostAsync($"managedcertificates/validatedeploymenttask", info);
            return JsonConvert.DeserializeObject<List<ActionResult>>(await result.Content.ReadAsStringAsync());
        }

        public async Task<string[]> GetItemLog(string id, int limit)
        {
            var response = await FetchAsync($"managedcertificates/log/{id}/{limit}");
            return JsonConvert.DeserializeObject<string[]>(response);
        }

        public async Task<List<ActionResult>> PerformManagedCertMaintenance(string id = null)
        {
            var result = await FetchAsync($"managedcertificates/maintenance/{id}");
            return JsonConvert.DeserializeObject<List<ActionResult>>(result);
        }

        #endregion Managed Certificates

        #region Accounts

        public async Task<List<CertificateAuthority>> GetCertificateAuthorities()
        {
            var result = await FetchAsync("accounts/authorities");
            return JsonConvert.DeserializeObject<List<CertificateAuthority>>(result);
        }
        public async Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority ca)
        {
            var result = await PostAsync("accounts/authorities", ca);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<ActionResult> DeleteCertificateAuthority(string id)
        {
            var result = await DeleteAsync("accounts/authorities/" + id);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<List<AccountDetails>> GetAccounts()
        {
            var result = await FetchAsync("accounts");
            return JsonConvert.DeserializeObject<List<AccountDetails>>(result);
        }

        public async Task<ActionResult> AddAccount(ContactRegistration contact)
        {
            var result = await PostAsync("accounts", contact);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<ActionResult> UpdateAccountContact(ContactRegistration contact)
        {
            var result = await PostAsync($"accounts/update/{contact.StorageKey}", contact);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<ActionResult> RemoveAccount(string storageKey, bool deactivate)
        {
            var result = await DeleteAsync($"accounts/remove/{storageKey}/{deactivate}");
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<ActionResult> ChangeAccountKey(string storageKey, string newKeyPEM)
        {
            var result = await PostAsync($"accounts/changekey/{storageKey}", new { newKeyPem = newKeyPEM });
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        #endregion

        #region Credentials

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

        #endregion

        #region Auth
        public async Task<string> GetAuthKeyWindows()
        {
            var result = await FetchAsync("auth/windows");
            return JsonConvert.DeserializeObject<string>(result);
        }

        public async Task<string> GetAccessToken(string key)
        {
            var result = await PostAsync("auth/token", new { Key = key });
            _accessToken = JsonConvert.DeserializeObject<string>(await result.Content.ReadAsStringAsync());

            if (!string.IsNullOrEmpty(_accessToken))
            {
                SetClientAuthorizationBearerToken();
            }

            return _accessToken;
        }

        public async Task<string> GetAccessToken(string username, string password)
        {
            var result = await PostAsync("auth/token", new { Username = username, Password = password });
            _accessToken = JsonConvert.DeserializeObject<string>(await result.Content.ReadAsStringAsync());

            if (!string.IsNullOrEmpty(_accessToken))
            {
                SetClientAuthorizationBearerToken();
            }

            return _accessToken;
        }

        public async Task<string> RefreshAccessToken()
        {
            var result = await PostAsync("auth/refresh", new { Token = _accessToken });
            _accessToken = JsonConvert.DeserializeObject<string>(await result.Content.ReadAsStringAsync());

            if (!string.IsNullOrEmpty(_accessToken))
            {
                SetClientAuthorizationBearerToken();
            }

            return _refreshToken;
        }

        #endregion
    }
}
