using System.Collections.Generic;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{
    internal sealed class ZeroSSL
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "zerossl.com",
                Title = "ZeroSSL",
                Description = "ZeroSSL is a free certificate service from apilayer. Certificates are valid for 90 days and can contain multiple domains or wildcards.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://zerossl.com/",
                PrivacyPolicyUrl = "https://zerossl.com/privacy/",
                ProductionAPIEndpoint = "https://acme.zerossl.com/v2/DV90",
                StagingAPIEndpoint = string.Empty,
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 100,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportsCachedValidations = false,
                SupportedFeatures = new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString()
                    },
                SupportedKeyTypes = new List<string>{
                        StandardKeyTypes.RSA256,
                        StandardKeyTypes.RSA256_3072,
                        StandardKeyTypes.ECDSA256,
                        StandardKeyTypes.ECDSA384
                    },
                EabInstructions = "To use ZeroSSL, Create a free account on ZeroSSL.com then navigate to Developer > EAB Credentials for ACME Clients > Generate. Save your EAB KID and EAB HMAC Key. Enter these in the Advanced tab (Add/Edit Account)."
            };

        }
    }
}
