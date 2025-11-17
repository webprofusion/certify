using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Models.Shared;
using Certify.Shared;
using Certify.Shared.Core.Utils;
using Microsoft.IdentityModel.JsonWebTokens;
using Registration.Core.Models.Shared;

namespace Certify.Management
{
    public partial class CertifyManager
    {
     
        // Hub-specific metrics (using the same meter instance from main CertifyManager class)
        private static readonly Counter<int> _hubConnectionAttemptsCounter = _meter.CreateCounter<int>("certify.hub.connection_attempts", "attempts", "Number of hub connection attempts");
        private static readonly Counter<int> _hubConnectionSuccessCounter = _meter.CreateCounter<int>("certify.hub.connection_success", "connections", "Number of successful hub connections");
        private static readonly Counter<int> _hubConnectionFailuresCounter = _meter.CreateCounter<int>("certify.hub.connection_failures", "failures", "Number of hub connection failures");
        private static readonly Counter<int> _hubCommandsProcessedCounter = _meter.CreateCounter<int>("certify.hub.commands_processed", "commands", "Number of hub commands processed");
        private static readonly Counter<int> _hubCommandsFailedCounter = _meter.CreateCounter<int>("certify.hub.commands_failed", "commands", "Number of hub commands that failed");
        private static readonly Counter<int> _hubHeartbeatsCounter = _meter.CreateCounter<int>("certify.hub.heartbeats", "heartbeats", "Number of heartbeats sent to hub");
        
        private static readonly Histogram<double> _hubCommandDurationHistogram = _meter.CreateHistogram<double>("certify.hub.command_duration", "ms", "Duration of hub command processing");
        private static readonly Histogram<double> _hubConnectionDurationHistogram = _meter.CreateHistogram<double>("certify.hub.connection_duration", "ms", "Duration of hub connection establishment");
        
        private static int _hubConnectionActive = 0;
        private static readonly ObservableGauge<int> _hubConnectionActiveGauge = _meter.CreateObservableGauge<int>("certify.hub.connection_active", () => _hubConnectionActive, "connections", "Whether hub connection is currently active");

        private IManagementServerClient _managementServerClient;
        private bool _isDirectMgmtHubBackend = false;
        private bool _isMgtmHubBackend = false;
        private bool _isHubConnectionErrorLogged = false;
        private ClientSecret _mgmtHubJoiningSecret;
        public const string MgmtHubJoiningCredId = "_ManagementHubJoiningKey";
        private string _mgmtHubJoiningToken = default!;

        public async Task<ActionResult> CheckManagementHubConnectionStatus()
        {
            using var activity = _activitySource.StartActivity("CheckManagementHubConnectionStatus", ActivityKind.Internal);
            
            var isConnected = _managementServerClient?.IsConnected() == true;
            
            activity?.SetTag("hub.is_connected", isConnected);
            activity?.SetStatus(ActivityStatusCode.Ok);
            
            if (isConnected)
            {
                _serviceLog?.Debug("Hub connection status check: Connected");
                return new ActionResult("Connected to Management Hub.", isSuccess: true);
            }
            else
            {
                _serviceLog?.Debug("Hub connection status check: Not connected");
                return new ActionResult("Not connected to Management Hub.", isSuccess: false);
            }
        }

