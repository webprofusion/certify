using System;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models.Hub;

namespace Certify.Client
{
    public interface IManagementServerClient
    {
        event Action OnConnectionClosed;
        event Action OnConnectionReconnected;
        event Action OnConnectionReconnecting;
        event Func<InstanceCommandRequest, Task<InstanceCommandResult>> OnGetCommandResult;
        event Func<ManagedInstanceItems> OnGetInstanceItems;

        /// <summary>
        /// Connect to the management hub, acquiring an auth token via the given factory.
        /// </summary>
        /// <param name="hubConnectionTokenFactory">
        /// Invoked for each connect and reconnect attempt, so it must return a currently valid token rather than a
        /// previously acquired one, otherwise reconnects begin to fail once the original token expires.
        /// </param>
        Task ConnectAsync(Func<CancellationToken, Task<string>> hubConnectionTokenFactory);
        Task Disconnect();
        bool IsConnected();
        void SendInstanceInfo(Guid commandId, bool isCommandResponse = true);
        void SendNotificationToManagementHub(string msgCommandType, object updateMsg);

        void UpdateCachedInstanceInfo(ManagedInstanceInfo instanceInfo);
    }
}
