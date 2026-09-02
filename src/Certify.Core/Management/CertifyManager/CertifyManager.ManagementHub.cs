using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Models.Shared;
using Certify.Server.Hub.Api;
using Certify.Shared;
using Certify.Shared.Core.Utils;
using Certify.SharedUtils;
using Microsoft.IdentityModel.JsonWebTokens;
using Registration.Core.Models.Shared;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private IManagementServerClient _managementServerClient;
        private bool _isDirectMgmtHubBackend = false;
        private bool _isMgtmHubBackend = false;

        private ClientSecret _mgmtHubJoiningSecret;
        private string? _mgmtHubRequestAuthSecret;

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

        private void SetHubAssignedInstanceId(string? val)
        {
            if (string.IsNullOrWhiteSpace(val))
            {
                _serviceLog.Warning("Hub assigned instance ID cannot be cleared automatically. Existing identity is retained.");
                return;
            }

            var updated = HubInstanceIdentityManager.TrySetHubAssignedInstanceId(val, overwriteExisting: false);
            if (!updated)
            {
                _serviceLog.Warning("Hub assigned instance ID update ignored because a different immutable identity is already stored.");
            }

            _serverConfig.HubAssignedInstanceId = HubInstanceIdentityManager.GetHubAssignedInstanceId(_serverConfig.HubAssignedInstanceId);
            SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(_serverConfig);
        }

        private async Task StoreManagementHubRequestAuthSecret(string? requestAuthSecret)
        {
            if (string.IsNullOrWhiteSpace(requestAuthSecret))
            {
                return;
            }

            _mgmtHubRequestAuthSecret = requestAuthSecret;

            await _credentialsManager.Update(new StoredCredential
            {
                StorageKey = Certify.Models.Hub.HubSharedConstants.MgmtHubRequestAuthSecretCredId,
                ProviderType = StandardAuthTypes.STANDARD_AUTH_MGMTHUB,
                Title = "Management Hub Request Auth Secret",
                Secret = requestAuthSecret
            });
        }

        private async Task ClearManagementHubRequestAuthSecret()
        {
            _mgmtHubRequestAuthSecret = null;

            try
            {
                await _credentialsManager.Delete(_itemManager, HubSharedConstants.MgmtHubRequestAuthSecretCredId);
            }
            catch (Exception ex)
            {
                _serviceLog.Warning($"Failed to clear management hub request auth secret from credentials store before rejoin: {ex.Message}");
            }
        }

        private async Task StoreManagementHubJoiningSecret(ClientSecret clientSecret)
        {
            _mgmtHubJoiningSecret = clientSecret;

            await _credentialsManager.Update(new StoredCredential
            {
                StorageKey = HubSharedConstants.MgmtHubJoiningCredId,
                ProviderType = StandardAuthTypes.STANDARD_AUTH_MGMTHUB,
                Title = "Management Hub Joining Key",
                Secret = JsonSerializer.Serialize(clientSecret)
            });
        }

        private async Task<string?> GetManagementHubRequestAuthSecret()
        {
            if (!string.IsNullOrWhiteSpace(_mgmtHubRequestAuthSecret))
            {
                return _mgmtHubRequestAuthSecret;
            }

            try
            {
                var secret = await _credentialsManager.GetUnlockedCredential(HubSharedConstants.MgmtHubRequestAuthSecretCredId);
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    _mgmtHubRequestAuthSecret = secret;
                }
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Error retrieving management hub request auth secret from credentials store.");
            }

            return _mgmtHubRequestAuthSecret;
        }

        public async Task<ActionResult> JoinManagementHub(string url, ClientSecret clientSecret)
        {
            var hubConnectionAuthToken = string.Empty;

            _serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();
            _serverConfig.HubAssignedInstanceId = HubInstanceIdentityManager.GetHubAssignedInstanceId(_serverConfig.HubAssignedInstanceId);

            ActionResult<HubJoiningInfo> joiningCredentialsCheck = null;
            var hasStoredRequestAuthSecret = !string.IsNullOrWhiteSpace(await GetManagementHubRequestAuthSecret());

            if (!string.IsNullOrWhiteSpace(_serverConfig.HubAssignedInstanceId))
            {
                _serviceLog.Information("Hub already joined, will reconnect.");
                // when have already joined a hub, first check if we are rejoining the same hub by just verifying the credentials
                if (!hasStoredRequestAuthSecret)
                {
                    _serviceLog.Information("Management hub request auth secret is missing locally. Requesting secret reissue during hub rejoin.");
                }

                joiningCredentialsCheck = await CheckManagementHubCredentials(url, clientSecret, registerInstance: false, reissueRequestAuthSecret: !hasStoredRequestAuthSecret);

                if (!joiningCredentialsCheck.IsSuccess && joiningCredentialsCheck.Result?.RejoinRequired == true)
                {
                    _serviceLog.Information("Hub rejoin required, will attempt to re-register instance.");
                    // need to re-register
                    _serviceLog.Warning("Hub rejoin required but hub assigned instance ID is immutable. Re-enroll this instance explicitly to change identity.");

                    joiningCredentialsCheck = await CheckManagementHubCredentials(url, clientSecret, registerInstance: true);
                }
            }
            else
            {
                // if we are not rejoining the same hub, we need to register a new instance
                _serviceLog.Information("Hub not yet joined, will attempt to join.");
                joiningCredentialsCheck = await CheckManagementHubCredentials(url, clientSecret, registerInstance: true);
            }

            if (joiningCredentialsCheck.IsSuccess)
            {
                hubConnectionAuthToken = joiningCredentialsCheck.Result.JoiningToken;

                var hubEndpoint = joiningCredentialsCheck.Result.HubEndpoint;

                // store hub api endpoint and assigned id

                _serverConfig.ManagementServerHubAPI = url;
                _serverConfig.ManagementServerHubEndpoint = hubEndpoint;

                // store our hub managed instance id if it has changed/been created
                if (_serverConfig.HubAssignedInstanceId != joiningCredentialsCheck.Result.HubAssignedInstanceId)
                {
                    if (!string.IsNullOrWhiteSpace(joiningCredentialsCheck.Result.HubAssignedInstanceId))
                    {
                        SetHubAssignedInstanceId(joiningCredentialsCheck.Result.HubAssignedInstanceId);
                    }
                    else
                    {
                        _serviceLog.Warning("Hub joined ok but hub assigned instance id was empty.");
                    }
                }

                SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(_serverConfig);

                // store/update clientId and secret
                await StoreManagementHubJoiningSecret(clientSecret);

                await StoreManagementHubRequestAuthSecret(joiningCredentialsCheck.Result.RequestAuthSecret);

                try
                {
                    // explicit join, wait for any in-progress connection check to complete rather than skipping
                    await EnsureMgmtHubConnection(hubConnectionAuthToken, skipIfCheckInProgress: false);
                }
                catch
                {
                    return new ActionResult("A problem occurred when connecting to the management hub. Check URL and credentials.", isSuccess: false);
                }

                var wasKnownInstance = joiningCredentialsCheck.Result?.IsKnownInstance == true;
                var joinMessage = wasKnownInstance
                    ? "Connected to Management Hub. Re-join successful: instance already known to hub."
                    : "Connected to Management Hub.";

                return new ActionResult(joinMessage, isSuccess: true)
                {
                    Result = joiningCredentialsCheck.Result
                };
            }
            else
            {
                _serviceLog.Information("Hub credentials check failed.");
                return joiningCredentialsCheck;
            }
        }

        private async Task PerformManagementHubRejoin(ManagementHubRejoinRequest rejoinRequest)
        {
            try
            {
                _serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

                var url = !string.IsNullOrWhiteSpace(rejoinRequest.JoiningCredential.Url)
                    ? rejoinRequest.JoiningCredential.Url
                    : _serverConfig.ManagementServerHubAPI;

                if (string.IsNullOrWhiteSpace(url))
                {
                    _serviceLog.Warning("Hub rejoin was requested but no management hub API URL was available.");
                    return;
                }

                var joiningSecret = new ClientSecret
                {
                    ClientId = rejoinRequest.JoiningCredential.ClientId,
                    Secret = rejoinRequest.JoiningCredential.Secret
                };

                await StoreManagementHubJoiningSecret(joiningSecret);

                if (rejoinRequest.ReissueRequestAuthSecret)
                {
                    await ClearManagementHubRequestAuthSecret();
                }

                if (_managementServerClient?.IsConnected() == true)
                {
                    await _managementServerClient.Disconnect();
                }

                var joinResult = await JoinManagementHub(url, joiningSecret);
                if (joinResult.IsSuccess)
                {
                    _serviceLog.Information("Management hub rejoin completed successfully.");
                }
                else
                {
                    _serviceLog.Warning("Management hub rejoin failed: {message}", joinResult.Message);
                }
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Management hub rejoin command failed.");
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
        public async Task<ActionResult<HubJoiningInfo>> CheckManagementHubCredentials(string url, ClientSecret clientSecret, bool registerInstance = false, bool reissueRequestAuthSecret = false)
        {
            _serverConfig.HubAssignedInstanceId = HubInstanceIdentityManager.GetHubAssignedInstanceId(_serverConfig.HubAssignedInstanceId);

            if (string.IsNullOrWhiteSpace(_serverConfig.HubAssignedInstanceId) && registerInstance == false)
            {
                _serviceLog.Warning("Attempting to rejoin hub but hub assigned instance ID is empty, need to re-register with hub");
                registerInstance = true;
            }

            var endpoint = $"{url.TrimEnd('/')}/api/v1/hub/{(registerInstance ? "register" : "joincheck")}";

            _serviceLog.Debug("Checking credentials via Management Hub {url}", endpoint);

            if (string.IsNullOrWhiteSpace(_serverConfig.HubAssignedInstanceId) && !registerInstance)
            {
                // if we are not registering, we should have an assigned id to check against
                return new ActionResult<HubJoiningInfo>("Hub Assigned Instance ID is required when rejoining the hub.", isSuccess: false);
            }

            try
            {
                var requestContext = new HubApiRequestContext
                {
                    ClientId = clientSecret.ClientId,
                    Secret = clientSecret.Secret,
                    HubAssignedInstanceId = _serverConfig.HubAssignedInstanceId,
                    InstanceVersion = Util.GetAppVersion().ToString(),
                    TraceInstanceName = GetManagedInstanceInfo().Title
                };

                var hubInfo = await UseHubApiClient(
                    url,
                    requestContext,
                    (client, ct) => registerInstance ? client.RegisterAsync(ct) : client.CheckJoiningAsync(false, reissueRequestAuthSecret, ct),
                    default);

                return new ActionResult<HubJoiningInfo>("Connected to Management Hub.", isSuccess: true, hubInfo);
            }
            catch (TaskCanceledException)
            {
                return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. Timeout trying to connect to hub, service may unavailable. {endpoint}", isSuccess: false);
            }
            catch (ApiException apiEx)
            {
                var problemResult = TryCreateHubProblemResult(endpoint, clientSecret, apiEx);
                if (problemResult != null)
                {
                    return problemResult;
                }

                if (apiEx.StatusCode == (int)System.Net.HttpStatusCode.Unauthorized)
                {
                    return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub (Unauthorized). {apiEx.Response} - Check credentials {endpoint} {clientSecret.ClientId} {clientSecret.Secret} {_serverConfig.HubAssignedInstanceId}.", isSuccess: false);
                }

                return new ActionResult<HubJoiningInfo>("Could not connect to Management Hub. Check URL.", isSuccess: false);
            }
            catch (System.Net.Http.HttpRequestException httpEx) when (httpEx.InnerException is System.Net.Sockets.SocketException socketEx && socketEx.ErrorCode == 111)
            {
                return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. Connection refused (port may not be open or service not running). {endpoint}", isSuccess: false);
            }
            catch (System.Net.Http.HttpRequestException httpEx)
            {
                return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. Network error: {httpEx.Message}", isSuccess: false);
            }
            catch (Exception exp)
            {
                return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. {exp}", isSuccess: false);
            }
        }

        private ActionResult<HubJoiningInfo>? TryCreateHubProblemResult(string endpoint, ClientSecret clientSecret, ApiException apiEx)
        {
            if (string.IsNullOrWhiteSpace(apiEx.Response))
            {
                return null;
            }

            try
            {
                var problemDetails = JsonSerializer.Deserialize<Microsoft.AspNetCore.Mvc.ProblemDetails>(apiEx.Response, JsonOptions.DefaultJsonSerializerOptions);
                if (problemDetails == null)
                {
                    return null;
                }

                var result = new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. {problemDetails.Title} - {problemDetails.Detail}", isSuccess: false);
                if (problemDetails.Type?.EndsWith("/hub-unknown-instance-id", StringComparison.OrdinalIgnoreCase) == true)
                {
                    result.Result = new HubJoiningInfo
                    {
                        Message = "The provided Hub Assigned Instance ID is not recognized by the Management Hub. It may be incorrect or associated with a different hub.",
                        RejoinRequired = true
                    };
                }

                return result;
            }
            catch (JsonException)
            {
                _serviceLog.Debug("Failed to parse problem+json response from hub for {endpoint}.");
            }

            return null;
        }

        public void EnableManagementHubBackend(bool isDirectHubBackend)
        {
            _isDirectMgmtHubBackend = isDirectHubBackend;
        }

        public void SetDirectManagementClient(IManagementServerClient client)
        {
            client.UpdateCachedInstanceInfo(GetManagedInstanceInfo());
            _managementServerClient = client;
        }

        public async Task<HubInfo> GetHubInfo()
        {
            if (_isMgtmHubBackend)
            {
                var hubInfo = new HubInfo();

                hubInfo.InstanceId = _serverConfig.HubAssignedInstanceId;

                var instanceInfo = GetManagedInstanceInfo();
                hubInfo.IsLicensed = instanceInfo.License?.StatusCode == LicenseCheckStatusCode.Licensed;

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

        private readonly SemaphoreSlim _hubConnectionCheckSync = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _hubTokenSync = new SemaphoreSlim(1, 1);

        private string? _cachedHubConnectionToken;
        private DateTimeOffset _cachedHubConnectionTokenExpiry = DateTimeOffset.MinValue;

        /// <summary>
        /// The management hub API url currently in use, resolved from either environment config or stored service config.
        /// </summary>
        private string? _resolvedMgmtHubApi;

        /// <summary>
        /// Cache a hub connection token along with its expiry, so it can be reused until it is close to expiring.
        /// </summary>
        private void StoreHubConnectionToken(string? token)
        {
            _cachedHubConnectionToken = token;
            _cachedHubConnectionTokenExpiry = DateTimeOffset.MinValue;

            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            try
            {
                var validTo = _joiningTokenHandler.ReadJsonWebToken(token).ValidTo;
                _cachedHubConnectionTokenExpiry = new DateTimeOffset(DateTime.SpecifyKind(validTo, DateTimeKind.Utc));
            }
            catch (Exception ex)
            {
                // if the expiry can't be read the token is treated as already expired, so the next connection attempt acquires a fresh one
                _serviceLog.Debug("Could not read expiry from management hub connection token: {message}", ex.Message);
            }
        }

        /// <summary>
        /// Provides a currently valid hub connection auth token, acquiring a new one when the cached token is missing or near expiry.
        /// This is invoked by the SignalR client for every connect and reconnect attempt.
        /// </summary>
        private async Task<string> GetHubConnectionTokenAsync(CancellationToken cancellationToken)
        {
            await _hubTokenSync.WaitAsync(cancellationToken);

            try
            {
                // reuse the current token while it has enough life left for a connection attempt to complete
                if (!string.IsNullOrWhiteSpace(_cachedHubConnectionToken)
                    && _cachedHubConnectionTokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    return _cachedHubConnectionToken!;
                }

                var api = !string.IsNullOrWhiteSpace(_resolvedMgmtHubApi) ? _resolvedMgmtHubApi : _serverConfig.ManagementServerHubAPI;

                if (string.IsNullOrWhiteSpace(api) || _mgmtHubJoiningSecret == null)
                {
                    _serviceLog.Warning("Cannot acquire a management hub connection token: hub API url or joining key is not available.");
                    return string.Empty;
                }

                var check = await CheckManagementHubCredentials(api, _mgmtHubJoiningSecret);

                if (!check.IsSuccess || string.IsNullOrWhiteSpace(check.Result?.JoiningToken))
                {
                    // returning empty fails the handshake, the reconnect policy and the scheduled connection check will retry
                    _serviceLog.Warning("Could not refresh the management hub connection token: {message}", check.Message);
                    StoreHubConnectionToken(null);
                    return string.Empty;
                }

                StoreHubConnectionToken(check.Result.JoiningToken);

                await StoreManagementHubRequestAuthSecret(check.Result.RequestAuthSecret);

                return _cachedHubConnectionToken!;
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Unhandled exception while acquiring a management hub connection token.");
                return string.Empty;
            }
            finally
            {
                _hubTokenSync.Release();
            }
        }

        /// <summary>
        /// Ensures there is a current connection to the management hub, reconnecting if necessary. Sends a heartbeat message to the hub if already connected.
        /// </summary>
        /// <param name="hubConnectionAuthToken">Optional pre-acquired joining token to connect with.</param>
        /// <param name="skipIfCheckInProgress">If true (the default, used by the scheduled checks) this attempt is skipped when another check is already running, otherwise it waits for the in-progress check to complete first.</param>
        /// <returns></returns>
        private async Task EnsureMgmtHubConnection(string? hubConnectionAuthToken = null, bool skipIfCheckInProgress = true)
        {
            // only one connection check should run at a time, otherwise overlapping checks each perform their own hub join check and each report the same connection status
            if (skipIfCheckInProgress)
            {
                if (!await _hubConnectionCheckSync.WaitAsync(0))
                {
                    _serviceLog?.Debug("EnsureMgmtHubConnection: a hub connection check is already in progress, skipping this attempt.");
                    return;
                }
            }
            else
            {
                await _hubConnectionCheckSync.WaitAsync();
            }

            try
            {
                // an explicit join passes a token it has just acquired, reuse it rather than immediately acquiring another
                if (!string.IsNullOrWhiteSpace(hubConnectionAuthToken))
                {
                    StoreHubConnectionToken(hubConnectionAuthToken);
                }

                // connect/reconnect to management hub if enabled (either connection not established or our joining token is null/expired)
                if (_managementServerClient?.IsConnected() != true)
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

                        // remember the resolved API url so the token factory can re-acquire tokens for the same hub
                        _resolvedMgmtHubApi = api;

                        // if hub url has resolved to "/", remove trailing slash and continue with empty string
                        mgmtHubUri = mgmtHubUri?.TrimEnd('/');

                        if (!string.IsNullOrWhiteSpace(mgmtHubUri))
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
                                        var secret = await _credentialsManager.GetUnlockedCredential(HubSharedConstants.MgmtHubJoiningCredId);

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
                                StoreHubConnectionToken(check.Result.JoiningToken);

                                await StoreManagementHubRequestAuthSecret(check.Result.RequestAuthSecret);

                                if (string.IsNullOrWhiteSpace(_serverConfig.HubAssignedInstanceId) && !string.IsNullOrWhiteSpace(check.Result.HubAssignedInstanceId))
                                {
                                    // first time join, store assigned id
                                    SetHubAssignedInstanceId(check.Result.HubAssignedInstanceId);

                                    _serviceLog.Warning($"EnsureMgmtHubConnection: hub assigned instance ID was previously empty, updated to new assigned ID.");
                                }
                                else if (_serverConfig.HubAssignedInstanceId != check.Result.HubAssignedInstanceId)
                                {
                                    AddSystemStatusItem(
                                        SystemStatusCategories.SERVICE_CORE,
                                        SystemStatusKeys.SERVICE_CORE_HUB_JOINING_AUTH,
                                        "Management Hub Joining Auth",
                                        "Management hub joining auth successful but hub assigned instance ID did not match. Current settings may be for a different hub. Hub assigned instance id updated.",
                                        hasError: true
                                    );

                                    SetHubAssignedInstanceId(check.Result.HubAssignedInstanceId);
                                }
                                else
                                {
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
                                    $"Management hub joining auth failed: {check.Message}",
                                    hasError: true
                                );

                                if (check.Result?.RejoinRequired == true)
                                {
                                    _serviceLog.Information("Hub rejoin required, will attempt to re-register instance.");

                                    // need to re-register
                                    _serviceLog.Warning("Hub requested rejoin but assigned identity is immutable. Manual re-enroll is required.");
                                }

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
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "EnsureMgmtHubConnection: unhandled exception while establishing/maintaining the management hub connection. Will retry on next scheduled check.");
            }
            finally
            {
                _hubConnectionCheckSync.Release();
            }
        }

        private void SendHeartbeatToManagementHub()
        {
            try
            {
                if (_managementServerClient == null || !_managementServerClient.IsConnected())
                {
                    _serviceLog.Warning("Cannot send heartbeat - not connected to Management Hub");

                    // Trigger reconnection attempt
                    _ = Task.Run(async () => await EnsureMgmtHubConnection());

                    return;
                }

                _managementServerClient.UpdateCachedInstanceInfo(GetManagedInstanceInfo());
                _managementServerClient.SendInstanceInfo(Guid.NewGuid(), isCommandResponse: false);

                _serviceLog.Debug("Heartbeat sent to Management Hub");
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Failed to send heartbeat to Management Hub");

                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                    "Management Hub Connection",
                    $"Heartbeat failed: {ex.Message}. Will attempt to reconnect shortly.",
                    hasWarning: true
                );
            }
        }

        public ManagedInstanceInfo GetManagedInstanceInfo()
        {
            return new ManagedInstanceInfo
            {
                Id = _serverConfig.HubAssignedInstanceId,
                InstanceId = _serverConfig.HubAssignedInstanceId,
                InternalInstanceId = CoreAppSettings.Current.InstanceId,
                Title = $"{Environment.MachineName}",
                OS = EnvironmentUtil.GetFriendlyOSName(detailed: false),
                OSVersion = EnvironmentUtil.GetOSVersion(),
                ClientVersion = Util.GetAppVersion().ToString(),
                ClientName = _isMgtmHubBackend ? "Certify Management Hub" : ConfigResources.AppName,
                License = _cachedLicenseCheck,
                IsDashboardEnabled = CoreAppSettings.Current.IsInstanceRegistered
            };
        }

        private async Task StartManagementHubConnection(string hubUri)
        {
            // fail fast if a token can't be acquired at all, otherwise the connection is made with the factory
            // so that each subsequent reconnect attempt presents a currently valid token
            var initialToken = await GetHubConnectionTokenAsync(CancellationToken.None);

            if (string.IsNullOrWhiteSpace(initialToken))
            {
                _serviceLog.Error("No hub connection auth token available, cannot connect to management hub.");
                return;
            }

            var appVersion = Util.GetAppVersion().ToString();

            var instanceInfo = GetManagedInstanceInfo();

            if (_managementServerClient != null)
            {
                // if not currently connected, attempt connection
                if (!_managementServerClient.IsConnected())
                {
                    _serviceLog.Information("Hub not connected, attempting connection. {hubUri}", hubUri);
                    await _managementServerClient.ConnectAsync(GetHubConnectionTokenAsync);
                }

                // if connected now, update status
                if (_managementServerClient.IsConnected())
                {
                    AddSystemStatusItem(
                            SystemStatusCategories.SERVICE_CORE,
                            SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                            "Management Hub Connection",
                            $"Successfully connected to Management Hub: {hubUri}"
                        );

                    // pick up any subscription update pushed while this instance was not connected
                    await RequestSubscriptionResyncFromMgmtHub("hub connection established");
                }
            }
            else
            {
                try
                {
                    _managementServerClient = new ManagementServerClient(hubUri, instanceInfo);

                    _managementServerClient.OnGetCommandResult += PerformHubCommandWithResult;
                    _managementServerClient.OnConnectionReconnecting += _managementServerClient_OnConnectionReconnecting;
                    _managementServerClient.OnConnectionReconnected += _managementServerClient_OnConnectionReconnected;
                    _managementServerClient.OnConnectionClosed += _managementServerClient_OnConnectionClosed;

                    await _managementServerClient.ConnectAsync(GetHubConnectionTokenAsync);

                    if (_managementServerClient.IsConnected())
                    {
                        AddSystemStatusItem(
                            SystemStatusCategories.SERVICE_CORE,
                            SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                            "Management Hub Connection",
                            $"Successfully connected to Management Hub: {hubUri}"
                        );

                        // pick up any subscription update pushed while this instance was not connected
                        await RequestSubscriptionResyncFromMgmtHub("hub connection established");
                    }
                    else
                    {
                        AddSystemStatusItem(
                            SystemStatusCategories.SERVICE_CORE,
                            SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                            "Management Hub Connection",
                            $"Could not connect to Management Hub at {hubUri}",
                            hasError: true
                        );
                    }
                }
                catch (Exception ex)
                {
                    AddSystemStatusItem(
                        SystemStatusCategories.SERVICE_CORE,
                        SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                        "Management Hub Connection",
                        $"Failed to connect to Management Hub at {hubUri}: {ex.Message}",
                        hasError: true
                    );
                }
            }
        }

        public async Task<InstanceCommandResult> PerformHubCommandWithResult(InstanceCommandRequest arg)
        {
            object val = null;

            if (arg.CommandType == ManagementHubCommands.GetInstanceInfo)
            {
                var update = GetManagedInstanceInfo();
                _managementServerClient.UpdateCachedInstanceInfo(update);
                val = update;
            }
            else if (arg.CommandType == ManagementHubCommands.GetManagedItem)
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
            else if (arg.CommandType == ManagementHubCommands.QueueAllStatusReports)
            {
                await QueueAllManagedCertificateStatusReports();
                val = true;
            }
            else if (arg.CommandType == ManagementHubCommands.GetManagedItemLog)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                var limit = args.FirstOrDefault(a => a.Key == "limit");

                val = await GetItemLog(managedCertIdArg.Value, int.Parse(limit.Value));
            }
            else if (arg.CommandType == ManagementHubCommands.GetSystemLogFiles)
            {
                val = await GetServiceLogFiles();
            }
            else if (arg.CommandType == ManagementHubCommands.GetSystemLog)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var logNameArg = args.FirstOrDefault(a => a.Key == "logName");
                var limit = args.FirstOrDefault(a => a.Key == "limit");

                val = await GetServiceLog(logNameArg.Value, int.Parse(limit.Value));
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
                var strictExportArg = args.FirstOrDefault(a => a.Key == "strictExport");
                var strictExport = false;
                if (strictExportArg.Key != null && bool.TryParse(strictExportArg.Value, out var se))
                {
                    strictExport = se;
                }

                val = await ExportCertificate(managedCertIdArg.Value, format.Value, strictExport);
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateManagedItem)
            {
                // update a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertArg = args.FirstOrDefault(a => a.Key == "managedCert");
                var managedCert = JsonSerializer.Deserialize<ManagedCertificate>(managedCertArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                val = await UpdateManagedCertificate(managedCert);
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveManagedItem)
            {
                // delete a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");

                val = await DeleteManagedCertificate(managedCertIdArg.Value);

                _ = PerformManagedChallengeCleanup(managedCertIdArg.Value);
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
            else if (arg.CommandType == ManagementHubCommands.ResetManagedItemStatus)
            {
                // test challenge response config for a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");

                val = await ResetManagedItemStatus(managedCertIdArg.Value);
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
                var deleteResult = await _credentialsManager.Delete(_itemManager, itemArg.Value);

                if (deleteResult?.IsSuccess == true)
                {
                    await RemoveHubItemTagsForItem(TaggedItemTypes.StoredCredential, itemArg.Value);
                }

                val = deleteResult;
            }
            else if (arg.CommandType == ManagementHubCommands.UnlockStoredCredential)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var itemArg = args.FirstOrDefault(a => a.Key == "storageKey");
                var key = itemArg.Value;
                var cred = await _credentialsManager.GetCredential(key);
                if (cred?.AllowUnlock == true)
                {
                    var unlockedCredValue = await _credentialsManager.GetUnlockedCredential(key);
                    if (unlockedCredValue != null)
                    {

                        cred.Secret = unlockedCredValue;
                        val = new StoredCredentialUnlockResult { IsSuccess = true, Result = cred };
                    }
                    else
                    {
                        val = null;
                    }
                }
                else
                {
                    val = new StoredCredentialUnlockResult { IsSuccess = false, Message = "This credential does not allow unlocking" };
                }
            }
            else if (arg.CommandType == ManagementHubCommands.GetChallengeProviders)
            {
                var providers = await Core.Management.Challenges.ChallengeProviders.GetChallengeAPIProviders();
                val = providers;
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
            else if (arg.CommandType == ManagementHubCommands.GetDeploymentProviderDefinition)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var idArg = args.FirstOrDefault(a => a.Key == "id");
                var configArg = args.FirstOrDefault(a => a.Key == "config");
                var config = string.IsNullOrWhiteSpace(configArg.Value)
                    ? null
                    : JsonSerializer.Deserialize<Certify.Config.DeploymentTaskConfig>(configArg.Value, JsonOptions.DefaultJsonSerializerOptions);

                val = await GetDeploymentProviderDefinition(idArg.Value, config);
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
                val = GetSystemStatusItems();
            }
            else if (arg.CommandType == ManagementHubCommands.GetDataStoreProviders)
            {
                val = await GetDataStoreProviders();
            }
            else if (arg.CommandType == ManagementHubCommands.GetDataStores)
            {
                val = await GetDataStores();
            }
            else if (arg.CommandType == ManagementHubCommands.TestDataStore)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var dataStoreArg = args.FirstOrDefault(a => a.Key == "dataStore");
                var dataStore = JsonSerializer.Deserialize<DataStoreConnection>(dataStoreArg.Value, JsonOptions.DefaultJsonSerializerOptions);
                val = await TestDataStoreConnection(dataStore);
            }
            else if (arg.CommandType == ManagementHubCommands.ApplyDataStoreSchemaMigrations)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var dataStoreArg = args.FirstOrDefault(a => a.Key == "dataStore");
                var dataStore = JsonSerializer.Deserialize<DataStoreConnection>(dataStoreArg.Value, JsonOptions.DefaultJsonSerializerOptions);
                val = await ApplyDataStoreSchemaMigrations(dataStore);
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateDataStore)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var dataStoreArg = args.FirstOrDefault(a => a.Key == "dataStore");
                var dataStore = JsonSerializer.Deserialize<DataStoreConnection>(dataStoreArg.Value, JsonOptions.DefaultJsonSerializerOptions);
                val = await UpdateDataStoreConnection(dataStore);
            }
            else if (arg.CommandType == ManagementHubCommands.SetDefaultDataStore)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var idArg = args.FirstOrDefault(a => a.Key == "dataStoreId");
                val = await SetDefaultDataStore(idArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.CopyDataStoreToTarget)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var sourceArg = args.FirstOrDefault(a => a.Key == "sourceId");
                var destArg = args.FirstOrDefault(a => a.Key == "destId");
                val = await CopyDateStoreToTarget(sourceArg.Value, destArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveDataStore)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var idArg = args.FirstOrDefault(a => a.Key == "dataStoreId");
                val = await RemoveDataStoreConnection(idArg.Value);
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
                    prefs.IsInstanceRegistered = update.IsInstanceRegistered;

                    prefs.NtpServer = update.NtpServer;
                    prefs.RenewalIntervalDays = update.RenewalIntervalDays;
                    prefs.RenewalIntervalMode = update.RenewalIntervalMode;
                    prefs.StoreCertificateIntermediates = update.StoreCertificateIntermediates;
                    prefs.UseModernPFXAlgs = update.UseModernPFXAlgs;

                    prefs.CertificateManagers = update.CertificateManagers;

                    prefs.MaintenanceWindows = update.MaintenanceWindows;
                    prefs.DefaultMaintenanceWindowId = update.DefaultMaintenanceWindowId;
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
                    _serverConfig.PowershellExecutionPolicy = configVal.PowershellExecutionPolicy;
                    _serverConfig.PreferModernPowershell = configVal.PreferModernPowershell;
                    _serverConfig.CustomPowerShellPaths = configVal.CustomPowerShellPaths ?? [];

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
            else if (arg.CommandType == ManagementHubCommands.ApplyLicense)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                var activationArg = args.FirstOrDefault(a => a.Key == "activation");
                var activation = JsonSerializer.Deserialize<LicenseKeyInstallResult>(activationArg.Value, JsonOptions.DefaultJsonSerializerOptions);
                if (activation != null)
                {
                    var settingsPath = EnvironmentUtil.EnsuredAppDataPath();
                    var productType = _isMgtmHubBackend ? 2 : 1; // 1 = ccm or agent, 2 = hub
                    _licensingManager.FinaliseInstall(productType, activation, settingsPath, CoreAppSettings.Current.InstanceId ?? string.Empty);

                    await RefreshCachedLicenseCheck();

                    // send updated instance info to hub
                    SendHeartbeatToManagementHub();

                    val = new ActionResult("Activated.", true);
                }
                else
                {
                    val = new ActionResult("Activation failed.", false);
                }
            }
            else if (arg.CommandType == ManagementHubCommands.DeactivateLicense)
            {
                var settingsPath = EnvironmentUtil.EnsuredAppDataPath();
                var productType = _isMgtmHubBackend ? 2 : 1; // 1 = ccm or agent, 2 = hub

                var i = new Models.Shared.RegisteredInstance
                {
                    InstanceId = CoreAppSettings.Current.InstanceId,
                    AppVersion = Management.Util.GetAppVersion().ToString()
                };

                var deactivated = await _licensingManager.DeactivateInstall(productType, settingsPath, null, i, CoreAppSettings.Current.InstanceId ?? string.Empty);

                if (deactivated)
                {
                    await RefreshCachedLicenseCheck();
                }

                // send updated instance info to hub
                SendHeartbeatToManagementHub();

                val = new ActionResult { IsSuccess = deactivated, Message = deactivated ? "Deactivated." : "Deactivation failed." };

            }
            else if (arg.CommandType == ManagementHubCommands.NotificationAuthenticationRequired)
            {
                _serviceLog.Information("Hub has requested that this instance re-authenticate");

                // discard the cached token so the next connection attempt acquires a new one
                StoreHubConnectionToken(null);

                await _managementServerClient.Disconnect();
            }
            else if (arg.CommandType == ManagementHubCommands.Reconnect)
            {
                _serviceLog.Information("Hub has requested that this instance re-connect");
                await _managementServerClient.Disconnect();
            }
            else if (arg.CommandType == ManagementHubCommands.RejoinManagementHub)
            {
                var rejoinRequest = JsonSerializer.Deserialize<ManagementHubRejoinRequest>(arg.Value ?? "{}", JsonOptions.DefaultJsonSerializerOptions);

                if (rejoinRequest == null
                    || string.IsNullOrWhiteSpace(rejoinRequest.JoiningCredential.ClientId)
                    || string.IsNullOrWhiteSpace(rejoinRequest.JoiningCredential.Secret))
                {
                    val = new ActionResult("Management hub rejoin command did not include a valid joining key.", false);
                }
                else
                {
                    _serviceLog.Information("Hub has requested that this instance rejoin using an updated joining key.");
                    _ = Task.Run(async () => await PerformManagementHubRejoin(rejoinRequest));
                    val = new ActionResult("Management hub rejoin initiated.", true);
                }
            }
            else if (arg.CommandType == ManagementHubCommands.RefreshExternalManagedCertificates)
            {
                _serviceLog.Information("Hub has requested that this instance refresh its external certificate manager cache.");
                _ = Task.Run(async () =>
                {
                    var refreshResult = await RefreshExternalManagedCertificateCache();
                    if (refreshResult.IsSuccess)
                    {
                        _serviceLog.Information(refreshResult.Message);
                    }
                    else if (refreshResult.IsWarning)
                    {
                        _serviceLog.Warning(refreshResult.Message);
                    }
                    else
                    {
                        _serviceLog.Error(refreshResult.Message);
                    }
                });
                val = new ActionResult("External certificate manager cache refresh initiated.", true);
            }
            else if (arg.CommandType == ManagementHubCommands.PushSubscriptionUpdate)
            {
                var update = JsonSerializer.Deserialize<SubscriptionUpdate>(arg.Value ?? "{}", JsonOptions.DefaultJsonSerializerOptions);

                if (!string.IsNullOrWhiteSpace(update?.ManagedCertificateId))
                {
                    val = await MarkSubscriptionUpdateAvailable(update.ManagedCertificateId, update.SourceVersion);
                }
                else
                {
                    val = new ActionResult("External managed certificate update did not include a target managed certificate id.", false);
                }
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

        /// <summary>
        /// The minimum time between subscription resync requests, so a flapping connection does not ask the hub to
        /// re-send every subscription version on each reconnect
        /// </summary>
        private static readonly TimeSpan _minSubscriptionResyncInterval = TimeSpan.FromMinutes(5);

        private long _lastSubscriptionResyncTicks = DateTimeOffset.MinValue.UtcTicks;

        /// <summary>
        /// Determine whether a certificate subscription needs the hub to re-send its current source version, and if so
        /// resolve the source it refers to. Only a subscription which depends on being told about updates needs this -
        /// one which polls its source will pick an update up on its own interval
        /// </summary>
        /// <param name="item"></param>
        /// <param name="sourceInstanceId">the instance which owns the source certificate</param>
        /// <param name="sourceManagedCertificateId">the source certificate on that instance</param>
        /// <returns></returns>
        internal static bool RequiresSubscriptionResync(ManagedCertificate item, out string sourceInstanceId, out string sourceManagedCertificateId)
        {
            sourceInstanceId = string.Empty;
            sourceManagedCertificateId = string.Empty;

            var sourceConfig = item?.ExternalSource;

            if (sourceConfig == null || !item.IsActionableSubscription)
            {
                return false;
            }

            // a subscription which polls its source does not depend on being told about updates
            if (!IsPushModeEnabled(sourceConfig))
            {
                return false;
            }

            // only the hub can answer this, other source types have no push notification to re-send
            if (!string.Equals(sourceConfig.SourceType, ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ManagedCertificate.TryParseManagementHubReference(sourceConfig.ExternalReference, out sourceInstanceId, out sourceManagedCertificateId);
        }

        /// <summary>
        /// Ask the hub to re-send the current source version for each certificate subscription which depends on push
        /// notifications.
        /// A push issued while this instance was disconnected is dropped by the hub rather than queued, so without this
        /// an update which arrived during a restart or a network outage would not be applied until the subscription's
        /// own renewal fell due - and for a push only subscription, which never polls, not at all.
        /// A pull capable subscription checks its source on its own interval, so it is left to do that.
        /// </summary>
        private async Task RequestSubscriptionResyncFromMgmtHub(string reason)
        {
            if (_managementServerClient?.IsConnected() != true || IsInDegradedMode)
            {
                return;
            }

            // the slot is claimed atomically before any work is done, because the heartbeat and the reconnected event
            // can both arrive at once and would otherwise each request a resync
            var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            var lastResyncTicks = System.Threading.Interlocked.Read(ref _lastSubscriptionResyncTicks);

            if (nowTicks < lastResyncTicks + _minSubscriptionResyncInterval.Ticks
                || System.Threading.Interlocked.CompareExchange(ref _lastSubscriptionResyncTicks, nowTicks, lastResyncTicks) != lastResyncTicks)
            {
                _serviceLog?.Debug("Skipping certificate subscription resync ({reason}), one was requested recently.", reason);
                return;
            }

            try
            {
                var subscriptions = await GetSubscriptionTargets();

                var requested = 0;

                foreach (var item in subscriptions)
                {
                    if (!RequiresSubscriptionResync(item, out var sourceInstanceId, out var sourceManagedCertificateId))
                    {
                        continue;
                    }

                    _managementServerClient.SendNotificationToManagementHub(
                        ManagementHubCommands.NotificationRequestSubscriptionUpdate,
                        new SubscriptionUpdateRequest
                        {
                            TargetManagedCertificateId = item.Id,
                            SourceInstanceId = sourceInstanceId,
                            SourceManagedCertificateId = sourceManagedCertificateId
                        });

                    requested++;
                }

                if (requested > 0)
                {
                    _serviceLog?.Information("Requested current certificate subscription versions from hub for {count} item(s) ({reason}).", requested, reason);
                }
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "Failed to request certificate subscription versions from the management hub ({reason}). Subscriptions will be checked again on the next connection.", reason);
            }
        }

        private void ReportManagedItemUpdateToMgmtHub(ManagedCertificate item)
        {
            if (item == null || _managementServerClient == null)
            {
                return;
            }

            try
            {
                if (_managementServerClient?.IsConnected() == true)
                {
                    _managementServerClient.SendNotificationToManagementHub(
                        ManagementHubCommands.NotificationUpdatedManagedItem, item);

                    _serviceLog.Debug("Reported managed item update to hub for {itemId}", item.Id);
                }
                else
                {
                    _serviceLog.Warning("Cannot report managed item update - not connected to hub. Update for {itemId} not sent.", item.Id);
                }
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Failed to report managed item update to hub for {itemId}", item.Id);
            }
        }

        private void ReportManagedItemDeleteToMgmtHub(string id)
        {
            if (_managementServerClient == null)
            {
                return;
            }

            try
            {
                if (_managementServerClient?.IsConnected() == true)
                {
                    _managementServerClient.SendNotificationToManagementHub(
                        ManagementHubCommands.NotificationRemovedManagedItem, id);

                    _serviceLog.Debug("Reported managed item deletion to hub for {itemId}", id);
                }
                else
                {
                    _serviceLog.Warning("Cannot report managed item deletion - not connected to hub. Deletion for {itemId} not sent.", id);
                }
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Failed to report managed item deletion to hub for {itemId}", id);
            }
        }

        private void ReportRequestProgressToMgmtHub(RequestProgressState progress)
        {
            if (_managementServerClient == null)
            {
                return;
            }

            try
            {
                if (_managementServerClient?.IsConnected() == true)
                {
                    _managementServerClient.SendNotificationToManagementHub(
                        ManagementHubCommands.NotificationManagedItemRequestProgress, progress);

                    _serviceLog.Debug("Reported request progress to hub for {itemId}", progress.ManagedCertificate?.Id);
                }
                else
                {
                    _serviceLog.Debug("Cannot report request progress - not connected to hub. Progress for {itemId} not sent.", progress.ManagedCertificate?.Id);
                }
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Failed to report request progress to hub for {itemId}", progress.ManagedCertificate?.Id);
            }
        }

        private void _managementServerClient_OnConnectionReconnecting()
        {
            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                "Management Hub Connection",
                "Attempting to reconnect to Management Hub",
                hasWarning: true
            );
        }

        private void _managementServerClient_OnConnectionReconnected()
        {
            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                "Management Hub Connection",
                "Successfully reconnected to Management Hub"
            );

            // Re-register instance with updated information after reconnection
            try
            {
                SendHeartbeatToManagementHub();
                _serviceLog.Debug("Sent heartbeat after reconnection to re-register instance");
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Failed to send heartbeat after reconnection");
            }

            // any subscription update pushed while the connection was down was dropped rather than queued, so the
            // current source versions are requested again now the instance can receive them
            _ = Task.Run(() => RequestSubscriptionResyncFromMgmtHub("hub connection re-established"));
        }

        private void _managementServerClient_OnConnectionClosed()
        {
            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                "Management Hub Connection",
                "Connection to Management Hub lost. Will attempt to reconnect.",
                hasError: true
            );
        }

        private async Task GenerateDemoItems(int? numItems)
        {
            var currentItems = await GetManagedCertificateSummary(new ManagedCertificateFilter { Keyword = "DemoData" });
            if (currentItems.Total == 0)
            {
                var items = DemoDataGenerator.GenerateDemoItems(numItems ?? 100, numItems ?? 500);
                foreach (var item in items)
                {
                    _ = UpdateManagedCertificate(item);
                }
            }
        }

        private async Task RandomlyUpdateDemoItems()
        {
            // randomly update status of demo items
            var items = await GetManagedCertificates(new ManagedCertificateFilter { IncludeExternal = true, Keyword = "DemoData" });
            var rand = new Random();

            // randomly update status for a few demo items
            foreach (var item in items)
            {
                if (rand.NextDouble() < 0.02) // 2% chance to update
                {
                    item.LastRenewalStatus = rand.NextDouble() < 0.8 ? RequestState.Success : RequestState.Error;

                    if (item.LastRenewalStatus == RequestState.Error)
                    {
                        item.RenewalFailureCount++;
                        item.RenewalFailureMessage = "Simulated renewal failure for demo purposes.";
                    }
                    else
                    {
                        item.RenewalFailureCount = 0;
                        item.RenewalFailureMessage = null;
                    }

                    item.DateLastRenewalAttempt = DateTimeOffset.UtcNow;

                    _ = UpdateManagedCertificate(item);
                }
            }

            // randomly remove a few demo items

            foreach (var item in items)
            {
                if (rand.NextDouble() < 0.01) // 10% chance to remove
                {
                    _ = DeleteManagedCertificate(item.Id);
                }
            }

            // randomly add a few demo items

            for (var i = 0; i < 5; i++)
            {
                if (rand.NextDouble() < 0.03) // 3% chance to add
                {
                    var newItems = DemoDataGenerator.GenerateDemoItems(1, 1);
                    foreach (var newItem in newItems)
                    {
                        _ = UpdateManagedCertificate(newItem);
                    }
                }
            }
        }
    }
}
