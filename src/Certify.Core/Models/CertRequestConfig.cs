using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class CertRequestConfig : BindableBase
    {
        /// <summary>
        /// Primary subject domain for our SSL Cert request
        /// </summary>
        public string PrimaryDomain { get; set; }

        /// <summary>
        /// Optional subject alternative names for our SSL Cert request
        /// </summary>
        public string[] SubjectAlternativeNames { get; set; }

        /// <summary>
        /// Root path for our website content, used when responding to file based challenges
        /// </summary>
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
        public bool PerformChallengeFileCopy { get; set; }

        /// <summary>
        /// If true, perform an automated check that the web host is configured to respond to
        /// extensionless file requests
        /// </summary>
        public bool PerformExtensionlessConfigChecks { get; set; }

        /// <summary>
        /// If true, perform an automated check that the web host is configured to respond to
        /// tls sni requests
        /// </summary>
        public bool PerformTlsSniBindingConfigChecks { get; set; }

        /// <summary>
        /// If true, attempt to automatically configure the web host/web aplication as required
        /// </summary>
        public bool PerformAutoConfig { get; set; }

        /// <summary>
        /// If true, automatically add/remove SSL certificate to store and create or update SSL
        /// certificate bindings in web host
        /// </summary>
        public bool PerformAutomatedCertBinding { get; set; }

        /// <summary>
        /// If true, indicates that Certify should attempt to send failure notifications if an
        /// automated renewal request fails
        /// </summary>
        public bool EnableFailureNotifications { get; set; }

        /// <summary>
        /// In the case of Lets Encrypt, the challenge type this request will use (eg. http-01)
        /// </summary>
        public string ChallengeType { get; set; }

        /// <summary>
        /// PowerShell script to run before executing certificate request
        /// </summary>
        public string PreRequestPowerShellScript { get; set; }

        /// <summary>
        /// PowerShell script to run before executing certificate request
        /// </summary>
        public string PostRequestPowerShellScript { get; set; }
    }
}