using Certify.Management;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.SignalR.ManagementHub;

namespace Certify.Server.HubService.Services
{
    /// <summary>
    /// Client for direct management server communication.
    /// </summary>
    public class DirectManagementServerClient : Client.IManagementServerClient
    {
        /// <summary>
        /// Event triggered when the connection is closed.
        /// </summary>
        public event Action? OnConnectionClosed;

        /// <summary>
        /// Event triggered when the connection is reconnected.
        /// </summary>
        public event Action? OnConnectionReconnected;

        /// <summary>
        /// Event triggered when the connection is reconnecting.
        /// </summary>
        public event Action? OnConnectionReconnecting;

        /// <summary>
        /// Event triggered to get the command result.
        /// </summary>
        public event Func<InstanceCommandRequest, Task<InstanceCommandResult>>? OnGetCommandResult;

        /// <summary>
        /// Event triggered to get the instance items.
        /// </summary>
        public event Func<ManagedInstanceItems>? OnGetInstanceItems;

        private ICertifyManager _certifyManager;
        private IInstanceManagementHub _managementHub;
        private ManagedInstanceInfo _instanceInfo;
        private string _joiningToken = default!;
        /// <summary>
        /// Initializes a new instance of the <see cref="DirectManagementServerClient"/> class.
        /// </summary>
        /// <param name="certifyManager">The certify manager.</param>
        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="instanceManagementHub">The instance management hub.</param>
        public DirectManagementServerClient(ICertifyManager certifyManager, IServiceProvider serviceProvider, IInstanceManagementHub instanceManagementHub)
        {
            _certifyManager = certifyManager;
            _managementHub = instanceManagementHub;

            _instanceInfo = certifyManager.GetManagedInstanceInfo();
        }

        /// <inheritdoc/>
        public Task ConnectAsync() => Task.CompletedTask;

        /// <inheritdoc/>
        public Task Disconnect() => throw new NotImplementedException();

        /// <inheritdoc/>
        public bool IsConnected() => true;

        /// <inheritdoc/>
        public void SendInstanceInfo(Guid commandId, bool isCommandResponse)
        {
            System.Diagnostics.Debug.WriteLine("SendInstanceInfo");

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

        /// <inheritdoc/>
        public void SendNotificationToManagementHub(string msgCommandType, object updateMsg)
        {
            var result = new InstanceCommandResult
            {
                CommandId = Guid.NewGuid(),
                InstanceId = _instanceInfo.InstanceId,
                CommandType = msgCommandType,
                Value = System.Text.Json.JsonSerializer.Serialize(updateMsg),
                ObjectValue = updateMsg,
                IsCommandResponse = false
            };

            result.ObjectValue = updateMsg;
            _managementHub.ReceiveCommandResult(result);
        }

        public void SetJoiningToken(string joiningToken)
        {
            _joiningToken = joiningToken;
        }
    }
}
