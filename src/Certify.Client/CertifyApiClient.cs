using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.API;
using Certify.Models.Config;
using Certify.Models.Config.AccessControl;
using Certify.Models.Reporting;
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
                .OrResult<HttpResponseMessage>(x => !x.IsSuccessStatusCode && x.StatusCode != HttpStatusCode.BadRequest && x.StatusCode != HttpStatusCode.InternalServerError)
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(1))
                .ExecuteAsync(() =>
                    base.SendAsync(request, cancellationToken)
                );
    }

    public class AuthContext
    {
        public string UserId { get; set; }
        public string Token { get; set; }
    }

    // This version of the client communicates with the Certify.Service instance on the local machine
    public partial class CertifyApiClient : ICertifyInternalApiClient
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

        private void SetAuthContextForRequest(HttpRequestMessage request, AuthContext authContext)
        {
            if (authContext != null)
            {
                request.Headers.Add("X-Context-User-Id", authContext.UserId);
            }
        }

        private async Task<string> FetchAsync(string endpoint, AuthContext authContext)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri + endpoint)))
                {
                    SetAuthContextForRequest(request, authContext);

                    var response = await _client.SendAsync(request);

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
            }
            catch (HttpRequestException exp)
            {
                throw new ServiceCommsException($"Failed to communicate with service: {_baseUri}{endpoint}: {exp} ");
            }
        }

        public class ServerErrorMsg
        {
            public string Message;
        }

        private async Task<HttpResponseMessage> PostAsync(string endpoint, object data, AuthContext authContext)
        {
            if (data != null)
            {
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, System.Text.UnicodeEncoding.UTF8, "application/json");
                try
                {

                    using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri + endpoint)))
                    {
                        SetAuthContextForRequest(request, authContext);

                        request.Content = content;

                        var response = await _client.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            return response;
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                            if (response.StatusCode == HttpStatusCode.Unauthorized)
                            {
                                throw new ServiceCommsException($"API Access Denied: {endpoint}: {error}");
                            }
                            else
                            {

                                if (response.StatusCode == HttpStatusCode.InternalServerError && error.Contains("\"message\""))
                                {
                                    var err = JsonConvert.DeserializeObject<ServerErrorMsg>(error);
                                    throw new ServiceCommsException($"Internal Service Error: {endpoint}: {err.Message}");
                                }
                                else
                                {
                                    throw new ServiceCommsException($"Internal Service Error: {endpoint}: {error}");
                                }
                            }
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

        private async Task<HttpResponseMessage> DeleteAsync(string endpoint, AuthContext authContext)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseUri + endpoint)))
            {
                SetAuthContextForRequest(request, authContext);

                var response = await _client.SendAsync(request);

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

        #region System

        public async Task<string> GetAppVersion(AuthContext authContext = null) => await FetchAsync("system/appversion", authContext);

        public async Task<UpdateCheck> CheckForUpdates(AuthContext authContext = null)
        {
            try
            {
                var result = await FetchAsync("system/updatecheck", authContext);
                return JsonConvert.DeserializeObject<UpdateCheck>(result);
            }
            catch (Exception)
            {
                //could not check for updates
                return null;
            }
        }

        public async Task<List<ActionResult>> PerformServiceDiagnostics(AuthContext authContext = null)
        {
            var result = await FetchAsync("system/diagnostics", authContext);
            return JsonConvert.DeserializeObject<List<ActionResult>>(result);
        }

        public async Task<List<ActionStep>> SetDefaultDataStore(string dataStoreId, AuthContext authContext = null)
        {
            var result = await PostAsync($"system/datastores/setdefault/{dataStoreId}", null, authContext);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await result.Content.ReadAsStringAsync());
        }

        public async Task<List<ProviderDefinition>> GetDataStoreProviders(AuthContext authContext = null)
        {
            var result = await FetchAsync("system/datastores/providers", authContext);
            return JsonConvert.DeserializeObject<List<ProviderDefinition>>(result);
        }
        
        public async Task<List<DataStoreConnection>> GetDataStoreConnections(AuthContext authContext = null)
        {
            var result = await FetchAsync("system/datastores/", authContext);
            return JsonConvert.DeserializeObject<List<DataStoreConnection>>(result);
        }
        
        public async Task<List<ActionStep>> CopyDataStore(string sourceId, string targetId, AuthContext authContext = null)
        {
            var result = await PostAsync($"system/datastores/copy/{sourceId}/{targetId}", null, authContext: authContext);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await result.Content.ReadAsStringAsync());
        }

        public async Task<List<ActionStep>> UpdateDataStoreConnection(DataStoreConnection dataStoreConnection, AuthContext authContext = null)
        {
            var result = await PostAsync($"system/datastores/update", dataStoreConnection, authContext);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await result.Content.ReadAsStringAsync());
        }

        public async Task<List<ActionStep>> TestDataStoreConnection(DataStoreConnection dataStoreConnection, AuthContext authContext = null)
        {
            var result = await PostAsync($"system/datastores/test", dataStoreConnection, authContext);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await result.Content.ReadAsStringAsync());
        }
        #endregion System

        #region Server

        public async Task<bool> IsServerAvailable(StandardServerTypes serverType, AuthContext authContext = null)
        {
            var result = await FetchAsync($"server/isavailable/{serverType}", authContext);
            return bool.Parse(result);
        }

        public async Task<List<SiteInfo>> GetServerSiteList(StandardServerTypes serverType, string itemId = null, AuthContext authContext = null)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                var result = await FetchAsync($"server/sitelist/{serverType}", authContext);
                return JsonConvert.DeserializeObject<List<SiteInfo>>(result);
            }
            else
            {
                var result = await FetchAsync($"server/sitelist/{serverType}/{itemId}", authContext);
                return JsonConvert.DeserializeObject<List<SiteInfo>>(result);
            }
        }

        public async Task<System.Version> GetServerVersion(StandardServerTypes serverType, AuthContext authContext = null)
        {
            var result = await FetchAsync($"server/version/{serverType}", authContext);

            var versionString = JsonConvert.DeserializeObject<string>(result, new Newtonsoft.Json.Converters.VersionConverter());
            var version = Version.Parse(versionString);
            return version;
        }

        public async Task<List<DomainOption>> GetServerSiteDomains(StandardServerTypes serverType, string serverSiteId, AuthContext authContext = null)
        {
            var result = await FetchAsync($"server/sitedomains/{serverType}/{serverSiteId}", authContext);
            return JsonConvert.DeserializeObject<List<DomainOption>>(result);
        }

        public async Task<List<ActionStep>> RunConfigurationDiagnostics(StandardServerTypes serverType, string serverSiteId, AuthContext authContext = null)
        {
            var results = await FetchAsync($"server/diagnostics/{serverType}/{serverSiteId}", authContext);
            return JsonConvert.DeserializeObject<List<ActionStep>>(results);
        }

        #endregion Server

        #region Preferences

        public async Task<Preferences> GetPreferences(AuthContext authContext = null)
        {
            var result = await FetchAsync("preferences/", authContext);
            return JsonConvert.DeserializeObject<Preferences>(result);
        }

        public async Task<bool> SetPreferences(Preferences preferences, AuthContext authContext = null)
        {
            _ = await PostAsync("preferences/", preferences, authContext);
            return true;
        }

        #endregion Preferences

        #region Managed Certificates

        public async Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter, AuthContext authContext = null)
        {
            var response = await PostAsync("managedcertificates/search/", filter, authContext);
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

        /// <summary>
        /// Get search results, same as GetManagedCertificates but result has count of total results available as used when paging
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public async Task<ManagedCertificateSearchResult> GetManagedCertificateSearchResult(ManagedCertificateFilter filter, AuthContext authContext = null)
        {
            var response = await PostAsync("managedcertificates/results/", filter, authContext);
            var serializer = new JsonSerializer();

            using (var sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (var reader = new JsonTextReader(sr))
            {
                var result = serializer.Deserialize<ManagedCertificateSearchResult>(reader);
                return result;
            }
        }

        public async Task<Summary> GetManagedCertificateSummary(ManagedCertificateFilter filter, AuthContext authContext = null)
        {
            var response = await PostAsync("managedcertificates/summary/", filter, authContext);
            var serializer = new JsonSerializer();

            using (var sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (var reader = new JsonTextReader(sr))
            {
                var result = serializer.Deserialize<Summary>(reader);
                return result;
            }
        }

        public async Task<ManagedCertificate> GetManagedCertificate(string managedItemId, AuthContext authContext = null)
        {
            var result = await FetchAsync($"managedcertificates/{managedItemId}", authContext);
            var site = JsonConvert.DeserializeObject<ManagedCertificate>(result);
            if (site != null)
            {
                site.IsChanged = false;
            }

            return site;
        }

        public async Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site, AuthContext authContext = null)
        {
            var response = await PostAsync("managedcertificates/update", site, authContext);
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ManagedCertificate>(json);
        }

        public async Task<bool> DeleteManagedCertificate(string managedItemId, AuthContext authContext = null)
        {
            var response = await DeleteAsync($"managedcertificates/delete/{managedItemId}", authContext);
            return JsonConvert.DeserializeObject<bool>(await response.Content.ReadAsStringAsync());
        }

        public async Task<StatusMessage> RevokeManageSiteCertificate(string managedItemId, AuthContext authContext = null)
        {
            var response = await FetchAsync($"managedcertificates/revoke/{managedItemId}", authContext);
            return JsonConvert.DeserializeObject<StatusMessage>(response);
        }

        public async Task<List<CertificateRequestResult>> BeginAutoRenewal(RenewalSettings settings, AuthContext authContext)
        {
            var response = await PostAsync("managedcertificates/autorenew", settings, authContext);
            var serializer = new JsonSerializer();
            using (var sr = new StreamReader(await response.Content.ReadAsStreamAsync()))
            using (var reader = new JsonTextReader(sr))
            {
                var results = serializer.Deserialize<List<CertificateRequestResult>>(reader);
                return results;
            }
        }

        public async Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused, bool isInteractive, AuthContext authContext = null)
        {
            try
            {
                var response = await FetchAsync($"managedcertificates/renewcert/{managedItemId}/{resumePaused}/{isInteractive}", authContext);
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

        public async Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate site, AuthContext authContext = null)
        {
            var response = await PostAsync($"managedcertificates/testconfig", site, authContext);
            return JsonConvert.DeserializeObject<List<StatusMessage>>(await response.Content.ReadAsStringAsync());
        }
        public async Task<List<StatusMessage>> PerformChallengeCleanup(ManagedCertificate site, AuthContext authContext = null)
        {
            var response = await PostAsync($"managedcertificates/challengecleanup", site, authContext);
            return JsonConvert.DeserializeObject<List<StatusMessage>>(await response.Content.ReadAsStringAsync());
        }
        public async Task<List<Models.Providers.DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId, AuthContext authContext = null)
        {
            var json = await FetchAsync($"managedcertificates/dnszones/{providerTypeId}/{credentialsId}", authContext);
            return JsonConvert.DeserializeObject<List<Models.Providers.DnsZone>>(json);
        }

        public async Task<List<ActionStep>> PreviewActions(ManagedCertificate site, AuthContext authContext = null)
        {
            var response = await PostAsync($"managedcertificates/preview", site, authContext);
            return JsonConvert.DeserializeObject<List<ActionStep>>(await response.Content.ReadAsStringAsync());
        }

        public async Task<List<CertificateRequestResult>> RedeployManagedCertificates(bool isPreviewOnly, bool includeDeploymentTasks, AuthContext authContext = null)
        {
            var response = await FetchAsync($"managedcertificates/redeploy/{isPreviewOnly}/{includeDeploymentTasks}", authContext);
            return JsonConvert.DeserializeObject<List<CertificateRequestResult>>(response);
        }

        public async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly, bool includeDeploymentTasks, AuthContext authContext = null)
        {
            var response = await FetchAsync($"managedcertificates/reapply/{managedItemId}/{isPreviewOnly}/{includeDeploymentTasks}", authContext);
            return JsonConvert.DeserializeObject<CertificateRequestResult>(response);
        }

        public async Task<CertificateRequestResult> RefetchCertificate(string managedItemId, AuthContext authContext = null)
        {
            var response = await FetchAsync($"managedcertificates/fetch/{managedItemId}/{false}", authContext);
            return JsonConvert.DeserializeObject<CertificateRequestResult>(response);
        }

        public async Task<List<ChallengeProviderDefinition>> GetChallengeAPIList(AuthContext authContext = null)
        {
            var response = await FetchAsync($"managedcertificates/challengeapis/", authContext);
            return JsonConvert.DeserializeObject<List<ChallengeProviderDefinition>>(response);
        }

        public async Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallenges(string type, string key, AuthContext authContext = null)
        {
            var result = await FetchAsync($"managedcertificates/currentchallenges/{type}/{key}", authContext);
            return JsonConvert.DeserializeObject<List<SimpleAuthorizationChallengeItem>>(result);
        }

        public async Task<List<DeploymentProviderDefinition>> GetDeploymentProviderList(AuthContext authContext = null)
        {
            var response = await FetchAsync($"managedcertificates/deploymentproviders/", authContext);
            return JsonConvert.DeserializeObject<List<DeploymentProviderDefinition>>(response);
        }

        public async Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, Config.DeploymentTaskConfig config, AuthContext authContext)
        {
            var response = await PostAsync($"managedcertificates/deploymentprovider/{id}", config, authContext);
            return JsonConvert.DeserializeObject<DeploymentProviderDefinition>(await response.Content.ReadAsStringAsync());
        }

        public async Task<List<ActionStep>> PerformDeployment(string managedCertificateId, string taskId, bool isPreviewOnly, bool forceTaskExecute, AuthContext authContext)
        {
            if (!forceTaskExecute)
            {
                if (string.IsNullOrEmpty(taskId))
                {
                    var response = await FetchAsync($"managedcertificates/performdeployment/{isPreviewOnly}/{managedCertificateId}", authContext);
                    return JsonConvert.DeserializeObject<List<ActionStep>>(response);
                }
                else
                {
                    var response = await FetchAsync($"managedcertificates/performdeployment/{isPreviewOnly}/{managedCertificateId}/{taskId}", authContext);
                    return JsonConvert.DeserializeObject<List<ActionStep>>(response);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(taskId))
                {
                    var response = await FetchAsync($"managedcertificates/performforceddeployment/{isPreviewOnly}/{managedCertificateId}", authContext);
                    return JsonConvert.DeserializeObject<List<ActionStep>>(response);
                }
                else
                {
                    var response = await FetchAsync($"managedcertificates/performforceddeployment/{isPreviewOnly}/{managedCertificateId}/{taskId}", authContext);
                    return JsonConvert.DeserializeObject<List<ActionStep>>(response);
                }
            }
        }

        public async Task<List<ActionResult>> ValidateDeploymentTask(DeploymentTaskValidationInfo info, AuthContext authContext = null)
        {
            var result = await PostAsync($"managedcertificates/validatedeploymenttask", info, authContext);
            return JsonConvert.DeserializeObject<List<ActionResult>>(await result.Content.ReadAsStringAsync());
        }

        public async Task<LogItem[]> GetItemLog(string id, int limit, AuthContext authContext = null)
        {
            var response = await FetchAsync($"managedcertificates/log/{id}/{limit}", authContext);
            return JsonConvert.DeserializeObject<LogItem[]>(response);
        }

        public async Task<List<ActionResult>> PerformManagedCertMaintenance(string id = null, AuthContext authContext = null)
        {
            var result = await FetchAsync($"managedcertificates/maintenance/{id}", authContext);
            return JsonConvert.DeserializeObject<List<ActionResult>>(result);
        }

        #endregion Managed Certificates

        #region Accounts

        public async Task<List<CertificateAuthority>> GetCertificateAuthorities(AuthContext authContext = null)
        {
            var result = await FetchAsync("accounts/authorities", authContext);
            return JsonConvert.DeserializeObject<List<CertificateAuthority>>(result);
        }
        public async Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority ca, AuthContext authContext =null)
        {
            var result = await PostAsync("accounts/authorities", ca, authContext);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<ActionResult> DeleteCertificateAuthority(string id, AuthContext authContext = null)
        {
            var result = await DeleteAsync("accounts/authorities/" + id, authContext);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<List<AccountDetails>> GetAccounts(AuthContext authContext = null)
        {
            var result = await FetchAsync("accounts", authContext);
            return JsonConvert.DeserializeObject<List<AccountDetails>>(result);
        }

        public async Task<ActionResult> AddAccount(ContactRegistration contact, AuthContext authContext = null)
        {
            var result = await PostAsync("accounts", contact, authContext);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<ActionResult> UpdateAccountContact(ContactRegistration contact, AuthContext authContext = null)
        {
            var result = await PostAsync($"accounts/update/{contact.StorageKey}", contact, authContext);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<ActionResult> RemoveAccount(string storageKey, bool deactivate, AuthContext authContext = null)
        {
            var result = await DeleteAsync($"accounts/remove/{storageKey}/{deactivate}", authContext);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        public async Task<ActionResult> ChangeAccountKey(string storageKey, string newKeyPEM, AuthContext authContext = null)
        {
            var result = await PostAsync($"accounts/changekey/{storageKey}", new { newKeyPem = newKeyPEM }, authContext);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        #endregion

        #region Credentials

        public async Task<List<StoredCredential>> GetCredentials(AuthContext authContext = null)
        {
            var result = await FetchAsync("credentials", authContext);
            return JsonConvert.DeserializeObject<List<StoredCredential>>(result);
        }

        public async Task<StoredCredential> UpdateCredentials(StoredCredential credential, AuthContext authContext = null)
        {
            var result = await PostAsync("credentials", credential, authContext);
            return JsonConvert.DeserializeObject<StoredCredential>(await result.Content.ReadAsStringAsync());
        }

        public async Task<bool> DeleteCredential(string credentialKey, AuthContext authContext = null)
        {
            var result = await DeleteAsync($"credentials/{credentialKey}", authContext);
            return JsonConvert.DeserializeObject<bool>(await result.Content.ReadAsStringAsync());
        }

        public async Task<ActionResult> TestCredentials(string credentialKey, AuthContext authContext = null)
        {
            var result = await PostAsync($"credentials/{credentialKey}/test", new { }, authContext);
            return JsonConvert.DeserializeObject<ActionResult>(await result.Content.ReadAsStringAsync());
        }

        #endregion

        #region Auth
        public async Task<string> GetAuthKeyWindows(AuthContext authContext)
        {
            var result = await FetchAsync("auth/windows", authContext);
            return JsonConvert.DeserializeObject<string>(result);
        }

        public async Task<string> GetAccessToken(string key, AuthContext authContext)
        {
            var result = await PostAsync("auth/token", new { Key = key }, authContext);
            _accessToken = JsonConvert.DeserializeObject<string>(await result.Content.ReadAsStringAsync());

            if (!string.IsNullOrEmpty(_accessToken))
            {
                SetClientAuthorizationBearerToken();
            }

            return _accessToken;
        }

        public async Task<string> GetAccessToken(string username, string password, AuthContext authContext = null)
        {
            var result = await PostAsync("auth/token", new { Username = username, Password = password }, authContext);
            _accessToken = JsonConvert.DeserializeObject<string>(await result.Content.ReadAsStringAsync());

            if (!string.IsNullOrEmpty(_accessToken))
            {
                SetClientAuthorizationBearerToken();
            }

            return _accessToken;
        }

        public async Task<string> RefreshAccessToken(AuthContext authContext)
        {
            var result = await PostAsync("auth/refresh", new { Token = _accessToken }, authContext);
            _accessToken = JsonConvert.DeserializeObject<string>(await result.Content.ReadAsStringAsync());

            if (!string.IsNullOrEmpty(_accessToken))
            {
                SetClientAuthorizationBearerToken();
            }

            return _refreshToken;
        }

        public async Task<List<SecurityPrinciple>> GetAccessSecurityPrinciples(AuthContext authContext)
        {
            var result = await FetchAsync("access/securityprinciples", authContext);
            return JsonToObject<List<SecurityPrinciple>>(result);
        }

        #endregion

        private T JsonToObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