        public async Task<ActionResult> JoinManagementHub(string url, ClientSecret clientSecret)
        {
            using var activity = _activitySource.StartActivity("JoinManagementHub", ActivityKind.Internal);
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                activity?.SetTag("hub.url", url);
                activity?.SetTag("hub.client_id", clientSecret?.ClientId);
                
                _serviceLog?.Information("Attempting to join Management Hub at {HubUrl}", url);
                
                _hubConnectionAttemptsCounter.Add(1, new KeyValuePair<string, object>("operation", "join"));

                _serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

                var registerInstance = true;
                ActionResult<HubJoiningInfo> joiningCredentialsCheck = null;

                if (!string.IsNullOrWhiteSpace(_serverConfig.HubAssignedInstanceId))
                {
                    activity?.SetTag("hub.assigned_instance_id", _serverConfig.HubAssignedInstanceId);
                    activity?.AddEvent(new ActivityEvent("CheckingExistingRegistration"));
                    
                    // when have already joined a hub, first check if we are rejoining the same hub by just verifying the credentials
                    joiningCredentialsCheck = await CheckManagementHubCredentials(url, clientSecret, registerInstance: false);

                    if (joiningCredentialsCheck.IsSuccess)
                    {
                        // already registered, just joining again
                        registerInstance = false;
                        activity?.AddEvent(new ActivityEvent("RejoiningExistingHub"));
                        _serviceLog?.Information("Rejoining existing hub with instance ID {InstanceId}", _serverConfig.HubAssignedInstanceId);
                    }
                    else
                    {
                        // if we are not rejoining the same hub (or our credentials failed), we need to register a new instance
                        registerInstance = true;
                        _serverConfig.HubAssignedInstanceId = null;
                        activity?.AddEvent(new ActivityEvent("RegisteringNewInstance", 
                            tags: new ActivityTagsCollection { { "reason", "credentials_failed" } }));
                        _serviceLog?.Warning("Previous hub credentials failed, registering as new instance");
                    }
                }
                else
                {
                    _serviceLog.Information("Hub not yet joined, will attempt to join.");
                    registerInstance = true;
                    activity?.AddEvent(new ActivityEvent("FirstTimeRegistration"));
                }

                activity?.SetTag("hub.register_instance", registerInstance);

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
                        activity?.AddEvent(new ActivityEvent("AssignedNewInstanceId", 
                            tags: new ActivityTagsCollection { { "instance_id", _serverConfig.HubAssignedInstanceId } }));
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
                        
                        stopwatch.Stop();
                        
                        _hubConnectionSuccessCounter.Add(1, new KeyValuePair<string, object>("operation", "join"));
                        _hubConnectionDurationHistogram.Record(stopwatch.ElapsedMilliseconds,
                            new KeyValuePair<string, object>("operation", "join"),
                            new KeyValuePair<string, object>("is_success", true));
                        
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        activity?.SetTag("hub.connection_duration_ms", stopwatch.ElapsedMilliseconds);
                        
                        _serviceLog?.Information("Successfully joined Management Hub at {HubUrl} with instance ID {InstanceId}", 
                            url, _serverConfig.HubAssignedInstanceId);
                        
                        return new ActionResult("Connected to Management Hub.", isSuccess: true);
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        
                        activity?.SetStatus(ActivityStatusCode.Error, "Connection failed after joining");
                        activity?.AddException(ex);
                        
                        _hubConnectionFailuresCounter.Add(1, 
                            new KeyValuePair<string, object>("operation", "join"),
                            new KeyValuePair<string, object>("error", ex.GetType().Name));
                        
                        _serviceLog?.Error(ex, "Failed to connect to hub after successful join");
                        
                        return new ActionResult("A problem occurred when connecting to the management hub. Check URL and credentials.", isSuccess: false);
                    }
                }
                else
                {
                    stopwatch.Stop();
                    
                    activity?.SetStatus(ActivityStatusCode.Error, joiningCredentialsCheck.Message);
                    
                    _hubConnectionFailuresCounter.Add(1, 
                        new KeyValuePair<string, object>("operation", "join"),
                        new KeyValuePair<string, object>("error", "credentials_check_failed"));
                    
                    _serviceLog?.Error("Hub joining credentials check failed: {Message}", joiningCredentialsCheck.Message);
                    
                    return joiningCredentialsCheck;
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                
                _hubConnectionFailuresCounter.Add(1, 
                    new KeyValuePair<string, object>("operation", "join"),
                    new KeyValuePair<string, object>("error", ex.GetType().Name));
                
                _serviceLog?.Error(ex, "Fatal error joining Management Hub");
                
                return new ActionResult($"Fatal error joining hub: {ex.Message}", isSuccess: false);
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
            using var activity = _activitySource.StartActivity("CheckManagementHubCredentials", ActivityKind.Internal);
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                activity?.SetTag("hub.url", url);
                activity?.SetTag("hub.register_instance", registerInstance);
                activity?.SetTag("hub.client_id", clientSecret?.ClientId);

                var handler = new HttpClientHandler()
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
                    {
                        // Allow all certificates (including untrusted ones)
                        return true;
                    }
                };

