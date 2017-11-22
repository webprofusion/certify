using System;

namespace Certify.Models.Shared
{
    public class RenewalStatusReport
    {
        public string InstanceId { get; set; }
        public string MachineName { get; set; }
        public ManagedSite ManagedSite { get; set; }
        public string PrimaryContactEmail { get; set; }
        public string AppVersion { get; set; }
        public DateTime? DateReported { get; set; }
    }
}