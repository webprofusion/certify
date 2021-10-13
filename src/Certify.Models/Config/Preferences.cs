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

    public static class RenewalIntervalModes
    {
        /// <summary>
        /// Renew certs N days after last renewal
        /// </summary>
        public static string DaysAfterLastRenewal = "DaysAfterLastRenewal";

        /// <summary>
        /// Renew certs N days before expiry
        /// </summary>
        public static string DaysBeforeExpiry = "DaysBeforeExpiry";
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

        public string RenewalIntervalMode { get; set; } = RenewalIntervalModes.DaysAfterLastRenewal;

        public int RenewalIntervalDays { get; set; } = 0;

        public int MaxRenewalRequests { get; set; } = 0;

        public string InstanceId { get; set; }

        public bool IsInstanceRegistered { get; set; } = false;

        public string Language { get; set; }

        public bool EnableHttpChallengeServer { get; set; } = true;

        public bool EnableCertificateCleanup { get; set; } = true;

        public CertificateCleanupMode? CertificateCleanupMode { get; set; }

        public string DefaultCertificateStore { get; set; }

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

        /// <summary>
        /// If true, will allow plugins to load from appdata
        /// </summary>
        public bool IncludeExternalPlugins { get; set; }

        public string[] FeatureFlags { get; set; }


        /// <summary>
        /// Server to use for Ntp time diagnostics
        /// </summary>
        public string NtpServer { get; set; }

        /// <summary>
        /// If enabled, certificate manager plugins are used to check for ACME certificates managed outside of Certify on same machine
        /// </summary>
        public bool EnableExternalCertManagers { get; set; }
    }

    public static class FeatureFlags
    {
        /// <summary>
        /// Enable import/export UI
        /// </summary>
        public static string IMPORT_EXPORT = "IMPORT_EXPORT";

        /// <summary>
        /// Enable options for PFX pwd (global and per item credentials)
        /// </summary>
        public static string PRIVKEY_PWD = "PRIVKEY_PWD";

        /// <summary>
        /// Enable editor for custom Certificate Authorities
        /// </summary>
        public static string CA_EDITOR = "CA_EDITOR";

        /// <summary>
        /// Enable options for auto CA Failover
        /// </summary>
        public static string CA_FAILOVER = "CA_FAILOVER";

        /// <summary>
        /// Enable options for external cert managers
        /// </summary>
        public static string EXTERNAL_CERT_MANAGERS = "EXTERNAL_CERT_MANAGERS";


        /// <summary>
        /// Enable server connection UI
        /// </summary>
        public static string SERVER_CONNECTIONS = "SERVER_CONNECTIONS";
    }
}
