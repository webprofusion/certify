using System.Collections.Generic;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{

    internal sealed class Martini
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "martinisecurity.com",
                Title = "Martini Security (STIR/SHAKEN)",
                Description = "Restore trust in phone calls by verifying caller metadata with STIR/SHAKEN. ACME-enabled issuance for SITR/SHAKEN certificates.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://www.martinisecurity.com/",
                PrivacyPolicyUrl = "https://www.martinisecurity.com/privacy_policy",
                TermsAndConditionsUrl = "https://www.martinisecurity.com/repository/MartiniSecuritySHAKENSubscriberAgreement-v1.1.pdf",
                ProductionAPIEndpoint = "https://wfe.prod.martinisecurity.com/v2/acme/directory",
                StagingAPIEndpoint = "https://wfe.dev.martinisecurity.com/v2/acme/directory",
                IsEnabled = true,
                IsCustom = false,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                SupportsCachedValidations = false,
                SupportedFeatures = new List<string>{
                    CertAuthoritySupportedRequests.TNAUTHLIST.ToString(),
                    CertAuthoritySupportedRequests.OPTIONAL_LIFETIME_DAYS.ToString()
                },
                SupportedKeyTypes = new List<string>{
                    StandardKeyTypes.ECDSA256
                }
            };
        }
    }
}
