using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Certify.Models.Config;
using Microsoft.IdentityModel.JsonWebTokens;
using Newtonsoft.Json;

namespace Certify.Models
{
    public class TkAuthToken
    {
        public string Token { get; set; } = string.Empty;
        public string Crl { get; set; } = string.Empty;
    }
    /// <summary>
    /// Authority Token Challenge claim
    /// </summary>
    public class AtcClaim
    {
        /// <summary>
        /// Token type e.g. TNAUthList
        /// </summary>
        [JsonProperty(propertyName: "tktype")]
        public string TkType { get; set; } = string.Empty;

        /// <summary>
        /// a "tkvalue" key with a string value equal to the base64url encoding of the TN Authorization List certificate extension ASN.1 object using DER encoding rules.
        /// "tkvalue" is a required key and MUST be included.
        /// </summary>
        [JsonProperty(propertyName: "tkvalue")]
        public string TkValue { get; set; } = string.Empty;

        /// <summary>
        /// a "ca" key with a boolean value set to either true when the requested certificate 
        /// is allowed to be a CA cert for delegation uses or false 
        /// when the requested certificate is not intended to be a CA cert, only an end-entity certificate.
        /// "ca" is an optional key, if not included the "ca" value is considered false by default.
        /// </summary>
        [JsonProperty(propertyName: "ca")]
        public bool? ca { get; set; }

        /// <summary>
        /// a "fingerprint" key is constructed as defined in [RFC8555] Section 8.1 
        /// </summary>
        [JsonProperty(propertyName: "fingerprint")]
        public string Fingerprint { get; set; } = string.Empty;
    }

    public class CertRequestChallengeConfig : BindableBase
    {
        /// <summary>
        /// In the case of ACME CA, the challenge type this request will use (eg. http-01) 
        /// </summary>
        public string? ChallengeType { get; set; }

        /// <summary>
        /// Optional primary domain (e.g. test.com for www.test.com) for auto matching credential to domain
        /// </summary>
        public string? DomainMatch { get; set; }

        /// <summary>
        /// Id/key for the provider type we require (such as DNS01.API.ROUTE53) 
        /// </summary>
        public string? ChallengeProvider { get; set; }

        /// <summary>
        /// Id/key for the stored credential we need to use with the Challenge Provider 
        /// </summary>
        public string? ChallengeCredentialKey { get; set; }

        /// <summary>
        /// If applicable, path or root path relevant to the challenge (e.g. wwwroot path)
        /// </summary>
        public string? ChallengeRootPath { get; set; }

        /// <summary>
        /// Optional, DNS Zone ID if using a DNS challenge provider 
        /// </summary>
        public string? ZoneId { get; set; }

        /// <summary>
        /// If set, DNS validation will work with the target domain/zone in place of the original
        /// e.g. _acme-challenge.www.example.com delegated to _acme-challenge.www.acme.example.co.uk would be specified as "*.example.com:acme.example.co.uk"
        /// Note: Zone ID/Zone Lookup, Credentials etc would be for the delegated domain, not the original domain. 
        /// </summary>
        public string? ChallengeDelegationRule { get; set; }

        public ObservableCollection<ProviderParameter>? Parameters { get; set; }
    }

    public class CertRequestConfig : BindableBase
    {
        public CertRequestConfig()
        {
            Challenges = new ObservableCollection<CertRequestChallengeConfig>();

            // by default set to Single Site deployment (the previous default) - this is to allow upgraded settings to behave normally
            DeploymentSiteOption = DeploymentOption.SingleSite;
        }

        /// <summary>
        /// Primary subject domain for our SSL Cert request 
        /// </summary>
        public string? PrimaryDomain { get; set; }

