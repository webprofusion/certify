using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class Preferences
    {
        public bool CheckForUpdatesAtStartup { get; set; }

        public bool EnableAppTelematics { get; set; }

        public bool IgnoreStoppedSites { get; set; }

        public bool EnableValidationProxyAPI { get; set; }

        public bool EnableEFS { get; set; }

        public bool EnableDNSValidationChecks { get; set; }

        public int RenewalIntervalDays { get; set; }

        public int MaxRenewalRequests { get; set; }
    }
}