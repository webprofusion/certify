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
        /// <summary>
        /// CA supports single domain certificates
        /// </summary>
        DOMAIN_SINGLE,
        /// <summary>
        /// CA supports single domain + www certificates
        /// </summary>
        DOMAIN_SINGLE_PLUS_WWW,
        /// <summary>
        /// CA support certificates with multiple SAN entries
        /// </summary>
        DOMAIN_MULTIPLE_SAN,
        /// <summary>
        /// CA supports wildcard certificates
        /// </summary>
        DOMAIN_WILDCARD,
        /// <summary>
        /// CA supports single IP address certificates
        /// </summary>
        IP_SINGLE,
        /// <summary>
        /// CA supports multiple IP address certificates
        /// </summary>
        IP_MULTIPLE,
        /// <summary>
        /// CA supports TnAuthList certificates
        /// </summary>
        TNAUTHLIST,
        /// <summary>
        /// CA supports optional lifetime/expiry days
        /// </summary>
        OPTIONAL_LIFETIME_DAYS
    }

    public static class StandardCertAuthorities
    {
        public const string LETS_ENCRYPT = "letsencrypt.org";
        public const string BUYPASS = "buypass.com";
        public const string ZEROSSL = "zerossl.com";
    }

    public static class StandardKeyTypes
    {
        /// <summary>
        /// Support all key types
        /// </summary>
        public const string ALL = "ALL";

        /// <summary>
        /// RSA256 with key size 2048
        /// </summary>
        /// 
        public const string RSA256 = "RS256";

        /// <summary>
        /// RSA256 with key size 3072
        /// </summary>
        public const string RSA256_3072 = "RS256_3072";

        /// <summary>
        /// RSA256 with key size 4096
        /// </summary>
        public const string RSA256_4096 = "RS256_4096";

        /// <summary>
        /// ECDSA 256
        /// </summary>
        public const string ECDSA256 = "ECDSA256";

        /// <summary>
        /// ECDSA 384
        /// </summary>
        public const string ECDSA384 = "ECDSA384";

        /// <summary>
        /// ECDSA 521
        /// </summary>
        public const string ECDSA521 = "ECDSA521";
    }

    public class CertificateAuthority
    {
        public static readonly List<CertificateAuthority> CoreCertificateAuthorities = new List<CertificateAuthority>
        {
            CertificateAuthorities.Definitions.LetsEncrypt.GetDefinition(),
            CertificateAuthorities.Definitions.BuyPass.GetDefinition(),
            CertificateAuthorities.Definitions.ZeroSSL.GetDefinition(),
            CertificateAuthorities.Definitions.SSLDotcom.GetDefinition(),
            CertificateAuthorities.Definitions.Google.GetDefinition(),
            CertificateAuthorities.Definitions.SectigoDV.GetDefinition(),
            CertificateAuthorities.Definitions.SectigoOV.GetDefinition(),
            CertificateAuthorities.Definitions.SectigoEV.GetDefinition(),
            CertificateAuthorities.Definitions.Martini.GetDefinition()
        };

        public string? Id { get; set; }
        public string APIType { get; set; } = CertAuthorityAPIType.ACME_V2.ToString();
        public List<string> SupportedFeatures { get; set; } = new();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string WebsiteUrl { get; set; } = string.Empty;
        public string PrivacyPolicyUrl { get; set; } = string.Empty;
        public string TermsAndConditionsUrl { get; set; } = string.Empty;
        public string StatusUrl { get; set; } = string.Empty;
        public string ProductionAPIEndpoint { get; set; } = string.Empty;
        public string StagingAPIEndpoint { get; set; } = string.Empty;
        public string DefaultPreferredChain { get; set; } = string.Empty;

        public bool IsEnabled { get; set; }
        public bool IsCustom { get; set; } = true;
        public int SANLimit { get; set; }

        public int StandardExpiryDays { get; set; }
        public bool RequiresEmailAddress { get; set; }
        public bool RequiresExternalAccountBinding { get; set; }
        public bool AllowUntrustedTls { get; set; }
        public bool AllowInternalHostnames { get; set; }
        public bool SupportsCachedValidations { get; set; } = true;
        public string EabInstructions { get; set; } = string.Empty;
        public List<string> SupportedKeyTypes { get; set; } = new();

        /// <summary>
        /// If set, lists intermediate cert for this CA which should be disabled or removed
        /// </summary>
        public List<string> DisabledIntermediates { get; set; } = new();

        /// <summary>
        /// Optional list of Trusted Root certificates to install for chain building and verification
        /// </summary>
        public Dictionary<string, string> TrustedRoots { get; set; } = new();

        /// <summary>
        /// Optional list of Intermediate certificates to install for chain building and verification
        /// </summary>
        public Dictionary<string, string> Intermediates { get; set; } = new();
    }
}
