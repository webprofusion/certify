using Newtonsoft.Json;

namespace Certify.Shared
{
    public class ServiceConfig
    {
        public bool UseHTTPS { get; set; }
#if DEBUG
        public int Port { get; set; } = 9695;
#else
        public int Port { get; set; } = 9696;
#endif
        public string Host { get; set; } = "localhost";

        public int HttpChallengeServerPort { get; set; } = 80;

        public string LogLevel { get; set; } = "information";

        public string ServiceFaultMsg { get; set; } = string.Empty;

        public string PowershellExecutionPolicy { get; set; } = "Unrestricted";

        /// <summary>
        /// windows;jwt;
        /// </summary>
        public string AuthenticationModes { get; set; } = "windows";

        [JsonIgnore]
        public ConfigStatus ConfigStatus { get; set; }

        /// <summary>
        /// If true, allow service to negotitate it's own port and update required config.
        /// </summary>
        public bool EnableAutoPortNegotiation { get; set; }
    }

    public enum ConfigStatus
    {
        New = 0,
        NotModified = 1,
        Updated = 2,
        DefaultFailed = 4
    }
}
