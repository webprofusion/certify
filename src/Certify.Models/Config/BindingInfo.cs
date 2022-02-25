namespace Certify.Models
{
    public class BindingInfo
    {
        public string ServerType { get; set; }
        public string SiteId { get; set; }
        public string SiteName { get; set; }

        public string Protocol { get; set; }
        public string Host { get; set; }
        public string IP { get; set; }
        public int Port { get; set; }

        public string PhysicalPath { get; set; }
        public bool IsHTTPS { get; set; }

        public bool HasCertificate { get; set; }
        public string CertificateHash { get; set; }
        public byte[] CertificateHashBytes { get; set; }
        public string CertificateStore { get; set; }

        public bool IsSNIEnabled { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsFtpSite { get; set; } = false;

        public override string ToString()
        {
            return string.IsNullOrEmpty(SiteName) ? $"{Protocol}://{Host}:{Port}" : SiteName;
        }
    }
}
