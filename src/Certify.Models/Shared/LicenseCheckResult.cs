using System;

namespace Registration.Core.Models.Shared
{
    public class LicenseCheckStatusCode
    {
        /// <summary>
        /// No license applied
        /// </summary>
        public const string Unlicensed = "unlicensed";

        /// <summary>
        /// Active license applied
        /// </summary>
        public const string Licensed = "licensed";

        /// <summary>
        /// License applied but has expired or is otherwise invalid
        /// </summary>
        public const string Invalid = "invalid";

        /// <summary>
        /// License status not known, possibly due to an error in checking the license
        /// </summary>
        public const string Unknown = "unknown";
    }

    public class LicenseCheckResult
    {
        public bool IsValid { get; set; }
        public string? StatusCode { get; set; } = string.Empty;
        public string? ValidationMessage { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public int MaxUsage { get; set; }
        public DateTime? DateExpiry { get; set; }
        public int UserProfileId { get; set; }
        public int UserProductLicenseId { get; set; }
        public string? ManagedLicenseId { get; set; }
        public string LicenseType { get; set; } = string.Empty;
    }
}
