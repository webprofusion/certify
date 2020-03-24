using Certify.Models;
using Microsoft.AspNet.SignalR;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Certify.Service
{
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
            Debug.WriteLine("Client connected to status stream..");

            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            Debug.WriteLine("Client disconnected from status stream..");
            return base.OnDisconnected(stopCalled);
        }

        public static void SendRequestProgressState(RequestProgressState state)
        {
            Debug.WriteLine("Sending progress state..");
            HubContext.Clients.All.RequestProgressStateUpdated(state);
        }

        public static void SendManagedCertificateUpdate(ManagedCertificate site)
        {
            Debug.WriteLine("Sending updated managed site..");
            HubContext.Clients.All.ManagedCertificateUpdated(site);
        }
    }
}
