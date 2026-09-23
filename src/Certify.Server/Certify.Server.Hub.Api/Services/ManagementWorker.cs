using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Simple worker to monitor for connected instances and manage instance state
    /// </summary>
    public class ManagementWorker : IHostedService, IDisposable
    {
        private readonly ILogger<ManagementWorker> _logger;
        private Timer? _timer = null;
        IHubContext<InstanceManagementHub> _hubContext;
        IInstanceManagementStateProvider _stateProvider;

        private ManagementAPI _mgmtAPI;
        private readonly Activity.ActivityRecorder? _activityRecorder;
        private readonly Activity.HubActivityService? _activityService;

        private const int StaleInstanceCacheExpiryMinutes = 30;
        private int _updateFrequency = 30;
        private string _serviceName = "[Management Worker]";
        private bool _isBatchRunning = false;

        private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(6);

        private readonly HashSet<string> _unresponsiveInstances = new(StringComparer.OrdinalIgnoreCase);
        private DateTimeOffset _lastSnapshot = DateTimeOffset.MinValue;
        private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

        /// <summary>
        /// Create a new instance of the management worker
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="hubContext"></param>
        /// <param name="stateProvider"></param>
        /// <param name="mgmtAPI"></param>
        /// <param name="activityRecorder">optional, records instances which stop and resume responding</param>
        /// <param name="activityService">optional, records status snapshots and removes expired activity history</param>
        public ManagementWorker(ILogger<ManagementWorker> logger, IHubContext<InstanceManagementHub> hubContext, IInstanceManagementStateProvider stateProvider, ManagementAPI mgmtAPI,
            Activity.ActivityRecorder? activityRecorder = null, Activity.HubActivityService? activityService = null)
        {
            _logger = logger;
            _hubContext = hubContext;
            _stateProvider = stateProvider;
            _mgmtAPI = mgmtAPI;
            _activityRecorder = activityRecorder;
            _activityService = activityService;
        }

        /// <summary>
        /// Record instances which have stopped (or resumed) sending their regular heartbeat while connected
        /// </summary>
        internal async Task CheckInstanceResponsiveness(IEnumerable<Certify.Models.Hub.ManagedInstanceInfo> connectedInstances, DateTimeOffset now)
        {
            if (_activityRecorder == null)
            {
                return;
            }

            var connectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var instance in connectedInstances)
            {
                if (string.IsNullOrWhiteSpace(instance.InstanceId) || !connectedIds.Add(instance.InstanceId))
                {
                    continue;
                }

                var isStale = now - instance.DateLastReported > Activity.HubActivityService.UnresponsiveAfter;

                if (isStale && _unresponsiveInstances.Add(instance.InstanceId))
                {
                    await _activityRecorder.InstanceResponsivenessChangedAsync(instance.InstanceId, isResponsive: false, instance.DateLastReported);
                }
                else if (!isStale && _unresponsiveInstances.Remove(instance.InstanceId))
                {
                    await _activityRecorder.InstanceResponsivenessChangedAsync(instance.InstanceId, isResponsive: true, instance.DateLastReported);
                }
            }

            // an instance which disconnected is recorded as disconnected, not as having recovered
            _unresponsiveInstances.RemoveWhere(id => !connectedIds.Contains(id));
        }

        private async Task PerformActivityMaintenance(DateTimeOffset now)
        {
            if (_activityService == null)
            {
                return;
            }

            try
            {
                if (now - _lastSnapshot > SnapshotInterval)
                {
                    _lastSnapshot = now;
                    await _activityService.RecordStatusSnapshotsAsync();
                }

                if (now - _lastPurge > PurgeInterval)
                {
                    _lastPurge = now;
                    await _activityService.PurgeExpiredHistoryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{svc} activity history maintenance failed", _serviceName);
            }
        }

        /// <summary>
        /// Start the management worker
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{svc} running.", _serviceName);
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(_updateFrequency));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Perform simple monitoring of connected instances
        /// </summary>
        /// <param name="state"></param>
        private void DoWork(object? state)
        {
            if (!_isBatchRunning)
            {
                _isBatchRunning = true;

                try
                {
                    var instances = _stateProvider.GetConnectedInstances();
                    _logger.LogDebug("{svc} connected instances: {count}", _serviceName, instances.Count());

                    var staleCutoff = DateTimeOffset.UtcNow.AddMinutes(-StaleInstanceCacheExpiryMinutes);
                    var staleInstances = _stateProvider.GetInstancesNotSeenSince(staleCutoff).ToList();

                    foreach (var staleInstance in staleInstances)
                    {
                        _stateProvider.RemoveManagedInstanceRuntimeState(staleInstance.InstanceId);
                    }

                    if (staleInstances.Count > 0)
                    {
                        _logger.LogInformation(
                            "{svc} evicted cached managed items for {count} instance(s) not seen since {cutoff}.",
                            _serviceName,
                            staleInstances.Count,
                            staleCutoff);
                    }

                    var staleInstanceIds = staleInstances
                        .Select(i => i.InstanceId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    List<string> instancesToRefresh = [];

                    foreach (var instance in instances)
                    {
                        if (string.IsNullOrWhiteSpace(instance.InstanceId) || staleInstanceIds.Contains(instance.InstanceId))
                        {
                            continue;
                        }

                        // Check if LastUpdateId has changed, indicating managed items have been added/updated/deleted
                        var currentSummary = _stateProvider.GetManagedInstanceStatusSummary(instance.InstanceId);

                        // Refresh status summary for each instance
                        _ = _mgmtAPI.RefreshManagedCertificateSummary(instance.InstanceId, null);

                        if (currentSummary != null)
                        {
                            var hasChanges = _stateProvider.HasLastUpdateIdChanged(instance.InstanceId, currentSummary.LastUpdateId);

                            if (hasChanges)
                            {
                                instancesToRefresh.Add(instance.InstanceId);
                            }
                        }
                    }

                    foreach (var instanceId in instancesToRefresh)
                    {
                        _logger.LogInformation("{svc} Instance {instanceId} has updated items, scheduling full refresh of managed items", _serviceName, instanceId);

                        // Schedule a full refresh of managed items since something has changed
                        _ = _mgmtAPI.RefreshInstanceManagedItems(instanceId, null);
                    }

                    var now = DateTimeOffset.UtcNow;

                    CheckInstanceResponsiveness(instances, now).GetAwaiter().GetResult();
                    PerformActivityMaintenance(now).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{svc} background worker error", _serviceName);
                }
                finally
                {
                    _isBatchRunning = false;
                }
            }
            else
            {
                _logger.LogInformation("{svc} background worker is still running a previous batch", _serviceName);
            }
        }

        /// <summary>
        /// Stop the management worker
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{svc} is stopping.", _serviceName);

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Dispose of the management worker timer etc
        /// </summary>
        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
