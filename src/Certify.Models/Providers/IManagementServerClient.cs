using System;
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

        Task ConnectAsync();
        Task Disconnect();
        bool IsConnected();
        void SendInstanceInfo(Guid commandId, bool isCommandResponse = true);
        void SendNotificationToManagementHub(string msgCommandType, object updateMsg);
    }
}