using System.Collections.Generic;

namespace Certify.Models
{
    public enum CertAuthorityAPIType
    {
        NONE,
        ACME_V1,
        ACME_V2
    }

    public enum CertAuthoritySupportedRequests
    {
        DOMAIN_SINGLE,
        DOMAIN_SINGLE_PLUS_WWW,
        DOMAIN_MULTIPLE_SAN,
        DOMAIN_WILDCARD
    }

    public static class StandardCertAuthorities
    {
        public static string LETS_ENCRYPT = "letsencrypt.org";
        public static string BUYPASS = "buypass.com";
        public static string ZEROSSL = "zerossl.com";
    }

    public static class StandardKeyTypes
    {
        /// <summary>
        /// Support all key types
        /// </summary>
        public static string ALL = "ALL";

        /// <summary>
        /// RSA256 with key size 2048
        /// </summary>
        /// 
        public static string RSA256 = "RS256";
        /// <summary>
        /// RSA256 with key size 3072
        /// </summary>
        public static string RSA256_3072 = "RS256_3072";

        /// <summary>
        /// RSA256 with key size 4096
        /// </summary>
        public static string RSA256_4096 = "RS256_4096";

        /// <summary>
        /// ECDSA 256
        /// </summary>
        public static string ECDSA256 = "ECDSA256";

        /// <summary>
        /// ECDSA 384
        /// </summary>
        public static string ECDSA384 = "ECDSA384";

        /// <summary>
        /// ECDSA 521
        /// </summary>
        public static string ECDSA521 = "ECDSA521";
    }

    public class CertificateAuthority
    {
        public static readonly List<CertificateAuthority> CoreCertificateAuthorities = new List<CertificateAuthority>
        {
            CertificateAuthorities.Definitions.LetsEncrypt.GetDefinition(),
            CertificateAuthorities.Definitions.BuyPass.GetDefinition(),
            CertificateAuthorities.Definitions.ZeroSSL.GetDefinition(),
            CertificateAuthorities.Definitions.SSLDotcom.GetDefinition(),
            CertificateAuthorities.Definitions.Google.GetDefinition()
        };

        public string Id { get; set; }
        public string APIType { get; set; } = CertAuthorityAPIType.ACME_V2.ToString();
        public List<string> SupportedFeatures { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string WebsiteUrl { get; set; }
        public string PrivacyPolicyUrl { get; set; }
        public string TermsAndConditionsUrl { get; set; }
        public string StatusUrl { get; set; }
        public string ProductionAPIEndpoint { get; set; }
        public string StagingAPIEndpoint { get; set; }

        public bool IsEnabled { get; set; }
        public bool IsCustom { get; set; } = true;
        public int SANLimit { get; set; }

        public int StandardExpiryDays { get; set; }
        public bool RequiresEmailAddress { get; set; }
        public bool RequiresExternalAccountBinding { get; set; } = false;
        public bool AllowUntrustedTls { get; set; } = false;
        public bool AllowInternalHostnames { get; set; } = false;
        public bool SupportsCachedValidations { get; set; } = true;
        public string EabInstructions { get; set; }
        public List<string> SupportedKeyTypes { get; set; }

        /// <summary>
        /// If set, lists intermediate cert for this CA which should be disabled or removed
        /// </summary>
        public List<string> DisabledIntermediates { get; set; }

        /// <summary>
        /// Optional list of Trusted Root certificates to install for chain building and verification
        /// </summary>
        public Dictionary<string, string> TrustedRoots { get; set; }

        /// <summary>
        /// Optional list of Intermediate certificates to install for chain building and verification
        /// </summary>
        public Dictionary<string, string> Intermediates { get; set; }

    }
}
