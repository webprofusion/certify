using System.Diagnostics;
using Certify.Models;
using Certify.Providers;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Service
{
    public class StatusHubReporting : IStatusReporting
    {
        private IHubContext<StatusHub> _hubContext;
        public StatusHubReporting(IHubContext<StatusHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task ReportRequestProgress(RequestProgressState state)
        {
            Debug.WriteLine($"Sending progress update message to UI: {state.Message}");
            await _hubContext.Clients.All.SendAsync(StatusHubMessages.SendProgressStateMsg, state);

        }

        public async Task ReportManagedCertificateUpdated(ManagedCertificate item)
        {
            Debug.WriteLine($"Sending updated managed cert message to UI: {item.Name}");
            await _hubContext.Clients.All.SendAsync(StatusHubMessages.SendManagedCertificateUpdateMsg, item);
        }

        public async Task ReportDiagnosticActionRequired(Certify.Models.Reporting.DiagnosticActionRequired diagnostic)
        {
            Debug.WriteLine($"Sending diagnostic action required message to UI: {diagnostic.Title}");

            await _hubContext.Clients.All.SendAsync(
                StatusHubMessages.SendMsg,
                StatusHubMessages.NotificationActionRequired,
                System.Text.Json.JsonSerializer.Serialize(diagnostic));
        }
    }

    public interface IStatusHub
    {
        Task SendRequestProgressState(RequestProgressState state);

        Task SendManagedCertificateUpdate(ManagedCertificate item);

        Task SendMessage(string notificationType, string payload);
    }

    public class StatusHub : Hub<IStatusHub>
    {
        public override Task OnConnectedAsync()
        {
            Debug.WriteLine("StatusHub: Client connected to status stream..");
            return base.OnConnectedAsync();
        }
        public override Task OnDisconnectedAsync(Exception exception)
        {
            Debug.WriteLine("StatusHub: Client disconnected from status stream..");
            return base.OnDisconnectedAsync(exception);
        }
    }
}
