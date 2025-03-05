using Certify.Models.Hub;
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
        private int _updateFrequency = 10;
        private string _serviceName = "[Management Worker]";

        /// <summary>
        /// Create a new instance of the management worker
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="hubContext"></param>
        /// <param name="stateProvider"></param>
        public ManagementWorker(ILogger<ManagementWorker> logger, IHubContext<InstanceManagementHub> hubContext, IInstanceManagementStateProvider stateProvider)
        {
            _logger = logger;
            _hubContext = hubContext;
            _stateProvider = stateProvider;
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
        /// Dispatch a command to a connected instance
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="cmd"></param>
        private void DispatchCommand(string instanceId, InstanceCommandRequest cmd)
        {
            var connectionId = _stateProvider.GetConnectionIdForInstance(instanceId);
            if (connectionId == null)
            {
                _logger.LogWarning("{svc} Could not dispatch command to instance {instanceId}. Connection ID not yet known", _serviceName, instanceId);
            }
            else
            {
                _stateProvider.AddAwaitedCommandRequest(cmd);
                _hubContext.Clients.Client(connectionId).SendAsync(ManagementHubMessages.SendCommandRequest, cmd);
            }
        }

        /// <summary>
        /// Perform simple monitoring of connected instances
        /// </summary>
        /// <param name="state"></param>
        private void DoWork(object? state)
        {
            var instances = _stateProvider.GetConnectedInstances();
            _logger.LogInformation("{svc} connected instances: {count}", _serviceName, instances.Count());
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
