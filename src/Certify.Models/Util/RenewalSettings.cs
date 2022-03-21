using System;
using System.Collections.Generic;

namespace Certify.Models
{
    [Flags]
    public enum RenewalMode
    {
        /// <summary>
        /// Renew items which are due to auto renew, auto decide 
        /// </summary>
        Auto = 0,
        /// <summary>
        /// Renewal all items which are due
        /// </summary>
        RenewalsDue = 1,
        /// <summary>
        /// Request/renew only items with a previous error status (ignore when last attempt was made)
        /// </summary>
        RenewalsWithErrors = 2,
        /// <summary>
        /// Request items which have not yet been requested (not previously renew or errored)
        /// </summary>
        NewItems = 4,
        /// <summary>
        /// Attempt to request/renew everything.
        /// </summary>
        All = 128
    }

    public class RenewalSettings
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public RenewalMode Mode { get; set; }

        public bool IsPreviewMode { get; set; }

        public List<string> TargetManagedCertificates { get; set; } = new();
    }
}
