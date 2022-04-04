using System.Collections.Generic;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions;

internal class Google
{
	public static CertificateAuthority GetDefinition()
	{
		var certs = GetKnownCertificates();

		return new CertificateAuthority
		{
			Id = "google",
			Title = "Google Cloud",
			Description =
				"The Google Cloud Certificate Manager is an ACME enabled certificate service. While in beta this service requires a sign up for preview. Certificates are valid for up-to 90 days and can contain multiple domains or wildcards.",
			APIType = CertAuthorityAPIType.ACME_V2.ToString(),
			WebsiteUrl = "https://cloud.google.com/public-certificate-authority/docs",
			PrivacyPolicyUrl = "https://pki.goog/repository/",
			ProductionAPIEndpoint = "https://dv.acme-v02.api.pki.goog/directory",
			StagingAPIEndpoint = "https://dv.acme-v02.test-api.pki.goog/directory",
			IsEnabled = true,
			IsCustom = false,
			SANLimit = 100,
			StandardExpiryDays = 90,
			RequiresEmailAddress = true,
			RequiresExternalAccountBinding = true,
			SupportsCachedValidations = true,
			SupportedFeatures =
				new List<string>
				{
					CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(), CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(), CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString()
				},
			SupportedKeyTypes =
				new List<string>
				{
					StandardKeyTypes.RSA256,
					StandardKeyTypes.RSA256_3072,
					StandardKeyTypes.RSA256_4096,
					StandardKeyTypes.ECDSA256,
					StandardKeyTypes.ECDSA384
				},
			EabInstructions =
				"To get started you need to create a project on Google Cloud, then generate EAB credentials using the gcloud command line: https://cloud.google.com/public-certificate-authority/docs/quickstart",
			TrustedRoots = new Dictionary<string, string>
			{
			}
		};
	}

	public static Dictionary<string, string> GetKnownCertificates()
	{
		var knownCerts = new Dictionary<string, string>
		{
		};
		return knownCerts;
	}
}
