using System.Collections.Generic;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{
    internal sealed class Actalis
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "actalis.com",
                Title = "Actalis",
                Description = "The Actalis ACME service offers free and paid certificate services.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://www.actalis.com/",
                PrivacyPolicyUrl = "https://www.actalis.it/acme/terms",
                ProductionAPIEndpoint = "https://acme-api.actalis.com/acme/directory",
                StagingAPIEndpoint = string.Empty,
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 100,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportsCachedValidations = true,
                AllowInternalHostnames = true,
                SupportedFeatures = new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE_PLUS_WWW.ToString(),
                    },
                SupportedKeyTypes = new List<string>{
                        StandardKeyTypes.RSA256,
                        StandardKeyTypes.RSA256_3072,
                        StandardKeyTypes.RSA256_4096,
                        StandardKeyTypes.ECDSA256,
                        StandardKeyTypes.ECDSA384,
                        StandardKeyTypes.ECDSA521
                    },
                EabInstructions = "See https://guide.actalis.com/ssl/activation/acme"
            };
        }
    }
}
