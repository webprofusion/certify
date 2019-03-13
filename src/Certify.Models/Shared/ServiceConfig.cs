namespace Certify.Shared
{
    public class ServiceConfig
    {
        public bool UseHTTPS { get; set; } = true;
#if DEBUG
        public int Port { get; set; } = 9695;
#else
        public int Port { get; set; } = 9696;
#endif
        public string Host { get; set; } = "localhost";

        public int HttpChallengeServerPort { get; set; } = 80;

        public string LogLevel { get; set; } = "information";

        public string ServiceFaultMsg { get; set; }

        public string PowershellExecutionPolicy { get; set; } = "Unrestricted";
    }
}
