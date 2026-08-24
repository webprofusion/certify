using Newtonsoft.Json;

namespace Certify.Shared
{
    public class ServiceConfig
    {
        public const string DEFAULT_LOCALHOST = "127.0.0.2";
        public bool UseHTTPS { get; set; }
#if DEBUG
        public int Port { get; set; } = 9695;
#else
        public int Port { get; set; } = 9696;
#endif
        public string? Host { get; set; } = DEFAULT_LOCALHOST;

        /// <summary>
        /// Which transport the service API listens on: "http" (default) or "namedpipe". Exactly one
        /// endpoint is published, so selecting the named pipe means http is not exposed at all and
        /// <see cref="Host"/>/<see cref="Port"/>/<see cref="UseHTTPS"/> no longer apply.
        /// Named pipe is Windows only.
        /// </summary>
        public string? Transport { get; set; } = NamedPipeConnection.HttpMode;

        public int HttpChallengeServerPort { get; set; } = 80;

        public string? LogLevel { get; set; } = "information";

        public string? ServiceFaultMsg { get; set; } = string.Empty;

        public string PowershellExecutionPolicy { get; set; } = "Unrestricted";

        public bool PreferModernPowershell { get; set; }

        public string[] CustomPowerShellPaths { get; set; } = [];

        /// <summary>
        /// windows;jwt;
        /// </summary>
        public string? AuthenticationModes { get; set; } = "windows";

        [JsonIgnore]
        public ConfigStatus ConfigStatus { get; set; }

        /// <summary>
        /// If true, allow service to negotitate it's own port and update required config.
        /// </summary>
        public bool EnableAutoPortNegotiation { get; set; }

        public string ManagementServerHubAPI { get; set; } = string.Empty;
        public string ManagementServerHubEndpoint { get; set; } = string.Empty;
        public string HubAssignedInstanceId { get; set; } = string.Empty;
        public bool IsManagementHub { get; set; }
    }

    public enum ConfigStatus
    {
        New = 0,
        NotModified = 1,
        Updated = 2,
        DefaultFailed = 4
    }
}
