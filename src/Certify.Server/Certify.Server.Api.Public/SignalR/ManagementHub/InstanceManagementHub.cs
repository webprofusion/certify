using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Certify.API.Management;
using Certify.Models;
using Certify.Models.Reporting;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Api.Public.SignalR.ManagementHub
{
    /// <summary>
    /// Interface for instance management hub events
    /// </summary>
    public interface IInstanceManagementHub
    {
        /// <summary>
        /// Send command to an instance or the current caller if instance not provided
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task SendCommandRequest(InstanceCommandRequest cmd);

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

            IssueCommand(request);

            return base.OnConnectedAsync();
        }

        private void IssueCommand(InstanceCommandRequest cmd)
        {
            _stateProvider.AddAwaitedCommandRequest(cmd);

            Clients.Caller.SendCommandRequest(cmd);
        }

        /// <summary>
        /// Handle disconnection event
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            var instanceId = _stateProvider.GetInstanceIdForConnection(Context.ConnectionId);
            if (exception != null)
            {
                _logger?.LogError("InstanceManagementHub: Instance {instanceId} disconnected unexpectedly from instance management hub. {exp}", instanceId, exception);
            }
            else
            {
                _logger?.LogInformation("InstanceManagementHub: Instance {instanceId} disconnected from instance management hub, with no error.", instanceId);
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

            result.Received = DateTimeOffset.Now;

            // check we are awaiting this result
            var cmd = _stateProvider.GetAwaitedCommandRequest(result.CommandId);

            if (cmd == null && !result.IsCommandResponse)
            {
                // message was not requested and has been sent by the instance (e.g. heartbeat)
                cmd = new InstanceCommandRequest { CommandId = result.CommandId, CommandType = result.CommandType };
            }

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

                        // if we don't yet have any managed items for this instance, ask for them
                        if (!_stateProvider.HasItemsForManagedInstance(instanceInfo.InstanceId))
                        {
                            var request = new InstanceCommandRequest
                            {
                                CommandId = Guid.NewGuid(),
                                CommandType = ManagementHubCommands.GetManagedItems
                            };

                            IssueCommand(request);
                        }

                        // if we dont have a status summary, ask for that
                        if (!_stateProvider.HasStatusSummaryForManagedInstance(instanceInfo.InstanceId))
                        {
                            var request = new InstanceCommandRequest
                            {
                                CommandId = Guid.NewGuid(),
                                CommandType = ManagementHubCommands.GetStatusSummary
                            };

                            IssueCommand(request);
                        }
                    }
                }
                else
                {
                    // for all other command results we need to resolve which instance id we are communicating with
                    var instanceId = _stateProvider.GetInstanceIdForConnection(Context.ConnectionId);
                    result.InstanceId = instanceId;

                    if (!string.IsNullOrWhiteSpace(instanceId))
                    {
                        // action this message from this instance
                        _logger?.LogInformation("Received instance command result {result}", result);

                        if (cmd.CommandType == ManagementHubCommands.GetManagedItems)
                        {
                            // got items from an instance
                            var val = System.Text.Json.JsonSerializer.Deserialize<ManagedInstanceItems>(result.Value);

                            _stateProvider.UpdateInstanceItemInfo(instanceId, val.Items);
                        }
                        else if (cmd.CommandType == ManagementHubCommands.GetStatusSummary && result?.Value!=null)
                        {
                            // got status summary
                            var val = System.Text.Json.JsonSerializer.Deserialize<StatusSummary>(result.Value);

                            _stateProvider.UpdateInstanceStatusSummary(instanceId, val);
                        }
                        else
                        {
                            // store for something else to consume
                            _stateProvider.AddAwaitedCommandResult(result);
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
            else
            {
                _logger?.LogError("Received instance command result for an unknown instance {msg}", message);
            }

            return Task.CompletedTask;
        }

        public Task SendCommandRequest(InstanceCommandRequest cmd)
        {
            IssueCommand(cmd);

            return Task.CompletedTask;
        }
    }
}