                if (Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_ALLOW_UNTRUSTED") == "true")
                {
                    handler.ServerCertificateCustomValidationCallback = null;
                }

                var endpoint = $"{url.TrimEnd('/')}/api/v1/hub/{(registerInstance ? "register" : "joincheck")}";
                
                activity?.SetTag("hub.endpoint", endpoint);

                _serviceLog.Information("Checking credentials via Management Hub {url}", endpoint);

                using (var httpClient = new System.Net.Http.HttpClient(handler))
                {
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
                        
                        stopwatch.Stop();
                        
                        activity?.SetTag("hub.http_status_code", (int)response.StatusCode);
                        activity?.SetTag("hub.response_time_ms", stopwatch.ElapsedMilliseconds);

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            var hubInfo = JsonSerializer.Deserialize<HubJoiningInfo>(json, JsonOptions.DefaultJsonSerializerOptions);
                            
                            activity?.SetTag("hub.assigned_instance_id", hubInfo?.HubAssignedInstanceId);
                            activity?.SetStatus(ActivityStatusCode.Ok);
                            
                            _serviceLog?.Information("Hub credentials check successful, assigned instance ID: {InstanceId}", 
                                hubInfo?.HubAssignedInstanceId);
                            
                            return new ActionResult<HubJoiningInfo>("Connected to Management Hub.", isSuccess: true, hubInfo);
                        }
                        else
                        {
                            activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {response.StatusCode}");
                            
                            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                            {
                                _serviceLog?.Error("Hub credentials check failed: Unauthorized");
                                return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub (Unauthorized). Check credentials {endpoint} {clientSecret.ClientId} {clientSecret.Secret} {_serverConfig.HubAssignedInstanceId}. {response}", isSuccess: false);
                            }
                            else
                            {
                                _serviceLog?.Error("Hub credentials check failed: {StatusCode}", response.StatusCode);
                                return new ActionResult<HubJoiningInfo>("Could not connect to Management Hub. Check URL.", isSuccess: false);
                            }
                        }
                    }
                    catch (HttpRequestException httpEx) when (httpEx.InnerException is System.Net.Sockets.SocketException socketEx && socketEx.ErrorCode == 111)
                    {
                        stopwatch.Stop();
                        
                        activity?.SetStatus(ActivityStatusCode.Error, "Connection refused");
                        activity?.AddException(httpEx);
                        
                        _serviceLog?.Error(httpEx, "Hub connection refused at {Endpoint}", endpoint);
                        
                        return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. Connection refused (port may not be open or service not running). {endpoint}", isSuccess: false);
                    }
                    catch (HttpRequestException httpEx)
                    {
                        stopwatch.Stop();
                        
                        activity?.SetStatus(ActivityStatusCode.Error, "Network error");
                        activity?.AddException(httpEx);
                        
                        _serviceLog?.Error(httpEx, "Hub network error");
                        
                        return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. Network error: {httpEx.Message}", isSuccess: false);
                    }
                    catch (Exception exp)
                    {
                        stopwatch.Stop();
                        
                        activity?.SetStatus(ActivityStatusCode.Error, exp.Message);
                        activity?.AddException(exp);
                        
                        _serviceLog?.Error(exp, "Hub credentials check failed");
                        
                        return new ActionResult<HubJoiningInfo>($"Could not connect to Management Hub. {exp}", isSuccess: false);
                    }
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                
                _serviceLog?.Error(ex, "Fatal error checking hub credentials");
                
