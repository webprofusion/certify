using System.Security.Claims;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Providers;
using Certify.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Hub.Api.SignalR.ManagementHub
{

    /// <summary>
    /// Individual backend/agent instances connect as clients to this hub to send back managed item updates, progress reports, config settings. 
    /// Instances receive commands (managed item updates etc, config updates)
    /// This also uses direct communication with certifyManager if talking to the local management hub instance
    /// This works in conjunction with the InstanceManagementStateProvider to track instance connections and state and Management API to send commands to instances
    /// </summary>
    public class InstanceManagementHub : Hub<IInstanceManagementHub>, IInstanceManagementHub
    {
        private IInstanceManagementStateProvider _stateProvider;
        private ILogger<InstanceManagementHub> _logger;
        private IHubContext<UserInterfaceStatusHub> _uiStatusHub;
        private ICertifyManager? _certifyManager;
        private ICertifyInternalApiClient? _backendClient;
        private IConfiguration _config;
        private readonly string _localInstanceId = default!;
        private bool _hasLocalInstance => _certifyManager != null;

        /// <summary>
        /// Set up instance management hub
        /// </summary>
        /// <param name="stateProvider"></param>
        /// <param name="logger"></param>
        /// <param name="uiStatusHub"></param>
        /// <param name="config"></param>
        /// <param name="backendClient"></param>
        /// <param name="certifyManager"></param>
        public InstanceManagementHub(
            IInstanceManagementStateProvider stateProvider,
            ILogger<InstanceManagementHub> logger,
            IHubContext<UserInterfaceStatusHub> uiStatusHub,
            IConfiguration config,
            ICertifyInternalApiClient backendClient,
            ICertifyManager? certifyManager = null
            )
        {
            _stateProvider = stateProvider;
            _logger = logger;
            _uiStatusHub = uiStatusHub;
            _config = config;
            _certifyManager = certifyManager;
            _backendClient = backendClient;

            // If we have a local certify manager, register it as a special local instance
            // this is so we can talk to it directly without going via SignalR
            if (_hasLocalInstance)
            {
                // Create a unique local instance connection id
                _localInstanceId = _certifyManager!.GetManagedInstanceInfo().InstanceId;
            }

            _config = config;
        }

        /// <summary>
        /// If true, abort connections from instances that fail authentication to force them to re-authenticate
        /// </summary>
        bool _abortConnectionsWhenNotAuthenticated = true;

        /// <summary>
        /// Handle connection event from an instance using SignalR
        /// </summary>
        /// <returns></returns>
        public async override Task OnConnectedAsync()
        {
            _logger?.LogDebug("InstanceManagementHub: Remote instance connected to management hub..");

            // validate jwt passed by joining instance
            var isAuthenticated = false;
            var hubAssignedId = String.Empty;

            try
            {
                var accessToken = Context.GetHttpContext()?.Request.Headers.Authorization;
                if (!string.IsNullOrWhiteSpace(accessToken?.ToString()))
                {
                    var joiningJwt = accessToken.ToString().Replace("Bearer ", "");
                    var jwtService = new Hub.Api.Services.JwtService(_config);

                    var claimsIdentity = await jwtService.ClaimsIdentityFromTokenAsync(joiningJwt, true);
                    var userId = claimsIdentity.FindFirst(ClaimTypes.Sid)?.Value;
                    hubAssignedId = claimsIdentity.FindFirst("hub-assigned-id")?.Value;
                    isAuthenticated = true;
                }
                else
                {
                    _logger?.LogWarning("InstanceManagementHub: No JWT token provided by instance. Connection attempt aborted.");

                    await Clients.Caller.SendCommandRequest(new InstanceCommandRequest
                    {
                        CommandId = Guid.NewGuid(),
                        CommandType = ManagementHubCommands.NotificationAuthenticationRequired,
                        Value = "No authentication token provided"
                    });

                    if (_abortConnectionsWhenNotAuthenticated)
                    {
                        Context.Abort();
                    }

                    return;
                }
            }
            catch (Exception exp)
            {
                // could not validate jwt
                _logger?.LogWarning(exp, "InstanceManagementHub: Failed to read auth token. Connection attempt aborted.");

                await Clients.Caller.SendCommandRequest(new InstanceCommandRequest
                {
                    CommandId = Guid.NewGuid(),
                    CommandType = ManagementHubCommands.NotificationAuthenticationRequired,
                    Value = "No authentication token provided"
                });

                if (_abortConnectionsWhenNotAuthenticated)
                {
                    Context.Abort();
                }

                return;
            }

            if (!isAuthenticated)
            {
                _logger?.LogWarning("InstanceManagementHub: Instance connection not authenticated. Instance commanded to re-authenticate. Connection attempt aborted.");

                await Clients.Caller.SendCommandRequest(new InstanceCommandRequest
                {
                    CommandId = Guid.NewGuid(),
                    CommandType = ManagementHubCommands.NotificationAuthenticationRequired,
                    Value = "No authentication token provided"
                });

                if (_abortConnectionsWhenNotAuthenticated)
                {
                    Context.Abort();
                }

                return;
            }

            // begin tracking connection 
            if (!string.IsNullOrEmpty(hubAssignedId))
            {
                _logger?.LogInformation("InstanceManagementHub: Instance connected to management hub. Assigned Hub ID: {hubId}", hubAssignedId);
                _stateProvider.UpdateInstanceConnectionInfo(Context.ConnectionId, new ManagedInstanceInfo
                {
                    Id = hubAssignedId ?? String.Empty,
                    InstanceId = hubAssignedId ?? String.Empty,
                    ConnectionStatus = ConnectionStatus.Connected,
                    DateLastReported = DateTimeOffset.UtcNow,
                    IsAuthenticated = isAuthenticated
                }
           );

                // at this stage we don't know which instance id this is, we need to issue a command for it to identify itself before it can participate
                IssueCommandViaSignalR(new InstanceCommandRequest(ManagementHubCommands.GetInstanceInfo));
            }
            else
            {
                _logger?.LogWarning("InstanceManagementHub: Instance connected to management hub with no Hub ID assigned.");
            }
        }

        private void IssueCommandViaSignalR(InstanceCommandRequest cmd)
        {
            _stateProvider.AddAwaitedCommandRequest(cmd);

            Clients.Caller.SendCommandRequest(cmd);
        }

        /// <summary>
        /// Issue command directly to the local instance
        /// </summary>
        private async Task IssueCommandDirectly(InstanceCommandRequest cmd)
        {
            if (!_hasLocalInstance)
            {
                _logger?.LogWarning("Attempted direct command but local instance not available");
                return;
            }

            _stateProvider.AddAwaitedCommandRequest(cmd);

            var result = await _certifyManager!.PerformHubCommandWithResult(cmd);
            if (result != null)
            {
                result.CommandType = cmd.CommandType;
                result.CommandId = cmd.CommandId;
                result.InstanceId = _stateProvider.GetInstanceIdForConnection(_localInstanceId);

                await ReceiveCommandResult(result);
            }
            else
            {
                _logger?.LogWarning("Attempted direct command but result was null {cmdType}", cmd.CommandType);
                _stateProvider.RemoveAwaitedCommandRequest(cmd.CommandId);
            }
        }

        private async Task IssueInstanceCommand(string instanceId, string commandType)
        {
            await IssueInstanceCommand(instanceId, new InstanceCommandRequest(commandType));
        }

        private async Task IssueInstanceCommand(string instanceId, InstanceCommandRequest cmd)
        {
            if (_hasLocalInstance && instanceId == _localInstanceId)
            {
                await IssueCommandDirectly(cmd);
            }
            else
            {
                // send command to instance via SignalR on the current caller context
                IssueCommandViaSignalR(cmd);
            }
        }

        /// <summary>
        /// Handle SignalR disconnection event
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            var instanceId = _stateProvider.GetInstanceIdForConnection(Context.ConnectionId);

            if (instanceId != null)
            {
                _stateProvider.UpdateInstanceConnectionStatus(instanceId, ConnectionStatus.Disconnected);

                if (exception != null)
                {
                    _logger?.LogError("InstanceManagementHub: Instance {instanceId} disconnected unexpectedly from instance management hub. {exp}", instanceId, exception);
                }
                else
                {
                    _logger?.LogInformation("InstanceManagementHub: Instance {instanceId} disconnected from instance management hub, with no error.", instanceId);
                }
            }

            return base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Receive results from a previously issued command
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public async Task ReceiveCommandResult(InstanceCommandResult result)
        {
            var instanceId = _stateProvider.GetInstanceIdForConnection(Context?.ConnectionId ?? _localInstanceId);
            result.Received = DateTimeOffset.Now;

            // check we are awaiting this result
            var cmd = _stateProvider.GetAwaitedCommandRequest(result.CommandId);

            _logger?.LogDebug("[InstanceManagementHub.ReceiveCommandResult] Received instance command result {result} {instance}", result.CommandType, instanceId);

            if (cmd == null && !result.IsCommandResponse)
            {
                // message was not requested and has been sent by the instance (e.g. heartbeat)
                cmd = new InstanceCommandRequest { CommandId = result.CommandId, CommandType = result.CommandType };
            }

            if (cmd != null)
            {

                // for all other command results we need to resolve which instance id we are communicating with

                result.InstanceId = instanceId;

                if (!string.IsNullOrWhiteSpace(instanceId))
                {
                    await ProcessInstanceCommandResult(result, cmd, instanceId);
                }
                else
                {
                    _logger?.LogError("Received instance command result for an unknown instance {result}", result.CommandType);
                }
            }
            else
            {
                _logger?.LogError("Received instance command result for an unknown command {cmdId} {result}", result.CommandId, result.CommandType);
            }
        }

        /// <summary>
        /// Processes the result of a command sent to an instance, handling various command types accordingly.
        /// </summary>
        /// <param name="result">Contains the outcome of the command executed on the instance.</param>
        /// <param name="cmd">Represents the command that was sent to the instance.</param>
        /// <param name="instanceId">Identifies the specific instance being processed.</param>
        private async Task ProcessInstanceCommandResult(InstanceCommandResult result, InstanceCommandRequest cmd, string instanceId)
        {
            // action this message from this instance
            _logger?.LogDebug("[ProcessInstanceCommandResult] Received instance command result {instanceId} {cmdType}", instanceId, cmd.CommandType);

            if (!cmd.IsResultAwaited && cmd.CommandType == ManagementHubCommands.GetManagedItems && result.Value != null)
            {
                // remove awaited command now it's been handled

                _stateProvider.RemoveAwaitedCommandRequest(cmd.CommandId);

                // got items from an instance
                var val = System.Text.Json.JsonSerializer.Deserialize<ManagedInstanceItems>(result.Value, JsonOptions.DefaultJsonSerializerOptions);

                _stateProvider.UpdateInstanceItemInfo(instanceId, val!.Items);
            }
            else if (!cmd.IsResultAwaited && cmd.CommandType == ManagementHubCommands.GetStatusSummary && result.Value != null)
            {
                // remove awaited command now it's been handled
                _stateProvider.RemoveAwaitedCommandRequest(cmd.CommandId);

                // got status summary
                var val = System.Text.Json.JsonSerializer.Deserialize<StatusSummary>(result.Value, JsonOptions.DefaultJsonSerializerOptions);

                _stateProvider.UpdateInstanceStatusSummary(instanceId, val!);
            }
            else if (result.IsCommandResponse)
            {
                _stateProvider.AddAwaitedCommandResult(result);
            }
            else if (result.CommandType == ManagementHubCommands.GetInstanceInfo)
            {
                await ProcessInstanceInfoResult(result);
            }
            else
            {
                // item was not requested, queue for processing
                if (result.CommandType == ManagementHubCommands.NotificationUpdatedManagedItem && result.Value != null)
                {
                    var updatedManagedCertificate = System.Text.Json.JsonSerializer.Deserialize<ManagedCertificate>(result.Value, JsonOptions.DefaultJsonSerializerOptions);
                    if (updatedManagedCertificate != null)
                    {
                        var previousManagedCertificate = GetCachedManagedCertificate(instanceId, updatedManagedCertificate.Id);

                        await _uiStatusHub.Clients.All.SendAsync(StatusHubMessages.SendManagedCertificateUpdateMsg, updatedManagedCertificate);

                        _stateProvider.UpdateCachedManagedInstanceItem(instanceId, updatedManagedCertificate);

                        if (HasManagedCertificateVersionChanged(previousManagedCertificate, updatedManagedCertificate))
                        {
                            await NotifyExternalSubscribersOfManagedItemUpdate(instanceId, updatedManagedCertificate);
                        }
                    }
                }
                else if (result.CommandType == ManagementHubCommands.NotificationManagedItemRequestProgress && result.Value != null)
                {
                    var progressState = System.Text.Json.JsonSerializer.Deserialize<RequestProgressState>(result.Value, JsonOptions.DefaultJsonSerializerOptions);
                    if (progressState?.ManagedCertificate != null)
                    {
                        progressState.ManagedCertificate.InstanceId = instanceId;
                    }

                    await _uiStatusHub.Clients.All.SendAsync(StatusHubMessages.SendProgressStateMsg, progressState);
                }
                else if (result.CommandType == ManagementHubCommands.NotificationRemovedManagedItem && result.Value != null)
                {

                    string managedItemId;
                    try
                    {
                        // normalize the id by deserializing it, in case it was serialized as a string with quotes etc
                        managedItemId = System.Text.Json.JsonSerializer.Deserialize<string>(result.Value, JsonOptions.DefaultJsonSerializerOptions)
                            ?? result.Value;
                    }
                    catch
                    {
                        managedItemId = result.Value.Trim().Trim('"');
                    }

                    await _uiStatusHub.Clients.All.SendAsync(
                        StatusHubMessages.SendMsg,
                        ManagementHubCommands.NotificationRemovedManagedItem,
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            InstanceId = instanceId,
                            ManagedItemId = managedItemId,
                            Action = "deleted"
                        }));

                    _stateProvider.DeleteCachedManagedInstanceItem(instanceId, managedItemId);
                }
                else if (result.CommandType == ManagementHubCommands.NotificationRequestExternalManagedCertificateUpdate && result.Value != null)
                {
                    await HandleExternalManagedCertificateRequest(instanceId, result.Value);
                }
            }
        }

        private async Task HandleExternalManagedCertificateRequest(string requestingInstanceId, string serializedRequest)
        {
            var request = System.Text.Json.JsonSerializer.Deserialize<ExternalManagedCertificateRequest>(serializedRequest, JsonOptions.DefaultJsonSerializerOptions);

            if (request == null || string.IsNullOrWhiteSpace(request.TargetManagedCertificateId))
            {
                _logger?.LogWarning("Ignored external managed certificate request from {instanceId}: missing target managed certificate id.", requestingInstanceId);
                return;
            }

            var payload = new ExternalManagedCertificateUpdate
            {
                ManagedCertificateId = request.TargetManagedCertificateId,
                SourceVersion = null
            };

            var command = new InstanceCommandRequest(ManagementHubCommands.PushExternalManagedCertificateUpdate)
            {
                Value = System.Text.Json.JsonSerializer.Serialize(payload)
            };

            await SendCommandToInstance(requestingInstanceId, command);

            _logger?.LogInformation(
                "External managed certificate push requested by {instanceId} for target {targetManagedCertificateId} and source {sourceInstanceId}/{sourceManagedCertificateId}.",
                requestingInstanceId,
                request.TargetManagedCertificateId,
                request.SourceInstanceId,
                request.SourceManagedCertificateId);
        }

        private async Task NotifyExternalSubscribersOfManagedItemUpdate(string sourceInstanceId, ManagedCertificate updatedManagedCertificate)
        {
            if (string.IsNullOrWhiteSpace(sourceInstanceId) || string.IsNullOrWhiteSpace(updatedManagedCertificate.Id))
            {
                return;
            }

            var sourceVersion = updatedManagedCertificate.DateRenewed?.UtcDateTime.Ticks.ToString();
            var managedItemsByInstance = _stateProvider.GetManagedInstanceItems();
            var targets = GetExternalPushSubscriptionTargets(sourceInstanceId, updatedManagedCertificate, managedItemsByInstance);

            if (targets.Count == 0)
            {
                return;
            }

            foreach (var target in targets)
            {
                var payload = new ExternalManagedCertificateUpdate
                {
                    ManagedCertificateId = target.TargetManagedCertificateId,
                    SourceVersion = sourceVersion
                };

                var command = new InstanceCommandRequest(ManagementHubCommands.PushExternalManagedCertificateUpdate)
                {
                    Value = System.Text.Json.JsonSerializer.Serialize(payload)
                };

                try
                {
                    await SendCommandToInstance(target.TargetInstanceId, command);
                    _logger?.LogInformation("Queued external certificate push update for target {targetInstanceId} item {targetItemId} from source {sourceInstanceId}/{sourceItemId}.", target.TargetInstanceId, target.TargetManagedCertificateId, sourceInstanceId, updatedManagedCertificate.Id);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to queue external certificate push update for target {targetInstanceId} item {targetItemId} from source {sourceInstanceId}/{sourceItemId}.", target.TargetInstanceId, target.TargetManagedCertificateId, sourceInstanceId, updatedManagedCertificate.Id);
                }
            }
        }

        internal static bool HasManagedCertificateVersionChanged(ManagedCertificate? previousManagedCertificate, ManagedCertificate updatedManagedCertificate)
        {
            if (updatedManagedCertificate == null || string.IsNullOrWhiteSpace(updatedManagedCertificate.Id))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(updatedManagedCertificate.CertificateThumbprintHash))
            {
                return !string.Equals(previousManagedCertificate?.CertificateThumbprintHash, updatedManagedCertificate.CertificateThumbprintHash, StringComparison.OrdinalIgnoreCase);
            }

            if (updatedManagedCertificate.DateRenewed.HasValue)
            {
                return previousManagedCertificate?.DateRenewed != updatedManagedCertificate.DateRenewed;
            }

            return false;
        }

        private ManagedCertificate? GetCachedManagedCertificate(string instanceId, string? managedCertificateId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(managedCertificateId))
            {
                return null;
            }

            if (_stateProvider.GetManagedInstanceItems().TryGetValue(instanceId, out var instanceItems))
            {
                return instanceItems.Items?.FirstOrDefault(i => string.Equals(i.Id, managedCertificateId, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        internal static List<(string TargetInstanceId, string TargetManagedCertificateId)> GetExternalPushSubscriptionTargets(
            string sourceInstanceId,
            ManagedCertificate updatedManagedCertificate,
            IEnumerable<KeyValuePair<string, ManagedInstanceItems>> managedItemsByInstance)
        {
            var targets = new List<(string TargetInstanceId, string TargetManagedCertificateId)>();

            if (string.IsNullOrWhiteSpace(sourceInstanceId)
                || string.IsNullOrWhiteSpace(updatedManagedCertificate.Id)
                || managedItemsByInstance == null)
            {
                return targets;
            }

            foreach (var instanceItems in managedItemsByInstance)
            {
                var targetInstanceId = instanceItems.Key;
                var items = instanceItems.Value?.Items;

                if (items == null || items.Count == 0)
                {
                    continue;
                }

                foreach (var item in items)
                {
                    if (!IsPushSubscriberForSource(item, sourceInstanceId, updatedManagedCertificate.Id))
                    {
                        continue;
                    }

                    if (targetInstanceId == sourceInstanceId && item.Id == updatedManagedCertificate.Id)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(item.Id))
                    {
                        targets.Add((targetInstanceId, item.Id));
                    }
                }
            }

            return targets;
        }

        private async Task SendCommandToInstance(string instanceId, InstanceCommandRequest command)
        {
            if (_hasLocalInstance && instanceId == _localInstanceId)
            {
                await _certifyManager!.PerformHubCommandWithResult(command);
                return;
            }

            var connectionId = _stateProvider.GetConnectionIdForInstance(instanceId);
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                throw new InvalidOperationException($"Cannot send command to instance '{instanceId}' because no current connection exists.");
            }

            await Clients.Client(connectionId).SendCommandRequest(command);
        }

        private static bool IsPushSubscriberForSource(ManagedCertificate managedCertificate, string sourceInstanceId, string sourceManagedCertificateId)
        {
            var source = managedCertificate.ExternalSource;
            if (source == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(source.ExternalReference))
            {
                return false;
            }

            if (!string.Equals(source.SourceType, ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!IsPushMode(source.RetrievalMode))
            {
                return false;
            }

            return ManagedCertificate.TryParseManagementHubReference(source.ExternalReference, out var referencedInstanceId, out var referencedManagedCertificateId)
                && string.Equals(referencedInstanceId, sourceInstanceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(referencedManagedCertificateId, sourceManagedCertificateId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPushMode(string? retrievalMode)
        {
            return string.Equals(retrievalMode, ExternalCertificateRetrievalModes.Push, StringComparison.OrdinalIgnoreCase)
                || string.Equals(retrievalMode, ExternalCertificateRetrievalModes.Auto, StringComparison.OrdinalIgnoreCase);
        }

        private async Task ProcessInstanceInfoResult(InstanceCommandResult result)
        {
            var instanceInfo = result.Value == null ? null : System.Text.Json.JsonSerializer.Deserialize<ManagedInstanceInfo>(result.Value, JsonOptions.DefaultJsonSerializerOptions);

            if (instanceInfo != null)
            {
                instanceInfo.DateLastReported = DateTimeOffset.Now;
                instanceInfo.IsPendingConnection = false;

                // update our stored instance info for this instance while preserving persistent metadata fields
                var storedInstance = await _backendClient?.GetHubManagedInstance(instanceInfo.InstanceId, null);

                if (storedInstance != null)
                {
                    // preserve custom title and security principal id from our store if not provided by the instance heartbeat etc
                    instanceInfo.CustomTitle = storedInstance.CustomTitle;
                    instanceInfo.SecurityPrincipalId = storedInstance.SecurityPrincipalId;
                    instanceInfo.DateRegistered = storedInstance.DateRegistered;
                    instanceInfo.Description = storedInstance.Description;
                    instanceInfo.IsPendingConnection = false;
                }

                // update our cached instance info
                _stateProvider.UpdateInstanceConnectionInfo(Context?.ConnectionId ?? _localInstanceId, instanceInfo);

                _logger?.LogDebug("Received instance {instanceId} {instanceTitle} for mgmt hub connection.", instanceInfo.InstanceId, instanceInfo.Title);

                if (storedInstance != null)
                {
                    // update stored instance with any new info from the instance, but preserve existing metadata fields
                    storedInstance.OS = instanceInfo.OS;
                    storedInstance.OSVersion = instanceInfo.OSVersion;
                    storedInstance.ClientName = instanceInfo.ClientName;
                    storedInstance.ClientVersion = instanceInfo.ClientVersion;
                    storedInstance.Title = instanceInfo.Title;
                    storedInstance.DateLastReported = instanceInfo.DateLastReported;
                    storedInstance.License = instanceInfo.License;

                    if (!string.IsNullOrWhiteSpace(instanceInfo.InternalInstanceId))
                    {
                        storedInstance.InternalInstanceId = instanceInfo.InternalInstanceId;
                    }

                    storedInstance.IsPendingConnection = false;

                    await _backendClient?.UpdateHubManagedInstance(storedInstance, null);
                }
                else
                {
                    await _backendClient?.AddHubManagedInstance(instanceInfo, null);
                }

                // if we don't yet have any managed items for this instance, ask for them
                if (!_stateProvider.HasItemsForManagedInstance(instanceInfo.InstanceId))
                {
                    await IssueInstanceCommand(instanceInfo.InstanceId, ManagementHubCommands.GetManagedItems);
                }

                // if we don't have a status summary, ask for that
                if (!_stateProvider.HasStatusSummaryForManagedInstance(instanceInfo.InstanceId))
                {
                    await IssueInstanceCommand(instanceInfo.InstanceId, ManagementHubCommands.GetStatusSummary);
                }
            }
        }

        /// <summary>
        /// Receives a message from an instance and logs the message details.
        /// </summary>
        /// <param name="message">The message received from the instance.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public Task ReceiveInstanceMessage(InstanceMessage message)
        {
            var instanceId = _stateProvider.GetInstanceIdForConnection(Context?.ConnectionId ?? _localInstanceId);
            if (instanceId != null)
            {
                // action this message from this instance
                _logger?.LogDebug("Received instance message {msg}", message);
            }
            else
            {
                _logger?.LogError("[ReceiveInstanceMessage] Received Instance Message result for an unknown instance {msgType}", message.MessageType);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends a command request either via SignalR or directly to a local instance based on the connection context.
        /// </summary>
        /// <param name="cmd">Contains the details of the command to be executed.</param>
        /// <returns>This method does not return a value.</returns>
        public async Task SendCommandRequest(InstanceCommandRequest cmd)
        {
            // If called in SignalR context, send to caller
            if (Context?.ConnectionId != null)
            {
                IssueCommandViaSignalR(cmd);
            }
            // Otherwise attempt direct communication with local instance
            else if (_hasLocalInstance)
            {
                await IssueCommandDirectly(cmd);
            }
            else
            {
                _logger?.LogError("SendCommandRequest: No connection context and no local instance available");
            }
        }
    }
}
