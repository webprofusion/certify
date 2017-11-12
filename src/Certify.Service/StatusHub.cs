using Microsoft.AspNet.SignalR;
using System.Threading.Tasks;
using System.Diagnostics;

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
                if (_context == null) _context = GlobalHost.ConnectionManager.GetHubContext<StatusHub>();
                return _context;
            }
        }

        private static IHubContext _context = null;

        public override Task OnConnected()
        {
            Debug.WriteLine("Client connect to status stream..");

            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            Debug.WriteLine("Client disconnected from status stream..");
            return base.OnDisconnected(stopCalled);
        }

        public void SendRequestProgressState(Certify.Models.RequestProgressState state)
        {
            Debug.WriteLine("Sending progress state..");
            Clients.All.RequestProgressStateUpdated(state);
        }

        public void Send(string name, string message)
        {
            Clients.All.SendMessage(name, message);
        }
    }
}