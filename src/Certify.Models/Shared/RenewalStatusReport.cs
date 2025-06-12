using System;

namespace Certify.Models.Shared
{
    public class RenewalStatusReport
    {
        public string? InstanceId { get; set; }
        public string? MachineName { get; set; }
        public ManagedCertificate? ManagedSite { get; set; }
        public string? PrimaryContactEmail { get; set; }
        public string? AppName { get; set; }
        public string? AppVersion { get; set; }
        public DateTime? DateReported { get; set; }

        /// <summary>
        /// If true, report should be removed from dashboard.
        /// </summary>
        public bool IsRemoved { get; set; }
    }
}
