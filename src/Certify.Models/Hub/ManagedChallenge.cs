using System;
using System.Collections.Generic;

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

    /// <summary>
    /// Managed challenge with tag information for API responses
    /// </summary>
    public class ManagedChallengeSummary
    {
        /// <summary>
        /// Unique identifier for the managed challenge
        /// </summary>
        public string Id { get; set; } = default!;

        /// <summary>
        /// Title/description of the managed challenge
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Challenge configuration details
        /// </summary>
        public CertRequestChallengeConfig? ChallengeConfig { get; set; }

        /// <summary>
        /// Tags assigned to this managed challenge
        /// </summary>
        public List<TagSummary> Tags { get; set; } = [];
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

        public DateTimeOffset? DateTimePerformed { get; set; }
        public string? ManagedCertId { get; set; }
    }
}
