using System;
using System.Collections.Generic;
using System.Text;

namespace Certify.Shared
{
    /// <summary>
    /// Used to save configuration of most recently connected servers (UI)
    /// </summary>
    public class ServerConnection
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public bool UseHTTPS { get; set; } = false;
#if DEBUG
        public int Port { get; set; } = 9695;
#else
        public int Port { get; set; } = 9696;
#endif
        public string Host { get; set; } = "localhost";
        public DateTime? DateLastConnected { get; set; }

        public string Mode { get; set; } = "direct";
        public string Authentication { get; set; } = "default";

        public bool IsDefault { get; set; } = false;

        public ServerConnection()
        {

        }

        public ServerConnection(ServiceConfig config)
        {
            Id = Guid.NewGuid().ToString();
            UseHTTPS = config.UseHTTPS;
            Host = config.Host;
            Port = config.Port;
            DisplayName = "(local)";
            Mode = "direct";
            Authentication = "default";
            IsDefault = true;
        }
    }
}
