using System.Collections.Generic;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{
    internal sealed class GlobalSign
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "acme.atlas.globalsign.com",
                Title = "GlobalSign Atlas",
                Description = "The (commercial) GlobalSign ACME service issues CA/Browser Forum-compliant publicly trusted TLS certificates, as well as non-public Intranet certificates.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://support.globalsign.com/atlas/acme/acme-overview",
                PrivacyPolicyUrl = "https://www.globalsign.com/en/repository/GlobalSign-Privacy-Policy.pdf",
                ProductionAPIEndpoint = "https://emea.acme.atlas.globalsign.com/directory",
                StagingAPIEndpoint = string.Empty,
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 100,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportsCachedValidations = true,  // GlobalSign Atlas supports cached validations for 365 days
                AllowInternalHostnames = true,
                SupportedFeatures = new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE_PLUS_WWW.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString()
                    },
                SupportedKeyTypes = new List<string>{
                        StandardKeyTypes.RSA256,
                        StandardKeyTypes.RSA256_3072,
                        StandardKeyTypes.RSA256_4096,
                        StandardKeyTypes.ECDSA256,
                        StandardKeyTypes.ECDSA384,
                        StandardKeyTypes.ECDSA521
                    },
                EabInstructions = "See https://support.globalsign.com/atlas/acme/acme-overview"
            };
        }
    }
}
