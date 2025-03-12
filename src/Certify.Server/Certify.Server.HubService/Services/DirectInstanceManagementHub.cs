using Certify.Management;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.HubService.Services
{

    /// <summary>
    /// Instance mgmt hub which talks directly to the in process backend certify core service, skipping HTTP API layer and signalr
    /// </summary>
    public class DirectInstanceManagementHub : IInstanceManagementHub
    {
        private IInstanceManagementStateProvider _stateProvider;
        private ILogger<DirectInstanceManagementHub> _logger;
        private ICertifyManager _certifyManager;
        private IHubContext<UserInterfaceStatusHub> _uiStatusHub;
        private ManagementAPI _mgmtAPI;
        public DirectInstanceManagementHub(ILogger<DirectInstanceManagementHub> logger, IInstanceManagementStateProvider stateProvider, ICertifyManager certifyManager, IHubContext<UserInterfaceStatusHub> uiStatusHub, Server.Hub.Api.Services.ManagementAPI mgmtAPI)
        {
            _stateProvider = stateProvider;
            _logger = logger;
            _certifyManager = certifyManager;
            _uiStatusHub = uiStatusHub;
            _mgmtAPI = mgmtAPI;

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

                if (cmd.CommandType == ManagementHubCommands.GetInstanceInfo && result.Value != null)
                {
                    var instanceInfo = System.Text.Json.JsonSerializer.Deserialize<ManagedInstanceInfo>(result.Value);

                    if (instanceInfo != null)
                    {

                        instanceInfo.LastReported = DateTimeOffset.UtcNow;
                        _stateProvider.UpdateInstanceConnectionInfo("internal", instanceInfo);

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
                    var instanceId = _stateProvider.GetInstanceIdForConnection("internal");
                    result.InstanceId = instanceId;

                    if (!string.IsNullOrWhiteSpace(instanceId))
                    {
                        // action this message from this instance
                        _logger?.LogInformation("Received instance command result {result}", result.CommandType);

                        if (cmd.CommandType == ManagementHubCommands.GetManagedItems && result.Value != null)
                        {
                            // got items from an instance
                            var val = System.Text.Json.JsonSerializer.Deserialize<ManagedInstanceItems>(result.Value);

                            _stateProvider.UpdateInstanceItemInfo(instanceId, val!.Items);
                        }
                        else if (cmd.CommandType == ManagementHubCommands.GetStatusSummary && result.Value != null)
                        {
                            // got status summary
                            var val = System.Text.Json.JsonSerializer.Deserialize<StatusSummary>(result.Value);

                            _stateProvider.UpdateInstanceStatusSummary(instanceId, val!);
                        }
                        else if (result != null)
                        {
                            // store for something else to consume
                            if (result.IsCommandResponse == true)
                            {
                                _stateProvider.AddAwaitedCommandResult(result);
                            }
                            else
                            {
                                // item was not requested, queue for processing
                                if (result.CommandType == ManagementHubCommands.NotificationUpdatedManagedItem)
                                {
                                    _uiStatusHub.Clients.All.SendAsync(Providers.StatusHubMessages.SendManagedCertificateUpdateMsg, System.Text.Json.JsonSerializer.Deserialize<Models.ManagedCertificate>(result.Value));
                                }
                                else if (result.CommandType == ManagementHubCommands.NotificationManagedItemRequestProgress)
                                {
                                    _uiStatusHub.Clients.All.SendAsync(Providers.StatusHubMessages.SendProgressStateMsg, System.Text.Json.JsonSerializer.Deserialize<Models.RequestProgressState>(result.Value));
                                }
                                else if (result.CommandType == ManagementHubCommands.NotificationRemovedManagedItem)
                                {
                                    // deleted :TODO
                                    _uiStatusHub.Clients.All.SendAsync(Providers.StatusHubMessages.SendMsg, $"Deleted item {result.Value}");
                                }
                            }
                        }
                    }
                    else
                    {
                        _logger?.LogError("Received instance command result for an unknown instance {result}", result.CommandType);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task ReceiveInstanceMessage(InstanceMessage message)
        {

            var instanceId = _stateProvider.GetInstanceIdForConnection("internal");
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

        private async void IssueCommand(InstanceCommandRequest cmd)
        {
            _stateProvider.AddAwaitedCommandRequest(cmd);

            //
            var result = await _certifyManager.PerformHubCommandWithResult(cmd);
            if (result.IsCommandResponse)
            {
                result.CommandType = cmd.CommandType;
                result.CommandId = cmd.CommandId;
                result.InstanceId = _stateProvider.GetManagementHubInstanceId();

                await ReceiveCommandResult(result);
            }
        }
    }
}
