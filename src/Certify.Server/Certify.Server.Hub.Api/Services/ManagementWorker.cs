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

        private int _updateFrequency = 30;
        private string _serviceName = "[Management Worker]";
        private bool _isBatchRunning = false;

        /// <summary>
        /// Create a new instance of the management worker
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="hubContext"></param>
        /// <param name="stateProvider"></param>
        /// <param name="mgmtAPI"></param>
        public ManagementWorker(ILogger<ManagementWorker> logger, IHubContext<InstanceManagementHub> hubContext, IInstanceManagementStateProvider stateProvider, ManagementAPI mgmtAPI)
        {
            _logger = logger;
            _hubContext = hubContext;
            _stateProvider = stateProvider;
            _mgmtAPI = mgmtAPI;
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
                try
                {
                    var instances = _stateProvider.GetConnectedInstances();
                    _logger.LogDebug("{svc} connected instances: {count}", _serviceName, instances.Count());

                    List<string> instancesToRefresh = [];

                    foreach (var instance in instances)
                    {
                        // Check if LastUpdateId has changed, indicating managed items have been added/updated/deleted
                        var currentSummary = _stateProvider.GetManagedInstanceStatusSummary(instance.InstanceId);

                        // Refresh status summary for each instance
                        _ = _mgmtAPI.RefreshManagedCertificateSummary(instance.InstanceId, null);

                        if (currentSummary != null)
                        {
                            var hasChanges = _stateProvider.HasLastUpdateIdChanged(instance.InstanceId, currentSummary.LastUpdateId);

                            if (hasChanges && !string.IsNullOrWhiteSpace(instance?.InstanceId))
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
