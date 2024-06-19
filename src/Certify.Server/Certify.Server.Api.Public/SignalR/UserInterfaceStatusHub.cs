using System.Diagnostics;
using Certify.Models;
using Certify.Providers;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Api.Public.SignalR
{
    /// <summary>
    /// Forwards status messages via SignalR back to UI client(s)
    /// </summary>
    public class UserInterfaceStatusHubReporting : IStatusReporting
    {
        private IHubContext<UserInterfaceStatusHub> _hubContext;

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="hubContext"></param>
        public UserInterfaceStatusHubReporting(IHubContext<UserInterfaceStatusHub> hubContext)
        {
            _hubContext = hubContext;
        }

        /// <summary>
        /// Send progress result back to subscribed UIs
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public async Task ReportRequestProgress(RequestProgressState state)
        {
            Debug.WriteLine($"Sending progress update message to UI: {state.Message}");
            await _hubContext.Clients.All.SendAsync(StatusHubMessages.SendProgressStateMsg, state);

        }

        /// <summary>
        /// Report change to managed certificate to subscribers
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task ReportManagedCertificateUpdated(ManagedCertificate item)
        {
            Debug.WriteLine($"Sending updated managed cert message to UI: {item.Name}");
            await _hubContext.Clients.All.SendAsync(StatusHubMessages.SendManagedCertificateUpdateMsg, item);
        }
    }

    /// <summary>
    /// Status Hub interface
    /// </summary>
    public interface IUserInterfaceStatusHub
    {
        /// <summary>
        /// Send progress result back to subscribed UIs
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        Task SendRequestProgressState(RequestProgressState state);

        /// <summary>
        /// Send managed certificate update to subscribers
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        Task SendManagedCertificateUpdate(ManagedCertificate item);
    }

    /// <summary>
    /// Status hub
    /// </summary>
    public class UserInterfaceStatusHub : Hub<IUserInterfaceStatusHub>
    {
        /// <summary>
        /// Handle connection event
        /// </summary>
        /// <returns></returns>
        public override Task OnConnectedAsync()
        {
            Debug.WriteLine("StatusHub: Client connected to status stream..");
            return base.OnConnectedAsync();
        }

        /// <summary>
        /// Handle disconnection event
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            Debug.WriteLine("StatusHub: Client disconnected from status stream..");
            return base.OnDisconnectedAsync(exception);
        }
    }
}
