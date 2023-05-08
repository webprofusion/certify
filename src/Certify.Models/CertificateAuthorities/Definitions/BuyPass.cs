using System.Collections.Generic;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{
    internal sealed class BuyPass
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "buypass.com",
                Title = "Buypass Go SSL",
                Description = "Buypass Go SSL is a free SSL certificate service from Buypass CA using the Buypass ACME API. Certificates are valid for 180 days and can contain up to 5 domains per certificate (wildcards are not available)",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://www.buypass.com/",
                PrivacyPolicyUrl = "https://www.buypass.com/about-buypass/privacy-policy",
                ProductionAPIEndpoint = "https://api.buypass.com/acme/directory",
                StagingAPIEndpoint = "https://api.test4.buypass.no/acme/directory",
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 5,
                StandardExpiryDays = 180,
                RequiresEmailAddress = true,
                SupportsCachedValidations = true,
                SupportedFeatures = new List<string> {
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString()
                },
                SupportedKeyTypes = new List<string> {
                    StandardKeyTypes.RSA256,
                    StandardKeyTypes.RSA256_3072,
                    StandardKeyTypes.RSA256_4096,
                    StandardKeyTypes.ECDSA256,
                    StandardKeyTypes.ECDSA384
                }
            };
        }
    }
}
