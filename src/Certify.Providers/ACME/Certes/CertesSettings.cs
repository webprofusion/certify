namespace Certify.Providers.ACME.Certes
{
    /// <summary>
    /// Certes Provider settings for serialization
    /// </summary>
    public class CertesSettings
    {
        public string AccountEmail { get; set; }
        public string AccountUri { get; set; }
        public string AccountKey { get; set; }
    }
}
