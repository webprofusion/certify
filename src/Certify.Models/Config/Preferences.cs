namespace Certify.Models
{
    public enum CertificateCleanupMode
    {
        None = 0,
        /// <summary>
        /// Clean up [Certify] expired certificates
        /// </summary>
        AfterExpiry = 1,
        /// <summary>
        /// Clean up [Certify] renewed certificates
        /// </summary>
        AfterRenewal = 2,
        /// <summary>
        /// Clean up all [Certify] certificates not currently managed
        /// </summary>
        FullCleanup = 3
    }

    /// <summary>
    /// Note the settings specified here are mapped to CoreAppSettings
    /// </summary>
    public class Preferences : BindableBase
    {
        public bool EnableAppTelematics { get; set; } = true;

        public bool IgnoreStoppedSites { get; set; } = false;

        public bool EnableValidationProxyAPI { get; set; } = true;

        public bool EnableEFS { get; set; } = false;

        public bool EnableDNSValidationChecks { get; set; } = false;

        public int RenewalIntervalDays { get; set; } = 0;

        public int MaxRenewalRequests { get; set; } = 0;

        public string InstanceId { get; set; }

        public bool IsInstanceRegistered { get; set; } = false;

        public string Language { get; set; }

        public bool EnableHttpChallengeServer { get; set; } = true;

        public bool EnableCertificateCleanup { get; set; } = true;

        public CertificateCleanupMode? CertificateCleanupMode { get; set; }

        public bool EnableStatusReporting { get; set; } = true;

        /// <summary>
        /// ID of default CA
        /// </summary>
        public string DefaultCertificateAuthority { get; set; }

        /// <summary>
        /// Id of default credentials (password) to use for private keys etc
        /// </summary>
        public string DefaultKeyCredentials { get; set; }

        /// <summary>
        /// If true, the app will decide which Certificate Authority to choose from the list of supported providers.
        /// The preferred provider will be chosen first, with fallback to any other supported (and configured) providers if a failure occurs.
        /// </summary>
        public bool EnableAutomaticCAFailover { get; set; }
    }
}
