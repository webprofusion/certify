using System.Diagnostics;
using Certify.Models;
using Certify.Providers;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Hub.Api.SignalR
{
    /// <summary>
    /// Forwards status messages via SignalR back to UI client(s)
    /// </summary>
    public class UserInterfaceStatusHubReporting : IStatusReporting
    {
        private readonly UserInterfaceStatusBroadcaster _broadcaster;

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
        /// <param name="broadcaster"></param>
        public UserInterfaceStatusHubReporting(UserInterfaceStatusBroadcaster broadcaster)
        {
            _broadcaster = broadcaster;
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

            await _broadcaster.SendRequestProgress(state);

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

            await _broadcaster.SendManagedItemUpdated(item);
        }

        /// <summary>
        /// Report a service level diagnostic which requires operator action to subscribers
        /// </summary>
        /// <param name="diagnostic"></param>
        /// <returns></returns>
        public async Task ReportDiagnosticActionRequired(Certify.Models.Reporting.DiagnosticActionRequired diagnostic)
        {
            Debug.WriteLine($"Sending diagnostic action required message to UI: {diagnostic.Title}");

            await _broadcaster.SendDiagnosticActionRequired(diagnostic);
        }
    }

    /// <summary>
    /// Status hub
    ///
    /// Connections are authenticated by the JWT bearer middleware during the negotiate/handshake request, so a client
    /// presenting a missing, invalid or expired token is rejected with a 401 and never receives status updates. Each
    /// connection is then tracked against the caller it authenticated as, and <see cref="UserInterfaceStatusBroadcaster"/>
    /// sends it only the updates that caller may see.
    /// </summary>
    [Authorize]
    public class UserInterfaceStatusHub : Microsoft.AspNetCore.SignalR.Hub
    {
        private readonly UserInterfaceStatusBroadcaster _broadcaster;

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="broadcaster"></param>
        public UserInterfaceStatusHub(UserInterfaceStatusBroadcaster broadcaster)
        {
            _broadcaster = broadcaster;
        }

        /// <summary>
        /// Handle connection event
        /// </summary>
        /// <returns></returns>
        public override Task OnConnectedAsync()
        {
            Debug.WriteLine("StatusHub: Client connected to status stream..");

            // a connection with no security principal to evaluate is left untracked, and so is sent nothing
            var authContext = PrincipalAccess.GetAuthContext(Context.User);

            if (authContext != null)
            {
                _broadcaster.AddConnection(Context.ConnectionId, authContext);
            }

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

            _broadcaster.RemoveConnection(Context.ConnectionId);

            return base.OnDisconnectedAsync(exception);
        }
    }
}
