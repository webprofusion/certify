using System.Collections.Generic;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{

    internal sealed class LetsEncrypt
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "letsencrypt.org",
                Title = "Let's Encrypt",
                Description = "Let's Encrypt is a free, automated, and open certificate authority. Certificates are valid for 90 days and can contain up to 100 domains/subdomains or wildcards.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://letsencrypt.org/",
                PrivacyPolicyUrl = "https://letsencrypt.org/privacy/",
                TermsAndConditionsUrl = "https://letsencrypt.org/repository/",
                ProductionAPIEndpoint = "https://acme-v02.api.letsencrypt.org/directory",
                StagingAPIEndpoint = "https://acme-staging-v02.api.letsencrypt.org/directory",
                StatusUrl = "https://letsencrypt.status.io/",
                DefaultPreferredChain = "ISRG Root X1",
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 100,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                SupportsCachedValidations = true,
                SupportedFeatures = new List<string>{
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString()
                },
                SupportedKeyTypes = new List<string>{
                    StandardKeyTypes.RSA256,
                    StandardKeyTypes.RSA256_3072,
                    StandardKeyTypes.RSA256_4096,
                    StandardKeyTypes.ECDSA256,
                    StandardKeyTypes.ECDSA384
                }
            };
        }

        public static List<ChainOption> GetChainOptions()
        {
            return new List<ChainOption>
            {
                new ChainOption {
                    Id="letsencrypt-rsa-modern",
                    Name="Modern Chain (ISRG Root X1)",
                    Issuer="ISRG Root X1",
                    ChainGroup="RSA",
                    Description="Switch to this chain in order to serve the shorter chain for modern operating systems which trust ISRG Root X1.",
                    Actions= new List<ChainAction>
                    {
                        new ChainAction (ChainActions.Delete, "933c6ddee95c9c41a40f9f50493d82be03ad87bf", "Remove ISRG Root X1 cross signed by DST Root CA X3"),
                        new ChainAction (ChainActions.StoreCARoot, "cabd2a79a1076a31f21d253635cb039d4329a5e8", "Add ISRG Root X1 self signed")
                    }
                },
                new ChainOption
                {
                    Id = "letsencrypt-rsa-legacy",
                    Name = "Legacy Chain (DST Root CA X3)",
                    Issuer = "DST Root CA X3",
                    ChainGroup = "RSA",
                    Description = "Switch to this chain in order to serve the longer (more compatible) chain to support operating systems which don't trust ISRG Root X1.",
                    Actions = new List<ChainAction>
                    {
                        new ChainAction (ChainActions.StoreCAIntermediate, "933c6ddee95c9c41a40f9f50493d82be03ad87bf", "Add ISRG Root X1 cross signed by DST Root CA X3")
                    }
                }
            };
        }
    }
}
