using System.Security.Claims;
using Certify.Management;
using Certify.Models.Hub;
using Certify.Models.Reporting;
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
        private IConfiguration _config;
        private readonly string _localInstanceId = default!;
        private bool _hasLocalInstance => _certifyManager != null;

        /// <summary>
        /// Set up instance management hub
        /// </summary>
        /// <param name="stateProvider"></param>
        /// <param name="logger"></param>
        public InstanceManagementHub(IInstanceManagementStateProvider stateProvider, ILogger<InstanceManagementHub> logger, IHubContext<UserInterfaceStatusHub> uiStatusHub, IConfiguration config, ICertifyManager? certifyManager = null)
        {
            _stateProvider = stateProvider;
            _logger = logger;
            _uiStatusHub = uiStatusHub;
            _config = config;
            _certifyManager = certifyManager;

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
        /// Handle connection event from an instance using SignalR
        /// </summary>
        /// <returns></returns>
        public async override Task OnConnectedAsync()
        {
            _logger?.LogInformation("InstanceManagementHub: Remote instance connected to instance management hub..");

            // validate jwt passed by joining instance
            var isAuthenticated = false;

            try
            {
                var accessToken = Context.GetHttpContext().Request.Headers[key: "Authorization"];

                var joiningJwt = accessToken.ToString().Replace("Bearer ", "");
                var jwtService = new Hub.Api.Services.JwtService(_config);
                var claimsIdentity = await jwtService.ClaimsIdentityFromTokenAsync(joiningJwt, true);
                var userId = claimsIdentity.FindFirst(ClaimTypes.Sid)?.Value;
                isAuthenticated = true;

            }
            catch (Exception)
            {
                // could not validate jwt

                return;
            }

            // begin tracking connection 
            _stateProvider.UpdateInstanceConnectionInfo(Context.ConnectionId, new ManagedInstanceInfo
            {
                InstanceId = string.Empty,
                ConnectionStatus = ConnectionStatus.Connected,
                LastReported = DateTimeOffset.Now,
                IsAuthenticated = isAuthenticated
            }
            );

            // at this stage we don't know which instance id this is, we need to issue a command for it to identify itself before it can participate
            var request = new InstanceCommandRequest
            {
                CommandId = Guid.NewGuid(),
                CommandType = ManagementHubCommands.GetInstanceInfo
            };

            IssueCommandViaSignalR(request);
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
                //_stateProvider.RemoveAwaitedCommandRequest(cmd.CommandId);

                if (cmd.CommandType == ManagementHubCommands.GetInstanceInfo)
                {
                    await ProcessInstanceInfoResult(result);
                }
                else
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

            if (cmd.CommandType == ManagementHubCommands.GetManagedItems && result.Value != null)
            {
                // got items from an instance
                var val = System.Text.Json.JsonSerializer.Deserialize<ManagedInstanceItems>(result.Value, JsonOptions.DefaultJsonSerializerOptions);

                _stateProvider.UpdateInstanceItemInfo(instanceId, val!.Items);
            }
            else if (cmd.CommandType == ManagementHubCommands.GetStatusSummary && result.Value != null)
            {
                // got status summary
                var val = System.Text.Json.JsonSerializer.Deserialize<StatusSummary>(result.Value, JsonOptions.DefaultJsonSerializerOptions);

                _stateProvider.UpdateInstanceStatusSummary(instanceId, val!);
            }
            else if (result.IsCommandResponse)
            {
                _stateProvider.AddAwaitedCommandResult(result);
            }
            else
            {
                // item was not requested, queue for processing
                if (result.CommandType == ManagementHubCommands.NotificationUpdatedManagedItem && result.Value != null)
                {
                    await _uiStatusHub.Clients.All.SendAsync(Providers.StatusHubMessages.SendManagedCertificateUpdateMsg, System.Text.Json.JsonSerializer.Deserialize<Models.ManagedCertificate>(result.Value, JsonOptions.DefaultJsonSerializerOptions));
                }
                else if (result.CommandType == ManagementHubCommands.NotificationManagedItemRequestProgress && result.Value != null)
                {
                    await _uiStatusHub.Clients.All.SendAsync(Providers.StatusHubMessages.SendProgressStateMsg, System.Text.Json.JsonSerializer.Deserialize<Models.RequestProgressState>(result.Value, JsonOptions.DefaultJsonSerializerOptions));
                }
                else if (result.CommandType == ManagementHubCommands.NotificationRemovedManagedItem && result.Value != null)
                {
                    // deleted :TODO
                    await _uiStatusHub.Clients.All.SendAsync(Providers.StatusHubMessages.SendMsg, $"Deleted item {result.Value}");
                }
            }
        }

        private async Task ProcessInstanceInfoResult(InstanceCommandResult result)
        {
            var instanceInfo = result.Value == null ? null : System.Text.Json.JsonSerializer.Deserialize<ManagedInstanceInfo>(result.Value, JsonOptions.DefaultJsonSerializerOptions);

            if (instanceInfo != null)
            {
                instanceInfo.LastReported = DateTimeOffset.UtcNow;
                _stateProvider.UpdateInstanceConnectionInfo(Context?.ConnectionId ?? _localInstanceId, instanceInfo);

                _logger?.LogInformation("Received instance {instanceId} {instanceTitle} for mgmt hub connection.", instanceInfo.InstanceId, instanceInfo.Title);

                // if we don't yet have any managed items for this instance, ask for them
                if (!_stateProvider.HasItemsForManagedInstance(instanceInfo.InstanceId))
                {
                    var request = new InstanceCommandRequest
                    {
                        CommandId = Guid.NewGuid(),
                        CommandType = ManagementHubCommands.GetManagedItems
                    };

                    await IssueInstanceCommand(instanceInfo.InstanceId, request);
                }

                // if we dont have a status summary, ask for that
                if (!_stateProvider.HasStatusSummaryForManagedInstance(instanceInfo.InstanceId))
                {
                    var request = new InstanceCommandRequest
                    {
                        CommandId = Guid.NewGuid(),
                        CommandType = ManagementHubCommands.GetStatusSummary
                    };

                    await IssueInstanceCommand(instanceInfo.InstanceId, request);
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
                _logger?.LogInformation("Received instance message {msg}", message);
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
