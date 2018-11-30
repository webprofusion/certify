using System;
using System.Collections.ObjectModel;
using Certify.Models.Config;

namespace Certify.Models
{
    public class CertificateAuthorities
    {
        public const string LETS_ENCRYPT = "letsencrypt";
    }

    public class CertRequestChallengeConfig : BindableBase
    {
        /// <summary>
        /// In the case of Lets Encrypt, the challenge type this request will use (eg. http-01) 
        /// </summary>
        public string ChallengeType { get; set; }

        /// <summary>
        /// Optional primary domain (e.g. test.com for www.test.com) for auto matching credential to domain
        /// </summary>
        public string DomainMatch { get; set; }

        /// <summary>
        /// Id/key for the provider type we require (such as DNS01.API.ROUTE53) 
        /// </summary>
        public string ChallengeProvider { get; set; }

        /// <summary>
        /// Id/key for the stored credential we need to use with the Challenge Provider 
        /// </summary>
        public string ChallengeCredentialKey { get; set; }

        /// <summary>
        /// If applicable, path or root path relevant to the challenge (e.g. wwwroot path)
        /// </summary>
        public string ChallengeRootPath { get; set; }

        /// <summary>
        /// Optional, DNS Zone ID if using a DNS challenge provider 
        /// </summary>
        public string ZoneId { get; set; }

        public ObservableCollection<ProviderParameter> Parameters { get; set; }
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
        public string PrimaryDomain { get; set; }

        /// <summary>
        /// Optional subject alternative names for our SSL Cert request 
        /// </summary>
        public string[] SubjectAlternativeNames { get; set; } = new string[] { };

        /// <summary>
        /// Root path for our website content, used when responding to file based challenges 
        /// </summary>
        ///
        [Obsolete]
        public string WebsiteRootPath { get; set; }

        /// <summary>
        /// If required, a specific IP address to bind to when creating/updating this binding 
        /// </summary>
        public string BindingIPAddress { get; set; }

        /// <summary>
        /// Optional specific port to bind SSL to. Defaults to 443. 
        /// </summary>
        public string BindingPort { get; set; }

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
        /// If true, attempt to automatically configure the web host/web aplication as required 
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
        public bool AlwaysRecreateBindings { get; set; } = false;

        /// <summary>
        /// If true, indicates that Certify should attempt to send failure notifications if an
        /// automated renewal request fails
        /// </summary>
        public bool EnableFailureNotifications { get; set; } = true;

        /// <summary>
        /// In the case of Let's Encrypt, the primary challenge type this request will use (eg. http-01) 
        /// </summary>
        [Obsolete("ChallengeType is now determined in Challenges collection")]
        public string ChallengeType { get; set; } = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP;

        /// <summary>
        /// The trigger for the webhook (None, Success, Error) 
        /// </summary>
        public string WebhookTrigger { get; set; } = "None";

        /// <summary>
        /// The http method for the webhook request 
        /// </summary>
        public string WebhookMethod { get; set; }

        /// <summary>
        /// The http url for the webhook request 
        /// </summary>
        public string WebhookUrl { get; set; }

        /// <summary>
        /// The http content type header for the webhook request 
        /// </summary>
        public string WebhookContentType { get; set; }

        /// <summary>
        /// The http body template for the webhook request 
        /// </summary>
        public string WebhookContentBody { get; set; }

        /// <summary>
        /// PowerShell script to run before executing certificate request 
        /// </summary>
        public string PreRequestPowerShellScript { get; set; }

        /// <summary>
        /// PowerShell script to run before executing certificate request 
        /// </summary>
        public string PostRequestPowerShellScript { get; set; }

        /// <summary>
        /// Key algorithm type for CSR signing. Default is RS256 
        /// </summary>
        public string CSRKeyAlg { get; set; } = SupportedCSRKeyAlgs.RS256;

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
        public bool DeploymentBindingBlankHostname { get; set; } = false;

        /// <summary>
        /// If true, apply cert where binding has certificatehash set to the old certificate 
        /// </summary>
        public bool DeploymentBindingReplacePrevious { get; set; } = false;

        /// <summary>
        /// Optional list of challenge configs, used when challenge requires credentials, optionally
        /// varying per domain
        /// </summary>
        public ObservableCollection<CertRequestChallengeConfig> Challenges { get; set; }

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
    }
}
