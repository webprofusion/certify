using System;
using System.Collections.Generic;
using System.Text;

namespace Certify.Models
{
    public class RenewalSettings
    {
        public bool AutoRenewalsOnly { get; set; } = true;
        public bool ForceRenewal { get; set; } = false;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
