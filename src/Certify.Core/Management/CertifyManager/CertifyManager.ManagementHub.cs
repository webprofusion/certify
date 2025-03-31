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
using Certify.Shared;
using Certify.Shared.Core.Utils;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private IManagementServerClient _managementServerClient;
        private bool _isDirectMgmtHubClient = false;
        private bool _isHubConnectionErrorLogged = false;
        private ClientSecret _mgmtHubJoiningSecret;
        private const string _mgmtHubJoiningCredId = "_ManagementHubJoiningKey";
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

            var check = await CheckManagementHubCredentials(url, clientSecret, registerInstance: true);

            if (check.IsSuccess)
            {
                _mgmtHubJoiningToken = check.Result.JoiningToken;

                var hubEndpoint = check.Result.HubEndpoint;

                _serverConfig.ManagementServerHubAPI = url;
                _serverConfig.ManagementServerHubEndpoint = hubEndpoint;

                // store our hub managed instance id if it has changed/been created
                if (_serverConfig.HubAssignedInstanceId != check.Result.HubAssignedInstanceId)
                {
                    _serverConfig.HubAssignedInstanceId = check.Result.HubAssignedInstanceId;
                }

                SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(_serverConfig);

                // store/update clientId and secret
                _mgmtHubJoiningSecret = clientSecret;

                await _credentialsManager.Update(new StoredCredential
                {
                    StorageKey = _mgmtHubJoiningCredId,
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
                return check;
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
            var hubAssignedInstanceId = _serverConfig.HubAssignedInstanceId;

            using (var httpClient = new System.Net.Http.HttpClient())
            {
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url + $"/api/v1/hub/{(registerInstance ? "register" : "joincheck")}");
                request.Headers.Add("X-Client-ID", clientSecret.ClientId);
                request.Headers.Add("X-Client-Secret", clientSecret.Secret);

                if (!string.IsNullOrEmpty(hubAssignedInstanceId))
                {
                    request.Headers.Add("X-Certify-HubAssignedId", hubAssignedInstanceId);
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
                            return new ActionResult<HubJoiningInfo>("Could not connect to Management Hub. Check credentials.", isSuccess: false);
                        }
                        else
                        {
                            return new ActionResult<HubJoiningInfo>("Could not connect to Management Hub. Check URL.", isSuccess: false);
                        }
                    }
                }
                catch (Exception exp)
                {
                    return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. {exp.Message}", isSuccess: false);
                }
            }
        }

        public void SetDirectManagementClient(IManagementServerClient client)
        {
            _managementServerClient = client;
            _isDirectMgmtHubClient = true;
        }

        private JsonWebTokenHandler _joiningTokenHandler = new JsonWebTokenHandler();
        private async Task EnsureMgmtHubConnection()
        {
            if (!_isDirectMgmtHubClient)
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

                if (!_isDirectMgmtHubClient)
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

                    if (string.IsNullOrWhiteSpace(_mgmtHubJoiningToken))
                    {
                        if (_mgmtHubJoiningSecret == null)
                        {
                            var secret = await _credentialsManager.GetUnlockedCredential(_mgmtHubJoiningCredId);
                            if (secret != null)
                            {
                                _mgmtHubJoiningSecret = JsonSerializer.Deserialize<ClientSecret>(secret, JsonOptions.DefaultJsonSerializerOptions);
                            }
                        }

                        // acquire new token
                        var check = await CheckManagementHubCredentials(api, _mgmtHubJoiningSecret);
                        if (check.IsSuccess)
                        {
                            if (_serverConfig.HubAssignedInstanceId != check.Result.HubAssignedInstanceId)
                            {
                                _serviceLog.Error($"Failed to match hub assigned instance ID current id. Hub has changed or instance is duplicated.");
                                return;
                            }
                            else
                            {
                                _mgmtHubJoiningToken = check.Result.JoiningToken;
                            }
                        }
                        else
                        {
                            _serviceLog.Error($"Failed to acquire new hub joining token using current joining key: {check.Message}");
                            return;
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
                var items = await GetManagedCertificates(new ManagedCertificateFilter { });
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

        private void GenerateDemoItems()
        {
            var items = DemoDataGenerator.GenerateDemoItems();
            foreach (var item in items)
            {
                _ = UpdateManagedCertificate(item);
            }
        }
    }
}
