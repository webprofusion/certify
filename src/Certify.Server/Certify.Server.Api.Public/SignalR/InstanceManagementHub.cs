using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Certify.API.Management;
using Certify.Models;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Api.Public.SignalR
{
    public interface IInstanceManagementStateProvider
    {
        public void UpdateInstanceConnectionInfo(string connectionId, ManagedInstanceInfo instanceInfo);
        public string GetConnectionIdForInstance(string instanceId);
        public string GetInstanceIdForConnection(string connectionId);
        public List<ManagedInstanceInfo> GetConnectedInstances();
        public void AddAwaitedCommandRequest(InstanceCommandRequest command);
        public void RemoveAwaitedCommandRequest(Guid commandId);
        public InstanceCommandRequest? GetAwaitedCommandRequest(Guid commandId);
        public void UpdateInstanceItemInfo(string instanceId, List<ManagedCertificate> items);
        public ConcurrentDictionary<string, ManagedInstanceItems> GetManagedInstanceItems(string instanceId = null);
    }

    /// <summary>
    /// Track state across pool of instance connections to management hub
    /// </summary>
    public class InstanceManagementStateProvider : IInstanceManagementStateProvider
    {
        private ConcurrentDictionary<string, ManagedInstanceInfo> _instanceConnections = [];
        private ConcurrentDictionary<Guid, InstanceCommandRequest> _awaitedCommandResults = [];

        private ConcurrentDictionary<string, ManagedInstanceItems> _managedInstanceItems = [];
        private ILogger<InstanceManagementStateProvider> _logger;
        public InstanceManagementStateProvider(ILogger<InstanceManagementStateProvider> logger)
        {
            _logger = logger;
        }

        public List<ManagedInstanceInfo> GetConnectedInstances()
        {
            return _instanceConnections.Values.ToList();
        }
        /// <summary>
        /// Track the instance info associated with a hub connection
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="instanceInfo"></param>
        public void UpdateInstanceConnectionInfo(string connectionId, ManagedInstanceInfo instanceInfo)
        {
            var existingOther = _instanceConnections.FirstOrDefault(a => a.Value.InstanceId == instanceInfo.InstanceId && a.Key != connectionId);

            if (existingOther.Value != null)
            {
                _logger.LogWarning("[InstanceManagementStateProvider] Connection ID for instance {instance} changed to {connection}", instanceInfo.Title, connectionId);
                _instanceConnections.Remove(existingOther.Key, out _);
            }

            _instanceConnections.AddOrUpdate(connectionId, instanceInfo, (i, oldValue) => { return instanceInfo; });
        }

        /// <summary>
        /// Get the current connection ID we haev associated with the given instance id
        /// </summary>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public string GetConnectionIdForInstance(string instanceId)
        {
            // TODO: of instances use the same instanceid accidentally they will clobber each other
            var info = _instanceConnections.FirstOrDefault(k => k.Value.InstanceId == instanceId);

            return info.Key;
        }

        public string? GetInstanceIdForConnection(string connectionId)
        {
            _instanceConnections.TryGetValue(connectionId, out var managedInstanceInfo);

            if (managedInstanceInfo != null)
            {
                return managedInstanceInfo.InstanceId;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Track command requests we are waiting on responses for.
        /// </summary>
        /// <param name="command"></param>
        public void AddAwaitedCommandRequest(InstanceCommandRequest command)
        {
            _awaitedCommandResults.AddOrUpdate(command.CommandId, command, (i, oldValue) => { return command; });
        }

        /// <summary>
        /// Get command request we are waiting on a response for
        /// </summary>
        /// <param name="commandId"></param>
        /// <returns></returns>
        public InstanceCommandRequest? GetAwaitedCommandRequest(Guid commandId)
        {
            _awaitedCommandResults.TryGetValue(commandId, out var cmd);
            return cmd;
        }

        /// <summary>
        /// Remove a command request we have received a response for
        /// </summary>
        /// <param name="commandId"></param>
        public void RemoveAwaitedCommandRequest(Guid commandId)
        {
            _awaitedCommandResults.Remove(commandId, out _);
        }

        public void UpdateInstanceItemInfo(string instanceId, List<ManagedCertificate> items)
        {
            var info = new ManagedInstanceItems { InstanceId = instanceId, Items = items };
            _managedInstanceItems.AddOrUpdate(instanceId, info, (k, old) => info);
        }

        public ConcurrentDictionary<string, ManagedInstanceItems> GetManagedInstanceItems(string instanceId = null)
        {
            return _managedInstanceItems;
        }
    }

    /// <summary>
    /// Interface for instance management hub events
    /// </summary>
    public interface IInstanceManagementHub
    {
        /// <summary>
        /// Send command to an instance
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task SendCommandRequest(InstanceCommandRequest request);

        /// <summary>
        /// Receive command result from an instance
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        Task ReceiveCommandResult(InstanceCommandResult result);

        /// <summary>
        /// Receive adhoc message from an instance
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        Task ReceiveInstanceMessage(InstanceMessage message);
    }

    /// <summary>
    /// Individual backend/agent instances connect as clients to this hub to send back managed item updates, progress reports, config settings. 
    /// Instances receive commands (managed item updates etc, config updates)
    /// </summary>
    public class InstanceManagementHub : Hub<IInstanceManagementHub>, IInstanceManagementHub
    {
        private IInstanceManagementStateProvider _stateProvider;
        private ILogger<InstanceManagementHub> _logger;

        /// <summary>
        /// Set up instance management hub
        /// </summary>
        /// <param name="stateProvider"></param>
        /// <param name="logger"></param>
        public InstanceManagementHub(IInstanceManagementStateProvider stateProvider, ILogger<InstanceManagementHub> logger)
        {
            _stateProvider = stateProvider;
            _logger = logger;
        }

        /// <summary>
        /// Handle connection event
        /// </summary>
        /// <returns></returns>
        public override Task OnConnectedAsync()
        {
            _logger?.LogInformation("InstanceManagementHub: Instance connected to instance management hub..");

            // begin tracking connection 
            _stateProvider.UpdateInstanceConnectionInfo(Context.ConnectionId, new ManagedInstanceInfo { InstanceId = string.Empty, LastReported = DateTimeOffset.Now });

            // issue command for instance to identify itself
            var request = new InstanceCommandRequest
            {
                CommandId = Guid.NewGuid(),
                CommandType = ManagementHubCommands.GetInstanceInfo
            };

            _stateProvider.AddAwaitedCommandRequest(request);

            Clients.Caller.SendCommandRequest(request);

            return base.OnConnectedAsync();
        }

        /// <summary>
        /// Handle disconnection event
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                _logger?.LogError("InstanceManagementHub: Instance disconnected unexpectedly from instance management hub. {exp}", exception);
            }
            else
            {
                _logger?.LogInformation("InstanceManagementHub: Instance disconnected from instance management hub..");
            }

            return base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Receive results from a previously issued command
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public Task ReceiveCommandResult(InstanceCommandResult result)
        {

            // check we are awaiting this result
            var cmd = _stateProvider.GetAwaitedCommandRequest(result.CommandId);

            if (cmd != null)
            {
                _stateProvider.RemoveAwaitedCommandRequest(cmd.CommandId);

                if (cmd.CommandType == ManagementHubCommands.GetInstanceInfo)
                {
                    var instanceInfo = System.Text.Json.JsonSerializer.Deserialize<ManagedInstanceInfo>(result.Value);

                    if (instanceInfo != null)
                    {

                        instanceInfo.LastReported = DateTimeOffset.Now;
                        _stateProvider.UpdateInstanceConnectionInfo(Context.ConnectionId, instanceInfo);

                        _logger?.LogInformation("Received instance {instanceId} {instanceTitle} for mgmt hub connection.", instanceInfo.InstanceId, instanceInfo.Title);
                    }
                }
                else
                {
                    // for all other command result we need to resolve which instance id we are communicating with
                    var instanceId = _stateProvider.GetInstanceIdForConnection(Context.ConnectionId);
                    if (instanceId != null)
                    {
                        // action this message from this instance
                        _logger?.LogInformation("Received instance command result {result}", result);

                        if (cmd.CommandType == ManagementHubCommands.GetInstanceItems)
                        {
                            // got items from an instance
                            var itemInfo = System.Text.Json.JsonSerializer.Deserialize<ManagedInstanceItems>(result.Value);

                            _stateProvider.UpdateInstanceItemInfo(instanceId, itemInfo.Items);
                        }
                    }
                    else
                    {
                        _logger?.LogError("Received instance command result for an unknown instance {result}", result);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task ReceiveInstanceMessage(InstanceMessage message)
        {

            var instanceId = _stateProvider.GetInstanceIdForConnection(Context.ConnectionId);
            if (instanceId != null)
            {
                // action this message from this instance
                _logger?.LogInformation("Received instance message {msg}", message);
            }

            return Task.CompletedTask;
        }
        public Task SendCommandRequest(InstanceCommandRequest request) => throw new NotImplementedException();

        public Task SendCommandToSpecificInstance(string instanceId, InstanceCommandRequest request)
        {
            // find connection id to send to for this instance

            var targetConnectionId = _stateProvider.GetConnectionIdForInstance(instanceId);
            if (targetConnectionId != null)
            {
                _stateProvider.AddAwaitedCommandRequest(request);

                return Clients.Client(targetConnectionId).SendCommandRequest(request);
            }
            else
            {
                _logger?.LogError("SendCommand could not map to target instance {instanceId} {req} ", instanceId, request);
                return Task.FromException(new Exception("SendCommand could not map to target instance"));
            }
        }
    }
}
