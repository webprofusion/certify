using System.Collections.Generic;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{
    internal sealed class SSLDotcom
    {
        public static CertificateAuthority GetDefinition()
        {
            return new CertificateAuthority
            {
                Id = "rsa.ssl.com",
                Title = "SSL.com (DV RSA)",
                Description = "SSL.com offer free and paid certificate services. Free certificates are valid for 90 days and can contain a single domain plus www.",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://ssl.com/",
                PrivacyPolicyUrl = "https://www.ssl.com/privacy-policy/",
                ProductionAPIEndpoint = "https://acme.ssl.com/sslcom-dv-rsa",
                StagingAPIEndpoint = string.Empty,
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 2,
                StandardExpiryDays = 90,
                RequiresEmailAddress = true,
                RequiresExternalAccountBinding = true,
                SupportsCachedValidations = true,
                SupportedFeatures = new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE_PLUS_WWW.ToString()
                    },
                SupportedKeyTypes = new List<string>{
                        StandardKeyTypes.RSA256,
                        StandardKeyTypes.RSA256_3072,
                        StandardKeyTypes.RSA256_4096
                    },
                EabInstructions = "To use SSL.com, Create a free account on SSL.com (https://secure.ssl.com/users/new) then navigate to Dashboard > Developers and Integrations > API and ACME Credentials > Add Credential. Save your Account/ACME Key and HMAC Key. Enter these in the Advanced tab (Add/Edit Account)."
            };
            /* // SSL.com ECDSA currently gives key errors
	        new CertificateAuthority{
	            Id="ecdsa.ssl.com",
	            Title ="SSL.com (ECDSA)",
	            Description="SSL.com offer free and paid certificate services. Free certificates are valid for 90 days and can contain a single domain plus www.",
	            APIType = CertAuthorityAPIType.ACME_V2.ToString(),
	            WebsiteUrl ="https://ssl.com/",
	            PrivacyPolicyUrl ="https://www.ssl.com/privacy-policy/",
	            ProductionAPIEndpoint = "https://acme.ssl.com/sslcom-dv-ecc",
	            StagingAPIEndpoint = null,
	            IsEnabled=true,
	            IsCustom = false,
	            SANLimit = 2,
	            RequiresEmailAddress = true,
	            RequiresExternalAccountBinding = true,
	            SupportedFeatures = new List<string>{
	                CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
	                CertAuthoritySupportedRequests.DOMAIN_SINGLE_PLUS_WWW.ToString()
	            },
	            SupportedKeyTypes =new List<string>{
	                StandardKeyTypes.ECDSA256,
	                StandardKeyTypes.ECDSA384,
	                StandardKeyTypes.ECDSA521
	            },
	            EabInstructions="To use SSL.com, Create a free account on SSL.com (https://secure.ssl.com/users/new) then navigate to Dashboard > Developers and Integrations > API and ACME Credentials > Add Credential. Save your Account/ACME Key and HMAC Key. Enter these in the Advanced tab (Add/Edit Account)."
	        }
        */
        }
    }
}
