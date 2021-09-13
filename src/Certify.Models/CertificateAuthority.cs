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
        public static List<CertificateAuthority> CoreCertificateAuthorities = new List<CertificateAuthority> {
            new CertificateAuthority{
                Id="letsencrypt.org",
                Title ="Let's Encrypt",
                Description="Let's Encrypt is a free, automated, and open certificate authority. Certificates are valid for 90 days and can contain up to 100 domains/subdomains or wildcards.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl ="https://letsencrypt.org/",
                PrivacyPolicyUrl ="https://letsencrypt.org/privacy/",
                TermsAndConditionsUrl="https://letsencrypt.org/repository/",
                ProductionAPIEndpoint = "https://acme-v02.api.letsencrypt.org/directory",
                StagingAPIEndpoint = "https://acme-staging-v02.api.letsencrypt.org/directory",
                StatusUrl = "https://letsencrypt.status.io/",
                IsEnabled = true,
                IsCustom = false,
                SANLimit=100,
                RequiresEmailAddress = true,
                SupportedFeatures = new List<string>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString()
                },
                SupportedKeyTypes =new List<string>{
                    StandardKeyTypes.RSA256,
                    StandardKeyTypes.RSA256_3072,
                    StandardKeyTypes.RSA256_4096,
                    StandardKeyTypes.ECDSA256,
                    StandardKeyTypes.ECDSA384
                },
                DisabledIntermediates = new List<string>{
                    "48504E974C0DAC5B5CD476C8202274B24C8C7172" // old R3 chained to DST Root CA X3
                }
            },
             new CertificateAuthority{
                Id="buypass.com",
                Title ="Buypass Go SSL",
                Description="Buypass Go SSL is a free SSL certificate service from Buypass CA using the Buypass ACME API. Certificates are valid for 180 days and can contain up to 5 domains per certificate (wildcards are not available)",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl ="https://www.buypass.com/",
                PrivacyPolicyUrl ="https://www.buypass.com/about-buypass/privacy-policy",
                ProductionAPIEndpoint = "https://api.buypass.com/acme/directory",
                StagingAPIEndpoint = "https://api.test4.buypass.no/acme/directory",
                IsEnabled=true,
                IsCustom = false,
                SANLimit=5,
                RequiresEmailAddress = true,
                SupportedFeatures = new List<string>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString()
                },
                SupportedKeyTypes =new List<string>{
                    StandardKeyTypes.RSA256,
                    StandardKeyTypes.RSA256_3072,
                    StandardKeyTypes.RSA256_4096,
                    StandardKeyTypes.ECDSA256,
                    StandardKeyTypes.ECDSA384
                }
            },
               new CertificateAuthority{
                Id="zerossl.com",
                Title ="ZeroSSL",
                Description="ZeroSSL is a free certificate service from apilayer. Certificates are valid for 90 days and can contain multiple domains or wildcards.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl ="https://zerossl.com/",
                PrivacyPolicyUrl ="https://zerossl.com/privacy/",
                ProductionAPIEndpoint = "https://acme.zerossl.com/v2/DV90",
                StagingAPIEndpoint = null,
                IsEnabled=true,
                IsCustom = false,
                SANLimit = 100,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportedFeatures = new List<string>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString()
                },
                SupportedKeyTypes =new List<string>{
                    StandardKeyTypes.RSA256,
                    StandardKeyTypes.RSA256_3072,
                    StandardKeyTypes.ECDSA256,
                    StandardKeyTypes.ECDSA384
                },
                EabInstructions="To use ZeroSSL, Create a free account on ZeroSSL.com then navigate to Developer > EAB Credentials for ACME Clients > Generate. Save your EAB KID and EAB HMAC Key. Enter these in the Advanced tab (Add/Edit Account)."
            },
                new CertificateAuthority{
                Id="rsa.ssl.com",
                Title ="SSL.com (RSA)",
                Description="SSL.com offer free and paid certificate services. Free certificates are valid for 90 days and can contain a single domain plus www.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl ="https://ssl.com/",
                PrivacyPolicyUrl ="https://www.ssl.com/privacy-policy/",
                ProductionAPIEndpoint = "https://acme.ssl.com/sslcom-dv-rsa",
                StagingAPIEndpoint = null,
                IsEnabled=true,
                IsCustom = false,
                SANLimit = 2,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportedFeatures = new List<string>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE_PLUS_WWW.ToString()
                },
                SupportedKeyTypes =new List<string>{
                    StandardKeyTypes.RSA256,
                    StandardKeyTypes.RSA256_3072,
                    StandardKeyTypes.RSA256_4096
                },
                EabInstructions="To use SSL.com, Create a free account on SSL.com (https://secure.ssl.com/users/new) then navigate to Dashboard > Developers and Integrations > API and ACME Credentials > Add Credential. Save your Account/ACME Key and HMAC Key. Enter these in the Advanced tab (Add/Edit Account)."
            }
                /* // SSL.com ECDSA currently gives key errors
            new CertificateAuthority{
                Id="ecdsa.ssl.com",
                Title ="SSL.com (ECDSA)",
                Description="SSL.com offer free and paid certificate services. Free certificates are valid for 90 days and can contain a single domain plus www.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl ="https://ssl.com/",
                PrivacyPolicyUrl ="https://www.ssl.com/privacy-policy/",
                ProductionAPIEndpoint = "https://acme.ssl.com/sslcom-dv-ecc",
                StagingAPIEndpoint = null,
                IsEnabled=true,
                IsCustom = false,
                SANLimit = 2,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportedFeatures = new List<string>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE_PLUS_WWW.ToString()
                },
                SupportedKeyTypes =new List<string>{
                    StandardKeyTypes.ECDSA256,
                    StandardKeyTypes.ECDSA384,
                    StandardKeyTypes.ECDSA521
                },
                EabInstructions="To use SSL.com, Create a free account on SSL.com (https://secure.ssl.com/users/new) then navigate to Dashboard > Developers and Integrations > API and ACME Credentials > Add Credential. Save your Account/ACME Key and HMAC Key. Enter these in the Advanced tab (Add/Edit Account)."
            }*/
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
        public bool RequiresEmailAddress { get; set; }
        public bool RequiresExternalAccountBinding { get; set; } = false;
        public bool AllowUntrustedTls { get; set; } = false;
        public bool AllowInternalHostnames { get; set; } = false;
        public string EabInstructions { get; set; }
        public List<string> SupportedKeyTypes { get; set; }

        /// <summary>
        /// If set, lists intermediate cert for this CA which should be disabled or removed
        /// </summary>
        public List<string> DisabledIntermediates { get; set; }

    }
}
