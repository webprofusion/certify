using System.Diagnostics;
using Certify.Models;
using Certify.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Hub.Api.SignalR
{
    /// <summary>
    /// Forwards status messages via SignalR back to UI client(s)
    /// </summary>
    public class UserInterfaceStatusHubReporting : IStatusReporting
    {
        private IHubContext<UserInterfaceStatusHub> _hubContext;

        /// <summary>
        /// Event raised when a progress update is available
        /// </summary>
        public event Action<RequestProgressState>? OnRequestProgressStateUpdated;

        /// <summary>
        /// Event raised when a managed certificate has been updated
        /// </summary>
        public event Action<ManagedCertificate>? OnManagedCertificateUpdated;

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
            if (OnRequestProgressStateUpdated != null)
            {
                OnRequestProgressStateUpdated.Invoke(state);
            }

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

            if (OnManagedCertificateUpdated != null)
            {
                OnManagedCertificateUpdated.Invoke(item);
            }

            await _hubContext.Clients.All.SendAsync(StatusHubMessages.SendManagedCertificateUpdateMsg, item);
        }

        /// <summary>
        /// Report a service level diagnostic which requires operator action to subscribers
        /// </summary>
        /// <param name="diagnostic"></param>
        /// <returns></returns>
        public async Task ReportDiagnosticActionRequired(Certify.Models.Reporting.DiagnosticActionRequired diagnostic)
        {
            Debug.WriteLine($"Sending diagnostic action required message to UI: {diagnostic.Title}");

            await _hubContext.Clients.All.SendAsync(
                StatusHubMessages.SendMsg,
                StatusHubMessages.NotificationActionRequired,
                System.Text.Json.JsonSerializer.Serialize(diagnostic));
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
    ///
    /// Connections are authenticated by the JWT bearer middleware during the negotiate/handshake request, so a client
    /// presenting a missing, invalid or expired token is rejected with a 401 and never receives status updates. This
    /// hub broadcasts managed certificate state for every connected instance, so it must not accept anonymous clients.
    /// </summary>
    [Authorize]
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
