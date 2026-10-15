using System.Collections.Concurrent;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;

namespace Certify.Server.Hub.Api.SignalR.ManagementHub
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public interface IInstanceManagementStateProvider
    {
        void SetManagementHubInstanceId(string instanceId);
        string GetManagementHubInstanceId();
        void UpdateInstanceConnectionInfo(string connectionId, ManagedInstanceInfo instanceInfo);
        void UpdateInstanceStatusSummary(string instanceId, StatusSummary summary);

        /// <summary>
        /// Note that an instance's cached item list may be missing changes and should be fetched again in full
        /// </summary>
        void MarkItemRefreshDue(string instanceId);

        /// <summary>
        /// True, and no longer due, if the instance's item list is due a full refresh and one was not taken within the
        /// given interval
        /// </summary>
        bool TakeItemRefreshIfDue(string instanceId, TimeSpan minInterval);

        string GetConnectionIdForInstance(string instanceId);
        string? GetInstanceIdForConnection(string connectionId);
        List<ManagedInstanceInfo> GetConnectedInstances();
        void AddAwaitedCommandRequest(InstanceCommandRequest command);

        /// <summary>
        /// Remove an awaited command so we don't keep waiting on the result
        /// </summary>
        /// <param name="commandId"></param>
        void RemoveAwaitedCommandRequest(Guid commandId);
        InstanceCommandRequest? GetAwaitedCommandRequest(Guid commandId);
        void AddAwaitedCommandResult(InstanceCommandResult result);

        /// <summary>
        /// Wait for a command result to be available
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="timeout">how long to wait for the result, 5 seconds if not given</param>
        /// <returns></returns>
        Task<InstanceCommandResult?> ConsumeAwaitedCommandResult(InstanceCommandRequest cmd, TimeSpan? timeout = null);
        void UpdateInstanceItemInfo(string instanceId, List<ManagedCertificate> items);
        ConcurrentDictionary<string, ManagedInstanceItems> GetManagedInstanceItems(string? instanceId = null);
        void UpdateCachedManagedInstanceItem(string instanceId, ManagedCertificate managedCertificate);
        void DeleteCachedManagedInstanceItem(string instanceId, string managedCertificateId);
        void RemoveManagedInstanceRuntimeState(string instanceId);
        bool HasItemsForManagedInstance(string instanceId);

        /// <summary>
        /// Record the CAs an instance reported, so its items can name their CA, including custom CAs the hub does not have
        /// </summary>
        void UpdateInstanceCertificateAuthorities(string instanceId, IEnumerable<CertificateAuthority> certificateAuthorities);

        /// <summary>
        /// CA titles keyed by CA id, as last reported by the instance, or null if it has not reported them
        /// </summary>
        IReadOnlyDictionary<string, string>? GetInstanceCertificateAuthorityTitles(string instanceId);

        bool HasStatusSummaryForManagedInstance(string instanceId);
        ConcurrentDictionary<string, StatusSummary> GetManagedInstanceStatusSummaries();
        StatusSummary? GetManagedInstanceStatusSummary(string instanceId);
        void UpdateInstanceConnectionStatus(string instanceId, string status);

        IEnumerable<ManagedInstanceInfo> GetInstancesNotSeenSince(DateTimeOffset cutoff);

        void AddOrUpdateSystemStatusItem(ActionStep status);
        List<ActionStep> GetSystemStatusItems();
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
        private ConcurrentDictionary<string, bool> _itemRefreshesDue = [];
        private ConcurrentDictionary<string, DateTimeOffset> _itemRefreshesTaken = [];
        private ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _instanceCertificateAuthorityTitles = new(StringComparer.OrdinalIgnoreCase);

        private List<ActionStep> _systemStatusItems = [];

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
        /// Get a list of all connected instances. Disconnected instances stay tracked until they reconnect so their cached state can still expire, but are not included.
        /// </summary>
        /// <returns></returns>
        public List<ManagedInstanceInfo> GetConnectedInstances()
        {
            return _instanceConnections.Values.Where(i => i.ConnectionStatus != ConnectionStatus.Disconnected).ToList();
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
                // connection ids are not reused, so a late update from a dropped connection must not mark it connected again
                instanceInfo.ConnectionStatus = oldValue.ConnectionStatus == ConnectionStatus.Disconnected ? ConnectionStatus.Disconnected : ConnectionStatus.Connected;
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
            if (summary == null)
            {
                // we don't store null summary updates, keep latest if any
                return;
            }

            var previousSummary = _managedInstanceStatusSummary.TryGetValue(instanceId, out var existing) ? existing : null;

            _managedInstanceStatusSummary.AddOrUpdate(instanceId, summary, (i, oldValue) => summary);

            // the instance counts its item changes. The per item notifications it sends can be lost, for example while
            // it is reconnecting, and some changes are not notified at all, so a change in the count means the cached
            // list may be out of date. A restarted instance counts from zero again, which also differs.
            if (previousSummary != null && previousSummary.LastUpdateId != summary.LastUpdateId)
            {
                _logger.LogDebug("Instance {instanceId} LastUpdateId changed from {oldId} to {newId}, its managed items are due a full refresh.",
                    instanceId, previousSummary.LastUpdateId, summary.LastUpdateId);

                MarkItemRefreshDue(instanceId);
            }

            // Cleanup any mismatched entries where the key doesn't match the summary's InstanceId, these can appear if instance id was temporary while verifying connection
            var keysToRemove = _managedInstanceStatusSummary
                .Where(kvp => kvp.Key != kvp.Value.InstanceId)
                .Select(kvp => kvp.Key)
                .ToList();

            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaning up {count} mismatched managed instance status summary entries.", keysToRemove.Count);
            }

            foreach (var key in keysToRemove)
            {
                _managedInstanceStatusSummary.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Note that an instance's cached item list may be missing changes and should be fetched again in full
        /// </summary>
        /// <param name="instanceId"></param>
        public void MarkItemRefreshDue(string instanceId)
        {
            _itemRefreshesDue[instanceId] = true;
        }

        /// <summary>
        /// True, and no longer due, if the instance's item list is due a full refresh and one was not taken within the
        /// given interval. The interval limits how often a busy instance with many items is asked for its whole list,
        /// and how often an instance which did not respond is asked again.
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="minInterval"></param>
        /// <returns></returns>
        public bool TakeItemRefreshIfDue(string instanceId, TimeSpan minInterval)
        {
            if (!_itemRefreshesDue.ContainsKey(instanceId))
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;

            if (_itemRefreshesTaken.TryGetValue(instanceId, out var taken) && taken > now - minInterval)
            {
                return false;
            }

            if (!_itemRefreshesDue.TryRemove(instanceId, out _))
            {
                return false;
            }

            _itemRefreshesTaken[instanceId] = now;

            return true;
        }

        /// <summary>
        /// Get the current connection ID we haev associated with the given instance id
        /// </summary>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public string GetConnectionIdForInstance(string instanceId)
        {
            // TODO: of instances use the same instanceid accidentally they will clobber each other
            var info = _instanceConnections.FirstOrDefault(k => k.Value.InstanceId == instanceId && k.Value.ConnectionStatus != ConnectionStatus.Disconnected);

            return info.Key;
        }

        /// <summary>
        /// Get the instance ID associated with a given connection ID
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public string? GetInstanceIdForConnection(string connectionId)
        {
            if (connectionId == _mgmtHubInstanceId)
            {
                // directly connected instance
                return _mgmtHubInstanceId;
            }

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
        /// <param name="timeout">how long to wait for the result, 5 seconds if not given</param>
        /// <returns></returns>
        public async Task<InstanceCommandResult?> ConsumeAwaitedCommandResult(InstanceCommandRequest cmd, TimeSpan? timeout = null)
        {
            _logger.LogDebug("[ConsumeAwaitedCommandResult] Waiting for command result {commandId}..", cmd.CommandId);
            var giveUpAt = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

            while (DateTimeOffset.UtcNow < giveUpAt && !_awaitedCommandResults.TryGetValue(cmd.CommandId, out _))
            {
                await Task.Delay(100);
            }

            _awaitedCommandResults.Remove(cmd.CommandId, out var cmdResult);

            if (cmdResult == null)
            {
                _logger.LogDebug("[ConsumeAwaitedCommandResult] Gave up waiting for command result {commandId} {cmdType}..", cmd.CommandId, cmd.CommandType);
            }
            else
            {
                _logger.LogDebug("[ConsumeAwaitedCommandResult] Received and consumed command result {commandId} {cmdType}..", cmd.CommandId, cmd.CommandType);
            }

            RemoveAwaitedCommandRequest(cmd.CommandId);

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
                _logger.LogDebug("[RemoveAwaitedCommandRequest] Removed awaited command request {commandId} {cmdType}..", request.CommandId, request.CommandType);
            }
        }

        /// <summary>
        /// Update the managed certificate items for a given instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="items"></param>
        public void UpdateInstanceItemInfo(string instanceId, List<ManagedCertificate> items)
        {
            var info = new ManagedInstanceItems { InstanceId = instanceId, Items = items, LastRefreshed = DateTimeOffset.UtcNow };
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
        /// Retrieves the status summary of a managed instance based on its identifier.
        /// </summary>
        /// <param name="instanceId">The identifier used to locate the specific managed instance's status summary.</param>
        /// <returns>Returns the status summary associated with the specified managed instance, or null if not found.</returns>
        public StatusSummary? GetManagedInstanceStatusSummary(string instanceId)
        {
            return _managedInstanceStatusSummary.FirstOrDefault(s => s.Key == instanceId).Value;
        }

        /// <summary>
        /// Update a cached managed certificate item for a given instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="managedCertificate"></param>
        public void UpdateCachedManagedInstanceItem(string instanceId, ManagedCertificate managedCertificate)
        {
            if (_managedInstanceItems.TryGetValue(instanceId, out var instance))
            {
                if (instance.Items != null)
                {
                    // replace item in the list at the same position

                    var original = instance.Items.FirstOrDefault(i => i.Id == managedCertificate.Id);
                    if (original != null)
                    {
                        var idx = instance.Items.IndexOf(original);
                        instance.Items[idx] = managedCertificate;
                    }
                    else
                    {
                        instance.Items.Add(managedCertificate);
                    }
                }
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
            if (_managedInstanceItems.TryGetValue(instanceId, out var instance))
            {
                instance.Items.RemoveAll(r => r.Id == managedCertificateId);
            }
        }

        /// <summary>
        /// Remove cached runtime state for a managed instance.
        /// </summary>
        /// <param name="instanceId"></param>
        public void RemoveManagedInstanceRuntimeState(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return;
            }

            var removedItems = _managedInstanceItems.TryRemove(instanceId, out var managedItems);
            var removedSummary = _managedInstanceStatusSummary.TryRemove(instanceId, out _);
            _itemRefreshesDue.TryRemove(instanceId, out _);
            _itemRefreshesTaken.TryRemove(instanceId, out _);
            _instanceCertificateAuthorityTitles.TryRemove(instanceId, out _);

            if (removedItems || removedSummary)
            {
                _logger.LogInformation(
                    "Removed cached runtime state for managed instance {instanceId}. RemovedItems: {removedItems}. RemovedItemCount: {itemCount}. RemovedSummary: {removedSummary}.",
                    instanceId,
                    removedItems,
                    managedItems?.Items?.Count ?? 0,
                    removedSummary);
            }
        }

        /// <summary>
        /// Record the CAs an instance reported, so its items can name their CA
        /// </summary>
        public void UpdateInstanceCertificateAuthorities(string instanceId, IEnumerable<CertificateAuthority> certificateAuthorities)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return;
            }

            var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ca in certificateAuthorities)
            {
                if (!string.IsNullOrEmpty(ca.Id) && !string.IsNullOrEmpty(ca.Title))
                {
                    titles[ca.Id] = ca.Title;
                }
            }

            _instanceCertificateAuthorityTitles[instanceId] = titles;
        }

        /// <summary>
        /// CA titles keyed by CA id, as last reported by the instance, or null if it has not reported them
        /// </summary>
        public IReadOnlyDictionary<string, string>? GetInstanceCertificateAuthorityTitles(string instanceId)
        {
            return _instanceCertificateAuthorityTitles.TryGetValue(instanceId, out var titles) ? titles : null;
        }

        /// <summary>
        /// Get tracked instances that have not reported since the specified cutoff.
        /// </summary>
        /// <param name="cutoff"></param>
        /// <returns></returns>
        public IEnumerable<ManagedInstanceInfo> GetInstancesNotSeenSince(DateTimeOffset cutoff)
        {
            return _instanceConnections.Values
                .Where(i => !string.IsNullOrWhiteSpace(i.InstanceId) && i.DateLastReported != default && i.DateLastReported <= cutoff)
                .GroupBy(i => i.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(i => i.DateLastReported).First());
        }

        /// <summary>  
        /// Update the connection status for a given instance.  
        /// </summary>  
        /// <param name="instanceId">The ID of the instance.</param>  
        /// <param name="status">The new connection status.</param>  
        public void UpdateInstanceConnectionStatus(string instanceId, string status)
        {
            var info = _instanceConnections.FirstOrDefault(k => k.Value.InstanceId == instanceId);
            if (info.Value != null)
            {
                info.Value.ConnectionStatus = status;
            }
        }

        /// <summary>
        /// Adds a new system status item or updates an existing one in the collection.
        /// </summary>
        /// <param name="status">Represents the action step to be added or updated in the system status items.</param>
        public void AddOrUpdateSystemStatusItem(ActionStep status)
        {
            var existing = _systemStatusItems.FirstOrDefault(k => k.Key == status.Key);

            if (existing != null)
            {
                _systemStatusItems.Remove(existing);
            }

            _systemStatusItems.Add(status);
        }

        /// <summary>
        /// Retrieves a list of action steps representing the current system status.
        /// </summary>
        /// <returns>Returns a List of ActionStep objects.</returns>
        public List<ActionStep> GetSystemStatusItems()
        {
            return _systemStatusItems.ToList();
        }
    }
}
