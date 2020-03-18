using System.Collections.Generic;
using System.Linq;

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
    }

    public class CertificateAuthority
    {
        public static List<CertificateAuthority> CertificateAuthorities = new List<CertificateAuthority> {
            new CertificateAuthority{
                Id="letsencrypt.org",
                Title ="Let's Encrypt",
                Description="Let's Encrypt is a free, automated, and open certificate authority. Certificates are valid for 90 days and can contain up to 100 domains/subdomains or wildcards.",
                APIType = CertAuthorityAPIType.ACME_V2,
                WebsiteUrl ="https://letsencrypt.org/",
                PrivacyPolicyUrl ="https://letsencrypt.org/privacy/",
                TermsAndConditionsUrl="https://letsencrypt.org/repository/",
                ProductionAPIEndpoint = "https://acme-v02.api.letsencrypt.org/directory",
                StagingAPIEndpoint = "https://acme-staging-v02.api.letsencrypt.org/directory",
                IsEnabled = true,
                SANLimit=100,
                SupportedRequests = new List<CertAuthoritySupportedRequests>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE,
                    CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN,
                    CertAuthoritySupportedRequests.DOMAIN_WILDCARD
                }
            },
             new CertificateAuthority{
                Id="buypass.com",
                Title ="Buypass Go SSL",
                Description="Buypass Go SSL is a free SSL certificate service from Buypass CA using the Buypass ACME API. Certificates are valid for 180 days and can be for one domain wildcard or contain 1 (one) domain and one www. subdomain (optional).",
                APIType = CertAuthorityAPIType.ACME_V2,
                WebsiteUrl ="https://www.buypass.com/",
                PrivacyPolicyUrl ="https://www.buypass.com/about-buypass/privacy-policy",
                ProductionAPIEndpoint = "https://api.buypass.com/acme/directory",
                StagingAPIEndpoint = "https://api.test4.buypass.no/acme/directory",
                IsEnabled=true,
                SANLimit=1,
                SupportedRequests = new List<CertAuthoritySupportedRequests>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE,
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE_PLUS_WWW,
                    CertAuthoritySupportedRequests.DOMAIN_WILDCARD
                }
            }
        };

        public static CertificateAuthority GetCertificateAuthority(string id)
        {
            return CertificateAuthorities.FirstOrDefault(c => c.Id == id);
        }

        public string Id { get; set; }
        public CertAuthorityAPIType APIType { get; set; }
        public List<CertAuthoritySupportedRequests> SupportedRequests { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string WebsiteUrl { get; set; }
        public string PrivacyPolicyUrl { get; set; }
        public string TermsAndConditionsUrl { get; set; }
        public string ProductionAPIEndpoint { get; set; }
        public string StagingAPIEndpoint { get; set; }
        public bool IsEnabled { get; set; }
        public int SANLimit { get; set; }
    }
}
