using System.Diagnostics;
using System.Threading.Tasks;
using Certify.Models;
using Microsoft.AspNet.SignalR;

namespace Certify.Service
{

    public class StatusHubReporting : Providers.IStatusReporting
    {

        public StatusHubReporting()
        {

        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task ReportRequestProgress(RequestProgressState state)
        {
            System.Diagnostics.Debug.WriteLine($"Sending progress update message to UI: {state.Message}");
            StatusHub.SendProgressState(state);
        }

        public async Task ReportManagedCertificateUpdated(ManagedCertificate item)

        {
            System.Diagnostics.Debug.WriteLine($"Sending updated managed cert message to UI: {item.Name}");
            StatusHub.SendManagedCertificateUpdate(item);
        }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }


    public class StatusHub : Hub
    {
        /// <summary>
        /// static instance reference for other parts of service to call in to 
        /// </summary>
        public static IHubContext HubContext
        {
            get
            {
                if (_context == null)
                {
                    _context = GlobalHost.ConnectionManager.GetHubContext<StatusHub>();
                }

                return _context;
            }
        }

        private static IHubContext _context = null;

        public override Task OnConnected()
        {
            Debug.WriteLine("StatusHub: Client connected to status stream..");

            if (nameof(SendProgressState) != Providers.StatusHubMessages.SendProgressStateMsg)
            {
                throw new System.ArgumentException("Invalid hub message name");
            }

            if (nameof(SendManagedCertificateUpdate) != Providers.StatusHubMessages.SendManagedCertificateUpdateMsg)
            {
                throw new System.ArgumentException("Invalid hub message name");
            }

            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            Debug.WriteLine("StatusHub: Client disconnected from status stream..");
            return base.OnDisconnected(stopCalled);
        }

        public static void SendProgressState(RequestProgressState state)
        {
            Debug.WriteLine("StatusHub: Broadcasting progress state to UI/clients..");
            HubContext.Clients.All.SendProgressState(state);
        }

        public static void SendManagedCertificateUpdate(ManagedCertificate site)
        {
            Debug.WriteLine("StatusHub: Broadcasting managed site update to UI/clients..");
            HubContext.Clients.All.SendManagedCertificateUpdate(site);
        }
    }
}
