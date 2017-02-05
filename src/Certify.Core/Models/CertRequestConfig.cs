using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class CertRequestConfig
    {
        public string Domain { get; set; }
        public string WebsiteRootPath { get; set; }
        public bool PerformChallengeFileCopy { get; set; }

        public bool PerformExtensionlessConfigChecks { get; set; }
        public bool PerformExtensionlessAutoConfig { get; set; }
    }
}