namespace Certify.Models
{
    public class Preferences
    {
        public bool EnableAppTelematics { get; set; } = true;

        public bool IgnoreStoppedSites { get; set; } = false;

        public bool EnableValidationProxyAPI { get; set; } = true;

        public bool EnableEFS { get; set; } = false;

        public bool EnableDNSValidationChecks { get; set; } = false;

        public int RenewalIntervalDays { get; set; } = 0;

        public int MaxRenewalRequests { get; set; } = 0;
    }
}