using Certify.API.Management;
using Certify.Server.Api.Public.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Api.Public.Services
{
    public class ManagementWorker : IHostedService, IDisposable
    {
        private readonly ILogger<ManagementWorker> _logger;
        private Timer? _timer = null;
        IHubContext<InstanceManagementHub> _hubContext;
        IInstanceManagementStateProvider _stateProvider;
        private int _updateFrequency = 10;
        private string _serviceName = "[Management Worker]";

        public ManagementWorker(ILogger<ManagementWorker> logger, IHubContext<InstanceManagementHub> hubContext, IInstanceManagementStateProvider stateProvider)
        {
            _logger = logger;
            _hubContext = hubContext;
            _stateProvider = stateProvider;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{svc} running.", _serviceName);
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(_updateFrequency));

            return Task.CompletedTask;
        }

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

        private void DoWork(object? state)
        {
            var instances = _stateProvider.GetConnectedInstances();
            _logger.LogInformation("{svc} connected instances: {count}", _serviceName, instances.Count());

            foreach (var instance in instances)
            {
                _logger.LogInformation("{svc} requesting info from instance: id:{id} title:{title}", _serviceName, instance.InstanceId, instance.Title);

                // refresh instance status
                var cmd = new InstanceCommandRequest { CommandId = Guid.NewGuid(), CommandType = ManagementHubCommands.GetInstanceItems, Value = null };

                DispatchCommand(instance.InstanceId, cmd);
            }

            // 
            var items = _stateProvider.GetManagedInstanceItems();
            _logger.LogInformation("{svc} total items managed across instances: {count}", _serviceName, items.SelectMany(s=>s.Value.Items).Count());

        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{svc} is stopping.", _serviceName);

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
