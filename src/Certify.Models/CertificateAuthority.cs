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
        DOMAIN_MULTIPLE_SAN,
        DOMAIN_WILDCARD
    }

    public class CertificateAuthority
    {
        public static List<CertificateAuthority> CertificateAuthorities = new List<CertificateAuthority> {
            new CertificateAuthority{
                Id="letsencrypt.org",
                Title ="Let's Encrypt",
                APIType = CertAuthorityAPIType.ACME_V2,
                WebsiteUrl ="https://letsencrypt.org/",
                PrivacyPolicyUrl ="https://letsencrypt.org/privacy/",
                TermsAndConditionsUrl="https://letsencrypt.org/repository/",
                ProductionAPIEndpoint = "https://acme-v02.api.letsencrypt.org/directory",
                StagingAPIEndpoint = "https://acme-staging-v02.api.letsencrypt.org/directory",
                IsEnabled = true,
                SupportedRequests = new List<CertAuthoritySupportedRequests>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE,
                    CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN,
                    CertAuthoritySupportedRequests.DOMAIN_WILDCARD
                }
            },
             new CertificateAuthority{
                Id="buypass.com",
                Title ="Let's Encrypt",
                APIType = CertAuthorityAPIType.ACME_V1,
                WebsiteUrl ="https://www.buypass.com/",
                PrivacyPolicyUrl ="https://www.buypass.com/about-buypass/privacy-policy",
                ProductionAPIEndpoint = "https://api.buypass.com/acme/directory",
                StagingAPIEndpoint = null,
                IsEnabled=false,
                 SupportedRequests = new List<CertAuthoritySupportedRequests>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE
                }
            }
        };

        public string Id { get; set; }
        public CertAuthorityAPIType APIType { get; set; }
        public List<CertAuthoritySupportedRequests> SupportedRequests { get; set; }
        public string Title { get; set; }
        public string Decription { get; set; }
        public string WebsiteUrl { get; set; }
        public string PrivacyPolicyUrl { get; set; }
        public string TermsAndConditionsUrl { get; set; }
        public string ProductionAPIEndpoint { get; set; }
        public string StagingAPIEndpoint { get; set; }
        public bool IsEnabled { get; set; }
    }
}
