using System.Collections.Generic;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{
    internal sealed class SectigoDV
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "sectigo-dv",
                Title = "Sectigo.com (DV certificates)",
                Description = "Sectigo offer paid ACME services to their customers. see https://sectigo.com for more details",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://sectigo.com/resource-library/sectigos-acme-automation",
                PrivacyPolicyUrl = "https://sectigo.com/privacy-policy/",
                ProductionAPIEndpoint = "https://acme.sectigo.com/v2/DV",
                StagingAPIEndpoint = string.Empty,
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 100,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportsCachedValidations = true,
                SupportedFeatures = new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString(),
                        CertAuthoritySupportedRequests.OPTIONAL_LIFETIME_DAYS.ToString()
                    },
                SupportedKeyTypes = new List<string>{
                        StandardKeyTypes.RSA256_3072,
                        StandardKeyTypes.RSA256_4096,
                        StandardKeyTypes.ECDSA256,
                        StandardKeyTypes.ECDSA384,
                        StandardKeyTypes.ECDSA521
                    },
                EabInstructions = "Use the Sectigo administration portal to acquire EAB credentials for use with an ACME client, then create your account within the app and provide the EAB credentials."
            };

        }
    }

    internal sealed class SectigoOV
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "sectigo-OV",
                Title = "Sectigo.com (OV certificates)",
                Description = "Sectigo offer paid ACME services to their customers. see https://sectigo.com for more details",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://sectigo.com/resource-library/sectigos-acme-automation",
                PrivacyPolicyUrl = "https://sectigo.com/privacy-policy/",
                ProductionAPIEndpoint = "https://acme.sectigo.com/v2/OV",
                StagingAPIEndpoint = string.Empty,
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 100,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportsCachedValidations = true,
                SupportedFeatures = new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString()
                    },
                SupportedKeyTypes = new List<string>{
                        StandardKeyTypes.RSA256_3072,
                        StandardKeyTypes.RSA256_4096,
                        StandardKeyTypes.ECDSA256,
                        StandardKeyTypes.ECDSA384,
                        StandardKeyTypes.ECDSA521
                    },
                EabInstructions = "Use the Sectigo administration portal to acquire EAB credentials for use with an ACME client, then create your account within the app and provide the EAB credentials."
            };

        }
    }

    internal sealed class SectigoEV
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "sectigo-ev",
                Title = "Sectigo.com (EV certificates)",
                Description = "Sectigo offer paid ACME services to their customers. see https://sectigo.com for more details",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://sectigo.com/resource-library/sectigos-acme-automation",
                PrivacyPolicyUrl = "https://sectigo.com/privacy-policy/",
                ProductionAPIEndpoint = "https://acme.sectigo.com/v2/EV",
                StagingAPIEndpoint = string.Empty,
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 100,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportsCachedValidations = true,
                SupportedFeatures = new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString()
                    },
                SupportedKeyTypes = new List<string>{
                        StandardKeyTypes.RSA256_3072,
                        StandardKeyTypes.RSA256_4096,
                        StandardKeyTypes.ECDSA256,
                        StandardKeyTypes.ECDSA384,
                        StandardKeyTypes.ECDSA521
                    },
                EabInstructions = "Use the Sectigo administration portal to acquire EAB credentials for use with an ACME client, then create your account within the app and provide the EAB credentials."
            };

        }
    }

    internal sealed class SectigoEnterprise
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "sectigo-enterprise",
                Title = "Sectigo.com (Enterprise certificates)",
                Description = "Sectigo offer paid ACME services to their customers. see https://sectigo.com for more details",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://sectigo.com/resource-library/sectigos-acme-automation",
                PrivacyPolicyUrl = "https://sectigo.com/privacy-policy/",
                ProductionAPIEndpoint = "https://acme.enterprise.sectigo.com/",
                StagingAPIEndpoint = string.Empty,
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 100,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                AllowInternalHostnames = true,
                SupportsCachedValidations = true,
                SupportedFeatures = new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString()
                    },
                SupportedKeyTypes = new List<string>{
                        StandardKeyTypes.RSA256_3072,
                        StandardKeyTypes.RSA256_4096,
                        StandardKeyTypes.ECDSA256,
                        StandardKeyTypes.ECDSA384,
                        StandardKeyTypes.ECDSA521
                    },
                EabInstructions = "Use the Sectigo administration portal to acquire EAB credentials for use with an ACME client, then create your account within the app and provide the EAB credentials."
            };

        }
    }
}
