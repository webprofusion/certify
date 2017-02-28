using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class CertRequestConfig
    {
        public string PrimaryDomain { get; set; }
        public string[] SubjectAlternativeNames { get; set; }
        public string WebsiteRootPath { get; set; }
        public bool PerformChallengeFileCopy { get; set; }

        public bool PerformExtensionlessConfigChecks { get; set; }
        public bool PerformExtensionlessAutoConfig { get; set; }
        public bool PerformAutomatedCertBinding { get; set; }
        public bool EnableFailureNotifications { get; set; }
        public string ChallengeType { get; set; }
    }
}