                return new ActionResult<HubJoiningInfo>($"Fatal error: {ex.Message}", isSuccess: false);
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
        
        private async Task EnsureMgmtHubConnection()
        {
            using var activity = _activitySource.StartActivity("EnsureMgmtHubConnection", ActivityKind.Internal);
            
            try
            {
                var wasConnected = _managementServerClient?.IsConnected() == true;
                
                activity?.SetTag("hub.was_connected", wasConnected);
                activity?.SetTag("hub.is_direct_backend", _isDirectMgmtHubBackend);

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
                            activity?.AddEvent(new ActivityEvent("JoiningTokenExpired"));
                            _serviceLog?.Warning("Hub joining token expired, will acquire new token");
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
                        
                        activity?.SetTag("hub.uri", mgmtHubUri);

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

                                        activity?.AddEvent(new ActivityEvent("JoiningSecretFromEnvironment"));

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
                                                activity?.AddEvent(new ActivityEvent("JoiningSecretFromCredentialStore"));
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            activity?.AddException(ex);
                                            _serviceLog.Error(ex, "Error retrieving management hub joining key from credentials store.");
                                        }
                                    }

                                    if (_mgmtHubJoiningSecret == null)
                                    {
                                        activity?.SetStatus(ActivityStatusCode.Error, "No joining secret available");

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
                                        activity?.SetStatus(ActivityStatusCode.Error, "Instance ID mismatch");

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
                                        
                                        activity?.AddEvent(new ActivityEvent("AcquiredJoiningToken"));
                                    }
                                }
                                else
                                {
                                    activity?.SetStatus(ActivityStatusCode.Error, "Failed to acquire joining token");

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
                
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                
                _serviceLog?.Error(ex, "Error ensuring hub connection");
            }
        }

        private void SendHeartbeatToManagementHub()
        {
            using var activity = _activitySource.StartActivity("SendHeartbeatToManagementHub", ActivityKind.Internal);
            
            try
            {
                if (_managementServerClient == null || !_managementServerClient.IsConnected())
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Not connected");
                    
                    _serviceLog.Warning("Cannot send heartbeat - not connected to Management Hub");
                    
                    // Trigger reconnection attempt
                    _ = Task.Run(async () => await EnsureMgmtHubConnection());
                    
                    return;
                }
                
                _managementServerClient.UpdateCachedInstanceInfo(GetManagedInstanceInfo());
                _managementServerClient.SendInstanceInfo(Guid.NewGuid(), isCommandResponse: false);
                
                _hubHeartbeatsCounter.Add(1);
                activity?.SetStatus(ActivityStatusCode.Ok);
                
                _serviceLog.Debug("Heartbeat sent to Management Hub");
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                
                _serviceLog.Error(ex, "Failed to send heartbeat to Management Hub");
                
                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                    "Management Hub Connection",
                    $"Heartbeat failed: {ex.Message}. Will attempt to reconnect.",
                    hasWarning: true
                );
                
                // Trigger reconnection attempt
                _ = Task.Run(async () => 
                {
                    await Task.Delay(1000);
                    await EnsureMgmtHubConnection();
                });
            }
        }

        public ManagedInstanceInfo GetManagedInstanceInfo()
        {
            return new ManagedInstanceInfo
            {
                Id = _serverConfig.HubAssignedInstanceId,
                InstanceId = _serverConfig.HubAssignedInstanceId,
                Title = $"{Environment.MachineName}",
                OS = EnvironmentUtil.GetFriendlyOSName(detailed: false),
                OSVersion = EnvironmentUtil.GetOSVersion(),
                ClientVersion = Util.GetAppVersion().ToString(),
                ClientName = _isMgtmHubBackend ? "Certify Management Hub" : ConfigResources.AppName,
                License = _cachedLicenseCheck
            };
        }

        private async Task StartManagementHubConnection(string hubUri)
        {
            using var activity = _activitySource.StartActivity("StartManagementHubConnection", ActivityKind.Internal);
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                if (string.IsNullOrWhiteSpace(_mgmtHubJoiningToken))
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "No joining token");
                    _serviceLog.Error("No joining token available, cannot connect to management hub.");
                    return;
                }

                activity?.SetTag("hub.uri", hubUri);
                
                _serviceLog.Debug("Attempting connection to management hub {hubUri}", hubUri);

                var appVersion = Util.GetAppVersion().ToString();
                var instanceInfo = GetManagedInstanceInfo();
                
                activity?.SetTag("instance.id", instanceInfo.InstanceId);
                activity?.SetTag("instance.version", appVersion);

                if (_managementServerClient != null)
                {
                    _managementServerClient.OnGetCommandResult -= PerformHubCommandWithResult;
                    _managementServerClient.OnConnectionReconnecting -= _managementServerClient_OnConnectionReconnecting;
                    _managementServerClient.OnConnectionReconnected -= _managementServerClient_OnConnectionReconnected;
                    _managementServerClient.OnConnectionClosed -= _managementServerClient_OnConnectionClosed;
                }

                _managementServerClient = new ManagementServerClient(hubUri, instanceInfo);
                _managementServerClient.SetJoiningToken(_mgmtHubJoiningToken);

                try
                {
                    await _managementServerClient.ConnectAsync();

                    _managementServerClient.OnGetCommandResult += PerformHubCommandWithResult;
                    _managementServerClient.OnConnectionReconnecting += _managementServerClient_OnConnectionReconnecting;
                    _managementServerClient.OnConnectionReconnected += _managementServerClient_OnConnectionReconnected;
                    _managementServerClient.OnConnectionClosed += _managementServerClient_OnConnectionClosed;

                    stopwatch.Stop();
                    
                    System.Threading.Interlocked.Exchange(ref _hubConnectionActive, 1);
                    
                    _hubConnectionSuccessCounter.Add(1, new KeyValuePair<string, object>("operation", "connect"));
                    _hubConnectionDurationHistogram.Record(stopwatch.ElapsedMilliseconds,
                        new KeyValuePair<string, object>("operation", "connect"),
                        new KeyValuePair<string, object>("is_success", true));
                    
                    activity?.SetTag("hub.connection_duration_ms", stopwatch.ElapsedMilliseconds);
                    activity?.SetStatus(ActivityStatusCode.Ok);

                    _serviceLog.Information("Connected to management hub {hubUri}", hubUri);
                    
                    _isHubConnectionErrorLogged = false; // Reset error flag on successful connection
                    
                    AddSystemStatusItem(
                        SystemStatusCategories.SERVICE_CORE,
                        SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                        "Management Hub Connection",
                        "Successfully connected to Management Hub"
                    );
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    
                    System.Threading.Interlocked.Exchange(ref _hubConnectionActive, 0);
                    
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.AddException(ex);
                    
                    _hubConnectionFailuresCounter.Add(1, 
                        new KeyValuePair<string, object>("operation", "connect"),
                        new KeyValuePair<string, object>("error", ex.GetType().Name));
                    
                    if (!_isHubConnectionErrorLogged)
                    {
                        _serviceLog.Error(ex, "Could not connect to Certify Management Hub {hubUri}. Service may not be currently available. Will retry periodically.", hubUri);
                        _isHubConnectionErrorLogged = true;
                        
                        AddSystemStatusItem(
                            SystemStatusCategories.SERVICE_CORE,
                            SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                            "Management Hub Connection",
                            $"Failed to connect to Management Hub: {ex.Message}",
                            hasError: true
                        );
                    }

                    _managementServerClient = null;
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                
                _serviceLog?.Error(ex, "Fatal error starting hub connection");
            }
        }

        public async Task<InstanceCommandResult> PerformHubCommandWithResult(InstanceCommandRequest arg)
        {
            using var activity = _activitySource.StartActivity("PerformHubCommandWithResult", ActivityKind.Internal);
            var stopwatch = Stopwatch.StartNew();

            object val = null;

            try
            {
                activity?.SetTag("hub.command_type", arg.CommandType);
                activity?.SetTag("hub.command_id", arg.CommandId);
                
                _serviceLog?.Debug("Processing hub command: {CommandType} ({CommandId})", arg.CommandType, arg.CommandId);
                
                _hubCommandsProcessedCounter.Add(1, 
                    new KeyValuePair<string, object>("command_type", arg.CommandType ?? "unknown"));

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
                    val = await _credentialsManager.Delete(_itemManager, itemArg.Value);
                }
                else if (arg.CommandType == ManagementHubCommands.UnlockStoredCredential)
                {
                    var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                    var itemArg = args.FirstOrDefault(a => a.Key == "storageKey");
                    var key = itemArg.Value;
                    var cred = await _credentialsManager.GetCredential(key);
                    if (cred.AllowUnlock)
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
                else if (arg.CommandType == ManagementHubCommands.ApplyLicense)
                {
                    var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value, JsonOptions.DefaultJsonSerializerOptions);
                    var activationArg = args.FirstOrDefault(a => a.Key == "activation");
                    var activation = JsonSerializer.Deserialize<LicenseKeyInstallResult>(activationArg.Value, JsonOptions.DefaultJsonSerializerOptions);
                    if (activation != null)
                    {
                        var settingsPath = EnvironmentUtil.EnsuredAppDataPath();
                        var productType = _isMgtmHubBackend ? 2 : 1; // 1 = ccm or agent, 2 = hub
                        _licensingManager.FinaliseInstall(productType, activation, settingsPath);

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

                    var deactivated = await _licensingManager.DeactivateInstall(productType, settingsPath, null, i);

                    if (deactivated)
                    {
                        await RefreshCachedLicenseCheck();
                    }

                    // send updated instance info to hub
                    SendHeartbeatToManagementHub();

                    val = new ActionResult { IsSuccess = deactivated, Message = deactivated ? "Deactivated." : "Deactivation failed." };

                }
                else if (arg.CommandType == ManagementHubCommands.Reconnect)
                {
                    await _managementServerClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "Error processing hub command: {CommandType}", arg.CommandType);
                
                val = new InstanceCommandResult
                {
                    CommandId = arg.CommandId,
                    Value = JsonSerializer.Serialize(new ActionResult
                    {
                        IsSuccess = false,
                        Message = "Error processing command",
                        Result = ex.Message
                    }),
                    ObjectValue = null
                };
            }
            finally
            {
                stopwatch.Stop();
                
                activity?.SetTag("hub.command_duration_ms", stopwatch.ElapsedMilliseconds);
                activity?.SetStatus(ActivityStatusCode.Ok);
                
                _hubCommandDurationHistogram.Record(stopwatch.ElapsedMilliseconds,
                    new KeyValuePair<string, object>("command_type", arg.CommandType ?? "unknown"),
                    new KeyValuePair<string, object>("is_success", true));
                
                _serviceLog?.Debug("Hub command completed: {CommandType} in {ElapsedMs}ms", 
                    arg.CommandType, stopwatch.ElapsedMilliseconds);
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
            if (item == null)
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
            _serviceLog.Warning("Reconnecting to Management Hub...");
            
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
            _serviceLog.Information("Successfully reconnected to Management Hub");
            
            _isHubConnectionErrorLogged = false; // Reset error flag
            
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
        }

        private void _managementServerClient_OnConnectionClosed()
        {
            _serviceLog.Error("Management Hub connection closed");
            
            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
                "Management Hub Connection",
                "Connection to Management Hub lost. Will attempt to reconnect.",
                hasError: true
            );
            
            // Trigger reconnection attempt after a delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                
                try
                {
                    _serviceLog.Information("Attempting to re-establish connection to Management Hub");
                    await EnsureMgmtHubConnection();
                }
                catch (Exception ex)
                {
                    _serviceLog.Error(ex, "Failed to re-establish connection to Management Hub");
                }
            });
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
