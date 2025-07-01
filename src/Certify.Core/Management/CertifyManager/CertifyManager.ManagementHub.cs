using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Shared;
using Certify.Shared.Core.Utils;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private IManagementServerClient _managementServerClient;
        private bool _isDirectMgmtHubBackend = false;
        private bool _isMgtmHubBackend = false;
        private bool _isHubConnectionErrorLogged = false;
        private ClientSecret _mgmtHubJoiningSecret;
        public const string MgmtHubJoiningCredId = "_ManagementHubJoiningKey";
        private string _mgmtHubJoiningToken = default!;

        public async Task<ActionResult> CheckManagementHubConnectionStatus()
        {
            if (_managementServerClient?.IsConnected() == true)
            {
                return new ActionResult("Connected to Management Hub.", isSuccess: true);
            }
            else
            {
                return new ActionResult("Not connected to Management Hub.", isSuccess: false);
            }
        }

        public async Task<ActionResult> JoinManagementHub(string url, ClientSecret clientSecret)
        {
            _serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            var registerInstance = true;
            ActionResult<HubJoiningInfo> joiningCredentialsCheck = null;

            if (!string.IsNullOrWhiteSpace(_serverConfig.HubAssignedInstanceId))
            {
                // when have already joined a hub, first check if we are rejoining the same hub by just verifying the credentials
                joiningCredentialsCheck = await CheckManagementHubCredentials(url, clientSecret, registerInstance: false);

                if (joiningCredentialsCheck.IsSuccess)
                {
                    // already registered, just joining again
                    registerInstance = false;
                }
                else
                {
                    // if we are not rejoining the same hub (or our credentials failed), we need to register a new instance
                    registerInstance = true;
                    _serverConfig.HubAssignedInstanceId = null;
                }
            }

            // if we are not rejoining the same hub, we need to register a new instance
            if (joiningCredentialsCheck == null || joiningCredentialsCheck?.IsSuccess != true)
            {
                joiningCredentialsCheck = await CheckManagementHubCredentials(url, clientSecret, registerInstance: registerInstance);
            }

            if (joiningCredentialsCheck.IsSuccess)
            {
                _mgmtHubJoiningToken = joiningCredentialsCheck.Result.JoiningToken;

                var hubEndpoint = joiningCredentialsCheck.Result.HubEndpoint;

                _serverConfig.ManagementServerHubAPI = url;
                _serverConfig.ManagementServerHubEndpoint = hubEndpoint;

                // store our hub managed instance id if it has changed/been created
                if (_serverConfig.HubAssignedInstanceId != joiningCredentialsCheck.Result.HubAssignedInstanceId)
                {
                    _serverConfig.HubAssignedInstanceId = joiningCredentialsCheck.Result.HubAssignedInstanceId;
                }

                SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(_serverConfig);

                // store/update clientId and secret
                _mgmtHubJoiningSecret = clientSecret;

                await _credentialsManager.Update(new StoredCredential
                {
                    StorageKey = MgmtHubJoiningCredId,
                    ProviderType = StandardAuthTypes.STANDARD_AUTH_MGMTHUB,
                    Title = "Management Hub Joining Key",
                    Secret = JsonSerializer.Serialize(clientSecret)
                });

                _managementServerClient = null;

                try
                {
                    await EnsureMgmtHubConnection();
                }
                catch
                {
                    return new ActionResult("A problem occurred when connecting to the management hub. Check URL and credentials.", isSuccess: false);
                }

                return new ActionResult("Connected to Management Hub.", isSuccess: true);
            }
            else
            {
                return joiningCredentialsCheck;
            }
        }

        /// <summary>
        /// Checks the credentials for connecting to a Management Hub and returns the status along with hub information.
        /// information.
        /// </summary>
        /// <param name="url">Specifies the endpoint for the Management Hub to verify the connection.</param>
        /// <param name="clientSecret">Contains the credentials required for authenticating the connection to the Management Hub.</param>
        /// <param name="registerInstance">Indicates whether to register the current instance with the Management Hub during the check.</param>
        /// <returns>Returns an action result indicating the success of the connection attempt and any relevant hub information.</returns>
        public async Task<ActionResult<HubJoiningInfo>> CheckManagementHubCredentials(string url, ClientSecret clientSecret, bool registerInstance = false)
        {

            using (var httpClient = new System.Net.Http.HttpClient())
            {
                var endpoint = $"{url.TrimEnd('/')}/api/v1/hub/{(registerInstance ? "register" : "joincheck")}";
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, endpoint);
                request.Headers.Add("X-Client-ID", clientSecret.ClientId);
                request.Headers.Add("X-Client-Secret", clientSecret.Secret);

                if (!string.IsNullOrWhiteSpace(_serverConfig.HubAssignedInstanceId))
                {
                    request.Headers.Add("X-Certify-HubAssignedId", _serverConfig.HubAssignedInstanceId);
                }

                try
                {
                    var response = await httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var hubInfo = JsonSerializer.Deserialize<HubJoiningInfo>(json, JsonOptions.DefaultJsonSerializerOptions);
                        return new ActionResult<HubJoiningInfo>("Connected to Management Hub.", isSuccess: true, hubInfo);
                    }
                    else
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub (Unauthorized). Check credentials {endpoint} {clientSecret.ClientId} {clientSecret.Secret} {_serverConfig.HubAssignedInstanceId}. {response}", isSuccess: false);
                        }
                        else
                        {
                            return new ActionResult<HubJoiningInfo>("Could not connect to Management Hub. Check URL.", isSuccess: false);
                        }
                    }
                }
                catch (Exception exp)
                {
                    return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. {exp}", isSuccess: false);
                }
            }
        }

        public void EnableManagementHubBackend(bool isDirectHubBackend)
        {
            _isDirectMgmtHubBackend = isDirectHubBackend;

        }

        public void SetDirectManagementClient(IManagementServerClient client)
        {
            _managementServerClient = client;
        }

        public async Task<HubInfo> GetHubInfo()
        {
            if (_isMgtmHubBackend)
            {
                var hubInfo = new HubInfo();

                hubInfo.InstanceId = _serverConfig.HubAssignedInstanceId;

                var versionInfo = Util.GetAppVersion().ToString();

                hubInfo.Version = new Models.Hub.VersionInfo
                {
                    Version = versionInfo,
                    Product = "Certify Management Hub"
                };

                return hubInfo;
            }
            else
            {
                return null;
            }
        }

        private JsonWebTokenHandler _joiningTokenHandler = new JsonWebTokenHandler();
        private async Task EnsureMgmtHubConnection()
        {
            if (!_isDirectMgmtHubBackend)
            {
                // check we have a current non-expired joining token
                if (!string.IsNullOrWhiteSpace(_mgmtHubJoiningToken))
                {
                    // check jwt has not expired

                    var validation = await _joiningTokenHandler.ValidateTokenAsync(_mgmtHubJoiningToken, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidateLifetime = true,
                        ValidateAudience = false,
                        ValidateIssuer = false,
                        ValidateIssuerSigningKey = false
                    });

                    if (!validation.IsValid)
                    {
                        // token has expired, will need a new one
                        _mgmtHubJoiningToken = null;
                    }
                }
            }

            // connect/reconnect to management hub if enabled
            if (_managementServerClient == null || !_managementServerClient.IsConnected())
            {
                var mgmtHubUri = string.Empty;
                var api = string.Empty;
                var endpoint = string.Empty;
                var defaultEnpoint = "api/internal/managementhub";

                if (!_isDirectMgmtHubBackend)
                {
                    // construct hub api url and status hub api endpoint
                    if (Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB") != null)
                    {
                        api = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB");

                        if (api.EndsWith(defaultEnpoint))
                        {
                            mgmtHubUri = api;

                            endpoint = defaultEnpoint;
                            api = api.Replace(defaultEnpoint, "");
                        }
                        else
                        {
                            endpoint = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_ENDPOINT") ?? defaultEnpoint;
                            mgmtHubUri = $"{api.Trim('/')}/{endpoint.Trim('/')}";
                        }
                    }
                    else
                    {
                        api = _serverConfig.ManagementServerHubAPI.Trim('/');
                        endpoint = _serverConfig.ManagementServerHubEndpoint.Trim('/');
                        mgmtHubUri = $"{api}/{endpoint}";
                    }

                    // if hub url has resolved to "/", remove trailing slash and continue with empty string
                    mgmtHubUri = mgmtHubUri?.TrimEnd('/');

                    if (!string.IsNullOrWhiteSpace(mgmtHubUri))
                    {
                        if (string.IsNullOrWhiteSpace(_mgmtHubJoiningToken))
                        {
                            if (_mgmtHubJoiningSecret == null)
                            {
                                // check if we have an environment variable for client id and client secret
                                var clientId = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_CLIENT_ID");
                                var clientSecret = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_CLIENT_SECRET");
                                if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
                                {
                                    _mgmtHubJoiningSecret = new ClientSecret
                                    {
                                        ClientId = clientId,
                                        Secret = clientSecret
                                    };

                                    AddSystemStatusItem(
                                        SystemStatusCategories.SERVICE_CORE,
                                        SystemStatusKeys.SERVICE_CORE_HUB_JOINING_KEY,
                                        "Management Hub Joining Key",
                                        "Using management hub joining key from environment variables"
                                        );
                                }

                                // if not set by env, check if we already have a management hub joining key as a stored credential
                                if (_mgmtHubJoiningSecret == null)
                                {
                                    try
                                    {
                                        var secret = await _credentialsManager.GetUnlockedCredential(CertifyManager.MgmtHubJoiningCredId);

                                        if (secret != null)
                                        {
                                            _mgmtHubJoiningSecret = JsonSerializer.Deserialize<ClientSecret>(secret, JsonOptions.DefaultJsonSerializerOptions);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _serviceLog.Error(ex, "Error retrieving management hub joining key from credentials store.");
                                    }
                                }

                                if (_mgmtHubJoiningSecret == null)
                                {

                                    AddSystemStatusItem(
                                        SystemStatusCategories.SERVICE_CORE,
                                        SystemStatusKeys.SERVICE_CORE_HUB_JOINING_KEY,
                                        "Management Hub Joining Key",
                                        "Management hub joining key not set, instance cannot join hub.",
                                        hasWarning: true
                                        );

                                    _serviceLog.Error($"Hub joining secret invalid or not found while attempting to join {mgmtHubUri}");
                                    return;
                                }
                            }

                            // acquire new token
                            var check = await CheckManagementHubCredentials(api, _mgmtHubJoiningSecret);

                            if (check.IsSuccess)
                            {
                                if (_serverConfig.HubAssignedInstanceId != check.Result.HubAssignedInstanceId)
                                {
                                    AddSystemStatusItem(
                                        SystemStatusCategories.SERVICE_CORE,
                                        SystemStatusKeys.SERVICE_CORE_HUB_JOINING_AUTH,
                                        "Management Hub Joining Auth",
                                        "Management hub joining auth successful but hub assigned instance ID did not match. Current settings may be for a different hub.",
                                        hasError: true
                                    );

                                    _serviceLog.Error($"Failed to match hub assigned instance ID current id. Hub has changed or instance is duplicated.");
                                    return;
                                }
                                else
                                {
                                    _mgmtHubJoiningToken = check.Result.JoiningToken;

                                    AddSystemStatusItem(
                                        SystemStatusCategories.SERVICE_CORE,
                                        SystemStatusKeys.SERVICE_CORE_HUB_JOINING_AUTH,
                                        "Management Hub Joining Auth",
                                        "Management hub joining auth successful."
                                    );
                                }
                            }
                            else
                            {
                                AddSystemStatusItem(
                                    SystemStatusCategories.SERVICE_CORE,
                                    SystemStatusKeys.SERVICE_CORE_HUB_JOINING_AUTH,
                                    "Management Hub Joining Auth",
                                    "Management hub joining auth failed, instance cannot join hub. Joining key (or current Hub Assigned ID) may be invalid or for a different hub.",
                                    hasError: true
                                );

                                _serviceLog.Error($"Failed to acquire new hub joining token using current joining key: {check.Message}");
                                return;
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(mgmtHubUri))
                {
                    await StartManagementHubConnection(mgmtHubUri);
                }
            }
            else
            {

                // send heartbeat message to management hub
                SendHeartbeatToManagementHub();
            }
        }

        private void SendHeartbeatToManagementHub()
        {
            _managementServerClient.SendInstanceInfo(Guid.NewGuid(), isCommandResponse: false);
        }

        public ManagedInstanceInfo GetManagedInstanceInfo()
        {
            return new ManagedInstanceInfo
            {
                Id = _serverConfig.HubAssignedInstanceId,
                InstanceId = _serverConfig.HubAssignedInstanceId,
                Title = $"{Environment.MachineName}",
                OS = EnvironmentUtil.GetFriendlyOSName(detailed: false),
                OSVersion = EnvironmentUtil.GetFriendlyOSName(),
                ClientVersion = Util.GetAppVersion().ToString(),
                ClientName = ConfigResources.AppName
            };
        }

        private async Task StartManagementHubConnection(string hubUri)
        {
            if (string.IsNullOrWhiteSpace(_mgmtHubJoiningToken))
            {
                _serviceLog.Error("No joining token available, cannot connect to management hub.");
                return;
            }

            _serviceLog.Debug("Attempting connection to management hub {hubUri}", hubUri);

            var appVersion = Util.GetAppVersion().ToString();

            var instanceInfo = GetManagedInstanceInfo();

            if (_managementServerClient != null)
            {
                _managementServerClient.OnGetCommandResult -= PerformHubCommandWithResult;
                _managementServerClient.OnConnectionReconnecting -= _managementServerClient_OnConnectionReconnecting;
            }

            _managementServerClient = new ManagementServerClient(hubUri, instanceInfo);
            _managementServerClient.SetJoiningToken(_mgmtHubJoiningToken);

            try
            {
                await _managementServerClient.ConnectAsync();

                _managementServerClient.OnGetCommandResult += PerformHubCommandWithResult;
                _managementServerClient.OnConnectionReconnecting += _managementServerClient_OnConnectionReconnecting;

                _serviceLog.Information("Connected to management hub {hubUri}", hubUri);
            }
            catch (Exception ex)
            {
                if (!_isHubConnectionErrorLogged)
                {
                    _serviceLog.Error(ex, "Could not connect to Certify Management Hub {hubUri}. Service may not be currently available. Will retry periodically, subsequent failures will not be logged.", hubUri);
                    _isHubConnectionErrorLogged = true;
                }

                _managementServerClient = null;
            }
        }

        public async Task<InstanceCommandResult> PerformHubCommandWithResult(InstanceCommandRequest arg)
        {
            object val = null;

            if (arg.CommandType == ManagementHubCommands.GetManagedItem)
            {
                // Get a single managed item by id
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                val = await GetManagedCertificate(managedCertIdArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.GetManagedItems)
            {
                // Get all managed items
                var items = await GetManagedCertificates(new ManagedCertificateFilter { IncludeExternal = CoreAppSettings.Current.EnableExternalCertManagers });
                val = new ManagedInstanceItems { InstanceId = _serverConfig.HubAssignedInstanceId, Items = items };
            }
            else if (arg.CommandType == ManagementHubCommands.GetStatusSummary)
            {
                var s = await GetManagedCertificateSummary(new ManagedCertificateFilter { });
                val = s;
            }
            else if (arg.CommandType == ManagementHubCommands.GetManagedItemLog)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                var limit = args.FirstOrDefault(a => a.Key == "limit");

                val = await GetItemLog(managedCertIdArg.Value, int.Parse(limit.Value));
            }
            else if (arg.CommandType == ManagementHubCommands.GetManagedItemRenewalPreview)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertArg = args.FirstOrDefault(a => a.Key == "managedCert");
                var managedCert = JsonSerializer.Deserialize<ManagedCertificate>(managedCertArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                val = await GeneratePreview(managedCert);
            }
            else if (arg.CommandType == ManagementHubCommands.ExportCertificate)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                var format = args.FirstOrDefault(a => a.Key == "format");
                val = await ExportCertificate(managedCertIdArg.Value, format.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateManagedItem)
            {
                // update a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertArg = args.FirstOrDefault(a => a.Key == "managedCert");
                var managedCert = JsonSerializer.Deserialize<ManagedCertificate>(managedCertArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                var item = await UpdateManagedCertificate(managedCert);

                val = item;

                ReportManagedItemUpdateToMgmtHub(item);
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveManagedItem)
            {
                // delete a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");

                var actionResult = await DeleteManagedCertificate(managedCertIdArg.Value);

                val = actionResult;

                if (actionResult.IsSuccess)
                {
                    ReportManagedItemDeleteToMgmtHub(managedCertIdArg.Value);
                }
            }
            else if (arg.CommandType == ManagementHubCommands.TestManagedItemConfiguration)
            {
                // test challenge response config for a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertArg = args.FirstOrDefault(a => a.Key == "managedCert");
                var managedCert = JsonSerializer.Deserialize<ManagedCertificate>(managedCertArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                var log = ManagedCertificateLog.GetLogger(managedCert.Id, _loggingLevelSwitch);

                val = await TestChallenge(log, managedCert, isPreviewMode: true);

            }
            else if (arg.CommandType == ManagementHubCommands.PerformManagedItemRequest)
            {
                // attempt certificate order
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                var managedCert = await GetManagedCertificate(managedCertIdArg.Value);

                var progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCert);
                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

                _ = await PerformCertificateRequest(
                                                        null,
                                                        managedCert,
                                                        progressIndicator,
                                                        resumePaused: true,
                                                        isInteractive: true
                                                        );

                val = true;
            }
            else if (arg.CommandType == ManagementHubCommands.GetCertificateAuthorities)
            {
                val = await GetCertificateAuthorities();
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateCertificateAuthority)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var itemArg = args.FirstOrDefault(a => a.Key == "certificateAuthority");
                var item = JsonSerializer.Deserialize<CertificateAuthority>(itemArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                val = await UpdateCertificateAuthority(item);
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveCertificateAuthority)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var itemArg = args.FirstOrDefault(a => a.Key == "id");
                val = await RemoveCertificateAuthority(itemArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.GetAcmeAccounts)
            {
                val = await GetAccountRegistrations();
            }
            else if (arg.CommandType == ManagementHubCommands.AddAcmeAccount)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var registrationArg = args.FirstOrDefault(a => a.Key == "registration");
                var registration = JsonSerializer.Deserialize<ContactRegistration>(registrationArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                val = await AddAccount(registration);
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveAcmeAccount)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var itemArg = args.FirstOrDefault(a => a.Key == "storageKey");
                var deactivateArg = args.FirstOrDefault(a => a.Key == "deactivate");
                val = await RemoveAccount(itemArg.Value, bool.Parse(deactivateArg.Value));
            }
            else if (arg.CommandType == ManagementHubCommands.GetStoredCredentials)
            {
                val = await _credentialsManager.GetCredentials();
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateStoredCredential)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var itemArg = args.FirstOrDefault(a => a.Key == "item");
                var storedCredential = JsonSerializer.Deserialize<StoredCredential>(itemArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                var updated = await _credentialsManager.Update(storedCredential);
                if (updated != null)
                {
                    val = new ActionResult { IsSuccess = true, Message = "Updated", Result = updated };
                }
                else
                {
                    val = new ActionResult("Update failed", false);
                }
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveStoredCredential)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var itemArg = args.FirstOrDefault(a => a.Key == "storageKey");
                val = await _credentialsManager.Delete(_itemManager, itemArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.GetChallengeProviders)
            {
                val = await Core.Management.Challenges.ChallengeProviders.GetChallengeAPIProviders();
            }

            else if (arg.CommandType == ManagementHubCommands.GetDnsZones)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var providerTypeArg = args.FirstOrDefault(a => a.Key == "providerTypeId");
                var credentialIdArg = args.FirstOrDefault(a => a.Key == "credentialId");

                val = await GetDnsProviderZones(providerTypeArg.Value, credentialIdArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.GetDeploymentProviders)
            {
                val = await GetDeploymentProviders();
            }
            else if (arg.CommandType == ManagementHubCommands.ExecuteDeploymentTask)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);

                var managedCertificateIdArg = args.FirstOrDefault(a => a.Key == "managedCertificateId");
                var taskIdArg = args.FirstOrDefault(a => a.Key == "taskId");

                val = await PerformDeploymentTask(null, managedCertificateIdArg.Value, taskIdArg.Value, isPreviewOnly: false, skipDeferredTasks: false, forceTaskExecution: false);
            }
            else if (arg.CommandType == ManagementHubCommands.GetTargetIPAddresses)
            {
                val = await GetTargetIPAddresses();
            }
            else if (arg.CommandType == ManagementHubCommands.GetTargetServiceTypes)
            {
                val = await GetTargetServiceTypes();
            }
            else if (arg.CommandType == ManagementHubCommands.GetTargetServiceItems)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var serviceTypeArg = args.FirstOrDefault(a => a.Key == "serviceType");

                var serverType = MapStandardServerType(serviceTypeArg.Value);

                val = await GetPrimaryWebSites(serverType, ignoreStoppedSites: true);
            }
            else if (arg.CommandType == ManagementHubCommands.GetTargetServiceItemIdentifiers)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var serviceTypeArg = args.FirstOrDefault(a => a.Key == "serviceType");
                var itemArg = args.FirstOrDefault(a => a.Key == "itemId");

                var serverType = MapStandardServerType(serviceTypeArg.Value);

                val = await GetDomainOptionsFromSite(serverType, itemArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.PerformImport)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var requestArg = args.FirstOrDefault(a => a.Key == "importRequest");
                var importRequest = JsonSerializer.Deserialize<ImportRequest>(requestArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                val = await PerformImport(importRequest);
            }
            else if (arg.CommandType == ManagementHubCommands.PerformExport)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var requestArg = args.FirstOrDefault(a => a.Key == "exportRequest");
                var exportRequest = JsonSerializer.Deserialize<ExportRequest>(requestArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                val = await PerformExport(exportRequest);
            }
            else if (arg.CommandType == ManagementHubCommands.GetSystemStatusItems)
            {
                val = _systemStatusItems;
            }
            else if (arg.CommandType == ManagementHubCommands.GetServiceConfig)
            {
                val = _serverConfig;
            }
            else if (arg.CommandType == ManagementHubCommands.GetServiceCoreSettings)
            {
                val = SettingsManager.ToPreferences();
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateServiceCoreSettings)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var prefUpdate = args.FirstOrDefault(a => a.Key == "prefs");
                var update = JsonSerializer.Deserialize<Preferences>(prefUpdate.Value, JsonOptions.DefaultJsonSerializerOptions);

                var prefs = SettingsManager.ToPreferences();

                if (update != null)
                {
                    // update supported settings
                    prefs.CertificateCleanupMode = update.CertificateCleanupMode;
                    prefs.DefaultACMERetryInterval = update.DefaultACMERetryInterval;
                    prefs.DefaultCertificateAuthority = update.DefaultCertificateAuthority;
                    prefs.DefaultCertificateStore = update.DefaultCertificateStore;
                    prefs.DefaultKeyType = update.DefaultKeyType;
                    prefs.DisableARIChecks = update.DisableARIChecks;

                    prefs.EnableAppTelematics = update.EnableAppTelematics;
                    prefs.EnableAutomaticCAFailover = update.EnableAutomaticCAFailover;
                    prefs.EnableExternalCertManagers = update.EnableExternalCertManagers;
                    prefs.EnableStatusReporting = update.EnableStatusReporting;
                    prefs.EnableValidationProxyAPI = update.EnableValidationProxyAPI;
                    prefs.EnableHttpChallengeServer = update.EnableHttpChallengeServer;

                    prefs.NtpServer = update.NtpServer;
                    prefs.RenewalIntervalDays = update.RenewalIntervalDays;
                    prefs.RenewalIntervalMode = update.RenewalIntervalMode;
                    prefs.UseModernPFXAlgs = update.UseModernPFXAlgs;

                    prefs.CertificateManagers = update.CertificateManagers;

                    SettingsManager.FromPreferences(prefs);

                    try
                    {
                        SettingsManager.SaveAppSettings();
                        val = new ActionResult("Service core settings updated", true);
                    }
                    catch (Exception ex)
                    {
                        _serviceLog.Error(ex, "Error saving preferences");
                        val = new ActionResult("Service core settings could not be updated.", false);
                    }

                    // cert manager config may have changed, refresh required
                    _externallyManagedCacheUpdated = DateTimeOffset.MinValue;
                }
                else
                {
                    val = new ActionResult("Service core settings could not be updated. Invalid data.", false);
                }
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateServiceConfig)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var configArg = args.FirstOrDefault(a => a.Key == "config");
                var configVal = JsonSerializer.Deserialize<ServiceConfig>(configArg.Value, JsonOptions.DefaultJsonSerializerOptions);
                if (configVal != null)
                {
                    _serverConfig.LogLevel = configVal.LogLevel;
                    _serverConfig.ManagementServerHubAPI = configVal.ManagementServerHubAPI;
                    _serverConfig.ManagementServerHubEndpoint = configVal.ManagementServerHubEndpoint;
                    _serverConfig.UseHTTPS = configVal.UseHTTPS;
                    _serverConfig.Host = configVal.Host;
                    _serverConfig.Port = configVal.Port;
                    _serverConfig.HttpChallengeServerPort = configVal.HttpChallengeServerPort;

                    try
                    {
                        SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(_serverConfig, throwOnError: true);
                        val = new ActionResult("Service config updated", true);
                    }
                    catch (Exception ex)
                    {
                        _serviceLog.Error(ex, "Error updating service config");
                        val = new ActionResult("Service config could not be updated.", false);
                    }
                }
                else
                {
                    val = new ActionResult("Service config could not be updated. Invalid data.", false);
                }
            }
            else if (arg.CommandType == ManagementHubCommands.Reconnect)
            {
                await _managementServerClient.Disconnect();
            }

            return new InstanceCommandResult { CommandId = arg.CommandId, Value = JsonSerializer.Serialize(val), ObjectValue = val };
        }

        private StandardServerTypes MapStandardServerType(string type)
        {
            if (StandardServerTypes.TryParse(type, out StandardServerTypes standardServerType))
            {
                return standardServerType;
            }
            else
            {
                return StandardServerTypes.Other;
            }
        }

        private void ReportManagedItemUpdateToMgmtHub(ManagedCertificate item)
        {
            if (item != null)
            {
                _managementServerClient?.SendNotificationToManagementHub(ManagementHubCommands.NotificationUpdatedManagedItem, item);
            }
        }
        private void ReportManagedItemDeleteToMgmtHub(string id)
        {
            _managementServerClient?.SendNotificationToManagementHub(ManagementHubCommands.NotificationRemovedManagedItem, id);
        }

        private void ReportRequestProgressToMgmtHub(RequestProgressState progress)
        {
            _managementServerClient?.SendNotificationToManagementHub(ManagementHubCommands.NotificationManagedItemRequestProgress, progress);
        }

        private void _managementServerClient_OnConnectionReconnecting()
        {
            _serviceLog.Warning("Reconnecting to Management Hub.");
        }

        private async Task GenerateDemoItems(int? numItems)
        {
            var currentItems = await GetManagedCertificateSummary(new ManagedCertificateFilter { });
            if (currentItems.Total == 0)
            {
                var items = DemoDataGenerator.GenerateDemoItems(numItems ?? 100, numItems ?? 500);
                foreach (var item in items)
                {

                    _ = UpdateManagedCertificate(item);
                }
            }
        }
    }
}
