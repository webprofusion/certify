namespace Certify.Models
{
    public class BindingInfo
    {
        public string ServerType { get; set; } = string.Empty;
        public string SiteId { get; set; } = string.Empty;
        public string SiteName { get; set; } = string.Empty;

        public string Protocol { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string IP { get; set; } = string.Empty;
        public int Port { get; set; }

        public string? PhysicalPath { get; set; }
        public bool IsHTTPS { get; set; }

        public bool HasCertificate { get; set; }
        public string? CertificateHash { get; set; }
        public byte[]? CertificateHashBytes { get; set; }
        public string? CertificateStore { get; set; }

        public bool IsSNIEnabled { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsFtpSite { get; set; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(SiteName) ? $"{Protocol}://{Host}:{Port}" : SiteName;
        }
    }
}
