#pragma warning disable CS0067 // Event is never used

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

        private IInstanceManagementHub _managementHub;
        private ManagedInstanceInfo _instanceInfo = default!;
        /// <summary>
        /// Initializes a new instance of the <see cref="DirectManagementServerClient"/> class.
        /// </summary>

        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="instanceManagementHub">The instance management hub.</param>
        public DirectManagementServerClient(IServiceProvider serviceProvider, IInstanceManagementHub instanceManagementHub)
        {
            _managementHub = instanceManagementHub;
        }

        /// <inheritdoc/>
        public Task ConnectAsync(string hubConnectionAuthToken) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task Disconnect() => Task.CompletedTask;

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

        /// <summary>
        /// Updates the cached instance information.
        /// </summary>
        /// <param name="instanceInfo">The updated instance information.</param>
        public void UpdateCachedInstanceInfo(ManagedInstanceInfo instanceInfo)
        {
            _instanceInfo = instanceInfo;
        }
    }
}

#pragma warning restore CS0067
