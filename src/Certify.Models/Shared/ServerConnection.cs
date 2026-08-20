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
        public string Host { get; set; } = Certify.Shared.ServiceConfig.DEFAULT_LOCALHOST;
        public DateTime? DateLastConnected { get; set; }

        public string? Mode { get; set; } = "direct";

        /// <summary>
        /// True when this connection uses the local named pipe transport instead of TCP. Backed by
        /// <see cref="Mode"/> so the saved connection format is unchanged.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool UseNamedPipe
        {
            get => Mode == NamedPipeConnection.ConnectionMode;
            set => Mode = value ? NamedPipeConnection.ConnectionMode : "direct";
        }

        /// <summary>
        /// Short description of the endpoint this connection uses, for display in connection lists
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public string EndpointDescription => UseNamedPipe ? "named pipe (local)" : $"{Host}:{Port}";

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
            UseHTTPS = config?.UseHTTPS ?? false;
            Host = config?.Host ?? Certify.Shared.ServiceConfig.DEFAULT_LOCALHOST;
            Port = config?.Port ?? 9696;
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
