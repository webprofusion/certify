using System;

namespace Certify.Shared
{
    /// <summary>
    /// Used to save configuration of most recently connected servers (UI)
    /// </summary>
    public class ServerConnection
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; } = string.Empty;
        public bool UseHTTPS { get; set; }
        public bool AllowUntrusted { get; set; }
#if DEBUG
        public int Port { get; set; } = 9695;
#else
        public int Port { get; set; } = 9696;
#endif
        public string Host { get; set; } = "localhost";
        public DateTime? DateLastConnected { get; set; }

        public string? Mode { get; set; } = "direct";
        public string? Authentication { get; set; } = "default";
        public string? ServerMode { get; set; } = "v1";
        public bool IsDefault { get; set; }

        public ServerConnection()
        {
            Id = Guid.NewGuid().ToString();
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

        public override string ToString()
        {
            return $"{DisplayName ?? $"{Host}:{Port}"}";
        }
    }
}
