using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{
    internal sealed class PKIaaS
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "pkiaas.io",
                Title = "PKIaaS.io",
                Description = "PKIaaS.io - Hosted private CA service.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://www.pkiaas.io/",
                ProductionAPIEndpoint = "https://acme-v02-api.pkiaas.io/directory",
                StagingAPIEndpoint = string.Empty,
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 100,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                AllowInternalHostnames = true,
                SupportedFeatures = [
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE_PLUS_WWW.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString()
                    ],
                SupportedKeyTypes = [
                        StandardKeyTypes.RSA256,
                        StandardKeyTypes.RSA256_3072,
                        StandardKeyTypes.RSA256_4096,
                        StandardKeyTypes.ECDSA256,
                        StandardKeyTypes.ECDSA384,
                        StandardKeyTypes.ECDSA521
                    ],
                EabInstructions = "See https://www.pkiaas.io/docs/acme/acme-clients/certify-the-web"
            };
        }
    }
}