        /// <summary>
        /// Optional subject alternative names for our SSL Cert request 
        /// </summary>
        public string[]? SubjectAlternativeNames { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Optional list of IP addresses to include in cert request, primary first
        /// </summary>
        public string[]? SubjectIPAddresses { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Root path for our website content, used when responding to file based challenges 
        /// </summary>
        ///
        public string? WebsiteRootPath { get; set; }

        /// <summary>
        /// If required, a specific IP address to bind to when creating/updating this binding 
        /// </summary>
        public string? BindingIPAddress { get; set; }

        /// <summary>
        /// Optional specific port to bind SSL to. Defaults to 443. 
        /// </summary>
        public string? BindingPort { get; set; }

        /// <summary>
        /// Optionally use SNI when creating bindings (the default us to PerformAutomatedCertBinding,
        /// which also uses SNI)
        /// </summary>
        public bool? BindingUseSNI { get; set; }

        /// <summary>
        /// If true, this request requires a challenge file copy as part of the web applications
        /// content, usually to /.well-known/acme-challenge/
        /// </summary>
        public bool PerformChallengeFileCopy { get; set; } = true;

        /// <summary>
        /// If true, perform an automated check that the web host is configured to respond to
        /// extensionless file requests
        /// </summary>
        public bool PerformExtensionlessConfigChecks { get; set; } = true;

        /// <summary>
        /// If true, perform an automated check that the web host is configured to respond to tls sni requests
        /// </summary>
        public bool PerformTlsSniBindingConfigChecks { get; set; } = true;

        /// <summary>
        /// If true, attempt to automatically configure the web host/web application as required 
        /// </summary>
        public bool PerformAutoConfig { get; set; } = true;

        /// <summary>
        /// If true, automatically add/remove SSL certificate to store and create or update SSL
        /// certificate bindings in web host
        /// </summary>
        public bool PerformAutomatedCertBinding { get; set; } = true;

        /// <summary>
        /// If true, existings https bindings for the cert we are renewing will be removed and replaced 
        /// </summary>
        public bool AlwaysRecreateBindings { get; set; }

        /// <summary>
        /// If true, indicates that Certify should attempt to send failure notifications if an
        /// automated renewal request fails
        /// </summary>
        public bool EnableFailureNotifications { get; set; } = true;

        /// <summary>
        /// In the case of ACME, the primary challenge type this request will use (eg. http-01) 
        /// </summary>
        [Obsolete("ChallengeType is now determined in Challenges collection. This value is preserved for upgrade of legacy settings.")]
        public string ChallengeType { get; set; } = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP;

        /// <summary>
        /// The trigger for the webhook (None, Success, Error) 
        /// </summary>
        public string WebhookTrigger { get; set; } = "None";

        /// <summary>
        /// The http method for the webhook request 
        /// </summary>
        public string? WebhookMethod { get; set; }

        /// <summary>
        /// The http url for the webhook request 
        /// </summary>
        public string? WebhookUrl { get; set; }

        /// <summary>
        /// The http content type header for the webhook request 
        /// </summary>
        public string? WebhookContentType { get; set; }

        /// <summary>
        /// The http body template for the webhook request 
        /// </summary>
        public string? WebhookContentBody { get; set; }

        /// <summary>
        /// PowerShell script to run before executing certificate request 
        /// </summary>
        public string? PreRequestPowerShellScript { get; set; }

        /// <summary>
        /// PowerShell script to run before executing certificate request 
        /// </summary>
        public string? PostRequestPowerShellScript { get; set; }

        /// <summary>
        /// Key algorithm type for CSR signing. Default is RS256 
        /// </summary>
        public string? CSRKeyAlg { get; set; } = SupportedCSRKeyAlgs.ECDSA256;

        /// <summary>
        /// Deployment site options (single/all etc) 
        /// </summary>
        public DeploymentOption DeploymentSiteOption { get; set; } = DeploymentOption.SingleSite;

        /// <summary>
        /// Binding options: Add/Update or Update 
        /// </summary>
        public DeploymentBindingOption DeploymentBindingOption { get; set; } = DeploymentBindingOption.AddOrUpdate;

        /// <summary>
        /// If true, apply cert to matching hostnames (default = true) 
        /// </summary>
        public bool DeploymentBindingMatchHostname { get; set; } = true;

        /// <summary>
        /// If true, apply cert where hostname in binding is blank (default = false) 
        /// </summary>
        public bool DeploymentBindingBlankHostname { get; set; }

        /// <summary>
        /// If true, apply cert where binding has certificatehash set to the old certificate 
        /// </summary>
        public bool DeploymentBindingReplacePrevious { get; set; }

        /*
        /// <summary>
        /// Host or server where this item is based, usually localhost if managing the local server
        /// </summary>
        public string? DeploymentTargetHost { get; set; }
        */

        /// <summary>
        /// Service type to deploy to on target host (e.g. IIS, nginx, Apache)
        /// </summary>
        public string? DeploymentTargetType { get; set; }

        /// <summary>
        /// Optional list of challenge configs, used when challenge requires credentials, optionally
        /// varying per domain
        /// </summary>
        public ObservableCollection<CertRequestChallengeConfig>? Challenges { get; set; }

        /// <summary>
        /// If set, this is a custom PEM encoded CSR to use for the certificate signing request to the CA
        /// </summary>
        public string? CustomCSR { get; set; }

        /// <summary>
        /// If set, this is one or more custom JWT tokens used for TkAuth-01 : https://datatracker.ietf.org/doc/draft-ietf-acme-authority-token-tnauthlist/13/
        /// </summary>
        public ObservableCollection<TkAuthToken>? AuthorityTokens { get; set; }

        /// <summary>
        /// If set, this is a custom Private Key to use for certificate signing
        /// </summary>
        public string? CustomPrivateKey { get; set; }

        /// <summary>
        /// If enabled, private key is exported on first use and re-used for subsequent certificate renewals
        /// </summary>
        public bool ReusePrivateKey { get; set; }

        /// <summary>
        /// If enabled, CSR will include extension to request OcspMustStaple attribute be included on the certificate
        /// </summary>
        public bool RequireOcspMustStaple { get; set; }

        /// <summary>
        /// If set, this is the preferred chain to select if present (e.g. the name of the root cert in the preferred chain)
        /// </summary>
        public string? PreferredChain { get; set; }

        /// <summary>
        /// If set, the preferred number of days the certificate should expire (e.g. 14 or 0.5). Support for this will vary by CA.
        /// </summary>
        public float? PreferredExpiryDays { get; set; }

        public void ApplyDeploymentOptionDefaults()
        {
            // if the selected mode is auto, discard settings which do not apply
            if (DeploymentSiteOption == DeploymentOption.Auto)
            {
                PerformAutomatedCertBinding = true;
                DeploymentBindingBlankHostname = false;
                DeploymentBindingMatchHostname = true;
                DeploymentBindingReplacePrevious = true;
                DeploymentBindingOption = DeploymentBindingOption.AddOrUpdate;
            }

            if (PerformAutomatedCertBinding)
            {
                // if using auto cert bindings discard prior selections for fixed IPs/non-default ports and use SNI
                BindingUseSNI = true;
                BindingIPAddress = null;
                BindingPort = null;
            }
        }

        internal List<string> GetCertificateDomains()
        {
            var allDomains = new List<string>();

            if (!string.IsNullOrEmpty(PrimaryDomain))
            {
#pragma warning disable CS8604 // Possible null reference argument.
                allDomains.Add(PrimaryDomain);
#pragma warning restore CS8604 // Possible null reference argument.
            }

            if (SubjectAlternativeNames != null)
            {
                allDomains.AddRange(SubjectAlternativeNames);
            }

            return allDomains.Distinct().ToList();
        }

        public List<CertIdentifierItem> GetCertificateIdentifiers()
        {
            var identifiers = new List<CertIdentifierItem>();

            var domains = GetCertificateDomains();
            foreach (var d in domains)
            {
                identifiers.Add(new CertIdentifierItem { IdentifierType = CertIdentifierType.Dns, Value = d });
            }

            if (SubjectIPAddresses?.Any() == true)
            {
                foreach (var ip in SubjectIPAddresses)
                {
                    identifiers.Add(new CertIdentifierItem { IdentifierType = CertIdentifierType.Ip, Value = ip });
                }
            }

            if (AuthorityTokens?.Any() == true)
            {
                foreach (var tnAuthToken in AuthorityTokens)
                {
                    var jwt = new JsonWebToken(jwtEncodedString: tnAuthToken.Token);

                    var atc = jwt.Claims.FirstOrDefault(c => c.Type == "atc");
                    if (atc != null)
                    {
                        var parsedAtc = System.Text.Json.JsonSerializer.Deserialize<AtcClaim>(atc.Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        identifiers.Add(new CertIdentifierItem { IdentifierType = CertIdentifierType.TnAuthList, Value = parsedAtc.TkValue });
                    }
                }
            }

            return identifiers;
        }

        public static AtcClaim? GetParsedAtc(string jwt)
        {
            try
            {
                var parsedJwt = new JsonWebToken(jwtEncodedString: jwt);

                var atc = parsedJwt.Claims.FirstOrDefault(c => c.Type == "atc");
                if (atc != null)
                {
                    var parsedAtc = System.Text.Json.JsonSerializer.Deserialize<AtcClaim>(atc.Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return parsedAtc;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public static byte[]? GetAuthorityTokenValueBytes(TkAuthToken token)
        {
            var parsedAtc = GetParsedAtc(token.Token);
            if (parsedAtc != null)
            {
                return Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parsedAtc.TkValue);
            }
            else
            {
                return null;
            }
        }
    }
}
