using Certify.Client;
using Certify.Management;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.SignalR.ManagementHub;

namespace Certify.Server.HubService.Services
{
    public class DirectManagementServerClient : Client.IManagementServerClient
    {
        public event Action OnConnectionClosed;
        public event Action OnConnectionReconnected;
        public event Action OnConnectionReconnecting;
        public event Func<InstanceCommandRequest, Task<InstanceCommandResult>> OnGetCommandResult;
        public event Func<ManagedInstanceItems> OnGetInstanceItems;

        private ICertifyManager _certifyManager;
        private IInstanceManagementHub _managementHub;

        private ManagedInstanceInfo _instanceInfo;
        public DirectManagementServerClient(ICertifyManager certifyManager, IServiceProvider serviceProvider, IInstanceManagementHub instanceManagementHub)
        {
            _certifyManager = certifyManager;
            _managementHub = instanceManagementHub;
            _instanceInfo = certifyManager.GetManagedInstanceInfo();
        }

        Task IManagementServerClient.ConnectAsync() => Task.CompletedTask;
        Task IManagementServerClient.Disconnect() => throw new NotImplementedException();
        bool IManagementServerClient.IsConnected() => true;
        void IManagementServerClient.SendInstanceInfo(Guid commandId, bool isCommandResponse)
        {
            System.Diagnostics.Debug.WriteLine("SendInstanceInfo");

            // send this clients instance ID back to the hub to identify it in the connection: should send a shared secret before this to confirm this client knows and is not impersonating another instance
            var result = new InstanceCommandResult
            {
                CommandId = commandId,
                InstanceId = _instanceInfo.InstanceId,
                CommandType = ManagementHubCommands.GetInstanceInfo,
                Value = System.Text.Json.JsonSerializer.Serialize(_instanceInfo),
                IsCommandResponse = isCommandResponse
            };

            result.ObjectValue = _instanceInfo;

            _managementHub.ReceiveCommandResult(result);
        }
        void IManagementServerClient.SendNotificationToManagementHub(string msgCommandType, object updateMsg)
        {
            System.Diagnostics.Debug.WriteLine("SendInstanceInfo");

        }
    }
}
