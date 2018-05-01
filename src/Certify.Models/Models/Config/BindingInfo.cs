namespace Certify.Models
{
    public class BindingInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Host { get; set; }
        public string IP { get; set; }
        public int Port { get; set; }
        public string Protocol { get; set; }

        public string PhysicalPath { get; set; }
        public bool IsHTTPS { get; set; }

        public bool HasCertificate { get; set; }
        public string CertificateHash { get; set; }

        public bool IsEnabled { get; set; }
    }
}
