using System.Collections.Concurrent;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;

namespace Certify.Server.Hub.Api.SignalR.ManagementHub
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public interface IInstanceManagementStateProvider
    {
        public void Clear();
        public void SetManagementHubInstanceId(string instanceId);
        public string GetManagementHubInstanceId();
        public void UpdateInstanceConnectionInfo(string? connectionId, ManagedInstanceInfo instanceInfo);
        public void UpdateInstanceStatusSummary(string instanceId, StatusSummary summary);
        public string GetConnectionIdForInstance(string instanceId);
        public string GetInstanceIdForConnection(string connectionId);
        public List<ManagedInstanceInfo> GetConnectedInstances();
        public void AddAwaitedCommandRequest(InstanceCommandRequest command);
        public void RemoveAwaitedCommandRequest(Guid commandId);
        public InstanceCommandRequest? GetAwaitedCommandRequest(Guid commandId);
        public void AddAwaitedCommandResult(InstanceCommandResult result);

        /// <summary>
        /// Wait for a command result to be available
        /// </summary>
        /// <param name="commandId"></param>
        /// <returns></returns>
        public Task<InstanceCommandResult?> ConsumeAwaitedCommandResult(InstanceCommandRequest cmd);
        public void UpdateInstanceItemInfo(string instanceId, List<ManagedCertificate> items);
        public ConcurrentDictionary<string, ManagedInstanceItems> GetManagedInstanceItems(string? instanceId = null);
        public void UpdateCachedManagedInstanceItem(string instanceId, ManagedCertificate managedCertificate);
        public void DeleteCachedManagedInstanceItem(string instanceId, string managedCertificateId);
        public bool HasItemsForManagedInstance(string instanceId);

        public bool HasStatusSummaryForManagedInstance(string instanceId);
        public ConcurrentDictionary<string, StatusSummary> GetManagedInstanceStatusSummaries();
        public void UpdateInstanceConnectionStatus(string instanceId, string status);
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

    /// <summary>
    /// Track state across pool of instance connections to the management hub
    /// </summary>
    public class InstanceManagementStateProvider : IInstanceManagementStateProvider
    {
        private ConcurrentDictionary<string, ManagedInstanceInfo> _instanceConnections = [];
        private ConcurrentDictionary<Guid, InstanceCommandRequest> _awaitedCommandRequests = [];
        private ConcurrentDictionary<Guid, InstanceCommandResult> _awaitedCommandResults = [];

        private ConcurrentDictionary<string, ManagedInstanceItems> _managedInstanceItems = [];
        private ConcurrentDictionary<string, StatusSummary> _managedInstanceStatusSummary = [];
        private ILogger<InstanceManagementStateProvider> _logger;
        private string _mgmtHubInstanceId = string.Empty;

        /// <summary>
        /// Create a new instance of the state provider
        /// </summary>
        /// <param name="logger"></param>
        public InstanceManagementStateProvider(ILogger<InstanceManagementStateProvider> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Clear all state
        /// </summary>
        public void Clear()
        {
            _logger.LogWarning("Flushing management hub state, clients will need to reconnect.");
            _instanceConnections.Clear();
            _managedInstanceItems.Clear();
            _awaitedCommandRequests.Clear();
            _awaitedCommandResults.Clear();
            _managedInstanceStatusSummary.Clear();

        }

        /// <summary>
        /// Set the instance ID of the management hub
        /// </summary>
        /// <param name="instanceId"></param>
        public void SetManagementHubInstanceId(string instanceId)
        {
            _mgmtHubInstanceId = instanceId;
        }

        /// <summary>
        /// Get the instance ID of the management hub
        /// </summary>
        /// <returns></returns>
        public string GetManagementHubInstanceId()
        {
            return _mgmtHubInstanceId;
        }

        /// <summary>
        /// Get a list of all connected instances
        /// </summary>
        /// <returns></returns>
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

            _instanceConnections.AddOrUpdate(connectionId, instanceInfo, (i, oldValue) =>
            {
                instanceInfo.ConnectionStatus = ConnectionStatus.Connected;
                return instanceInfo;
            });
        }

        /// <summary>
        /// Update the status summary for a given instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="summary"></param>
        public void UpdateInstanceStatusSummary(string instanceId, StatusSummary summary)
        {
            _managedInstanceStatusSummary.AddOrUpdate(instanceId, summary, (i, oldValue) => summary);
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

        /// <summary>
        /// Get the instance ID associated with a given connection ID
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
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
            _logger.LogDebug("[AddAwaitedCommandRequest] Issued command {commandId} {cmdType}.", command.CommandId, command.CommandType);

            _awaitedCommandRequests.AddOrUpdate(command.CommandId, command, (i, oldValue) => { return command; });
        }

        /// <summary>
        /// Get command request we are waiting on a response for
        /// </summary>
        /// <param name="commandId"></param>
        /// <returns></returns>
        public InstanceCommandRequest? GetAwaitedCommandRequest(Guid commandId)
        {
            _awaitedCommandRequests.TryGetValue(commandId, out var cmd);
            return cmd;
        }

        /// <summary>
        /// Add a command result we are waiting for
        /// </summary>
        /// <param name="result"></param>
        public void AddAwaitedCommandResult(InstanceCommandResult result)
        {
            _logger.LogDebug("[AddAwaitedCommandResult] {commandId} {cmdType}.", result.CommandId, result.CommandType);
            _awaitedCommandResults.AddOrUpdate(result.CommandId, result, (i, oldValue) => result);
        }

        /// <summary>
        /// Wait for a command result to be available
        /// </summary>
        /// <param name="cmd"></param>
        /// <returns></returns>
        public async Task<InstanceCommandResult?> ConsumeAwaitedCommandResult(InstanceCommandRequest cmd)
        {
            _logger.LogDebug("[ConsumeAwaitedCommandResult] Waiting for command result {commandId}..", cmd.CommandId);
            var attempts = 50;

            while (attempts > 0 && !_awaitedCommandResults.TryGetValue(cmd.CommandId, out _))
            {
                attempts--;
                await Task.Delay(100);
            }

            _awaitedCommandResults.Remove(cmd.CommandId, out var cmdResult);

            if (cmdResult == null)
            {
                _logger.LogError("[ConsumeAwaitedCommandResult] Gave up waiting for command result {commandId} {cmdType}..", cmd.CommandId, cmd.CommandType);
            }
            else
            {
                _logger.LogDebug("[ConsumeAwaitedCommandResult] Got command result {commandId} {cmdType}..", cmd.CommandId, cmd.CommandType);
            }

            return cmdResult;
        }

        /// <summary>
        /// Remove a command request we have received a response for
        /// </summary>
        /// <param name="commandId"></param>
        public void RemoveAwaitedCommandRequest(Guid commandId)
        {
            _awaitedCommandRequests.Remove(commandId, out var request);

            if (request != null)
            {
                _logger.LogDebug("[RemoveAwaitedCommandRequest] Removed command request {commandId} {cmdType}..", request.CommandId, request.CommandType);
            }
            else
            {
                _logger.LogWarning("[RemoveAwaitedCommandRequest] Could not remove unknown command request {commandId}..", commandId);
            }
        }

        /// <summary>
        /// Update the managed certificate items for a given instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="items"></param>
        public void UpdateInstanceItemInfo(string instanceId, List<ManagedCertificate> items)
        {
            var info = new ManagedInstanceItems { InstanceId = instanceId, Items = items };
            _managedInstanceItems.AddOrUpdate(instanceId, info, (k, old) => info);
        }

        /// <summary>
        /// Get the current managed certificate items for a given instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public ConcurrentDictionary<string, ManagedInstanceItems> GetManagedInstanceItems(string? instanceId = null)
        {
            return _managedInstanceItems;
        }

        /// <summary>
        /// Get the current status summaries for all managed instances
        /// </summary>
        /// <returns></returns>
        public ConcurrentDictionary<string, StatusSummary> GetManagedInstanceStatusSummaries()
        {
            return _managedInstanceStatusSummary;
        }

        /// <summary>
        /// Update a cached managed certificate item for a given instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="managedCertificate"></param>
        public void UpdateCachedManagedInstanceItem(string instanceId, ManagedCertificate managedCertificate)
        {
            _managedInstanceItems.TryGetValue(instanceId, out var instance);
            if (instance?.Items != null)
            {
                instance.Items.RemoveAll(r => r.Id == managedCertificate.Id);
                instance.Items.Add(managedCertificate);
            }
        }

        /// <summary>
        /// Check if we have any items cached for a given managed instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public bool HasItemsForManagedInstance(string instanceId)
        {
            return _managedInstanceItems.ContainsKey(instanceId);
        }

        /// <summary>
        /// Check if we have a status summary for a given managed instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public bool HasStatusSummaryForManagedInstance(string instanceId)
        {
            return _managedInstanceStatusSummary.ContainsKey(instanceId);
        }

        /// <summary>
        /// Remove a cached managed certificate item for a given instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="managedCertificateId"></param>
        public void DeleteCachedManagedInstanceItem(string instanceId, string managedCertificateId)
        {
            _managedInstanceItems.TryGetValue(instanceId, out var instance);

            if (instance?.Items != null)
            {
                instance.Items.RemoveAll(r => r.Id == managedCertificateId);
            }
        }

        public void UpdateInstanceConnectionStatus(string instanceId, string status)
        {
            var info = _instanceConnections.FirstOrDefault(k => k.Value.InstanceId == instanceId);
            if (info.Value != null)
            {
                info.Value.ConnectionStatus = status;
                UpdateInstanceConnectionInfo(info.Key, info.Value);

            }
        }
    }
}
