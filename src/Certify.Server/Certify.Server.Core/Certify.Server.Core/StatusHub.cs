using System;
using System.Diagnostics;
using System.Threading.Tasks;
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
    }

    public interface IStatusHub
    {
        Task SendRequestProgressState(RequestProgressState state);

        Task SendManagedCertificateUpdate(ManagedCertificate item);
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
