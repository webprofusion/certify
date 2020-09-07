using Certify.Models;
using Certify.Providers;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Certify.Service
{
    public class StatusHubReporting : Providers.IStatusReporting
    {
        IHubContext<Service.StatusHub> _hubContext;
        public StatusHubReporting(IHubContext<Service.StatusHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task ReportRequestProgress(RequestProgressState state)
        {
            System.Diagnostics.Debug.WriteLine($"Sending progress update message to UI: {state.Message}");
            await _hubContext.Clients.All.SendAsync(StatusHubMessages.SendProgressStateMsg, state);

        }

        public async Task ReportManagedCertificateUpdated(ManagedCertificate item)
        {
            System.Diagnostics.Debug.WriteLine($"Sending updated managed cert message to UI: {item.Name}");
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
