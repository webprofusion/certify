using System;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Configuration for a managed challenge, such as a DNS challenge for a specific domain/zone
    /// A managed challenge is one the management hub can complete on behalf of another ACME client
    /// </summary>
    public class ManagedChallenge : ConfigurationStoreItem
    {
        public CertRequestChallengeConfig? ChallengeConfig { get; set; }
    }

    public class ManagedChallengeRequest
    {
        /// <summary>
        /// The type of challenge to perform (e.g. dns-01)
        /// </summary>
        public string ChallengeType { get; set; } = string.Empty;

        /// <summary>
        /// domain etc challenge is being performed for
        /// </summary>
        public string Identifier { get; set; } = string.Empty;
        public string ResponseKey { get; set; } = string.Empty;
        public string ResponseValue { get; set; } = string.Empty;
        public string AuthKey { get; set; } = string.Empty;
        public string AuthSecret { get; set; } = string.Empty;
    }
}
