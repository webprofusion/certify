namespace Certify.Models.Config
{
    public class CertificateManagerPreference
    {
        public string Id { get; set; } = default!;
        public bool IsEnabled { get; set; } = default!;
        public string ConfigPath { get; set; } = default!;
        public string LogPath { get; set; } = default!;
    }
}
