namespace Certify.Models.Config
{
    public class CertificateManagerPreference
    {
        public string Id { get; set; }
        public bool IsEnabled { get; set; }
        public string ConfigPath { get; set; }
        public string LogPath { get; set; }
    }
}
