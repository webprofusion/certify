using System;
using System.Collections.Generic;
using System.Text;

namespace Registration.Core.Models.Shared
{
    public class LicenseCheckResult
    {
        public bool IsValid { get; set; }
        public string StatusCode { get; set; }
        public string ValidationMessage { get; set; }
        public int UsageCount { get; set; }
        public int MaxUsage { get; set; }
        public DateTime? DateExpiry { get; set; }
        public int UserProfileId { get; set; }
        public int UserProductLicenseId { get; set; }
    }
}