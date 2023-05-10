using Newtonsoft.Json;

namespace Certify.Models
{
    public class AccountDetails
    {
        public string? StorageKey { get; set; }
        public string? ID { get; set; }
        public string Title { get; set; } = string.Empty;

        public string? CertificateAuthorityId { get; set; }
        public bool IsStagingAccount { get; set; }

        public string Email { get; set; } = string.Empty;
        public string AccountURI { get; set; } = string.Empty;
        public string AccountKey { get; set; } = string.Empty;
        public string AccountFingerprint { get; set; } = string.Empty;
        public string? EabKeyId { get; set; }
        public string? EabKey { get; set; }
        public string? EabKeyAlgorithm { get; set; }
        public string? PreferredChain { get; set; }

        /// <summary>
        /// If true, account was selected based on fallback/failover criteria
        /// </summary>
        [JsonIgnore]
        public bool IsFailoverSelection { get; set; }
    }
}
