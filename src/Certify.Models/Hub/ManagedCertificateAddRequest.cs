using System.Collections.Generic;
using Certify.Models.Config;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Minimal request to add or update a managed certificate on a target managed instance.
    /// </summary>
    public class ManagedCertificateAddRequest
    {
        /// <summary>
        /// Hub-assigned target managed instance id.
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// Optional friendly title for the managed certificate.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// identifiers to include in the certificate request.
        /// </summary>
        public ICollection<IdentifierItem>? Identifiers { get; set; }

        /// <summary>
        /// Optional challenge configuration. If omitted, an automatic hub-managed DNS challenge is used.
        /// </summary>
        public ICollection<ManagedCertificateChallengeRequest>? Challenge { get; set; }

        /// <summary>
        /// If true, request will start after creation
        /// </summary>
        public bool PerformRequest { get; set; }
    }

    /// <summary>
    /// Extensible challenge configuration for simple certificate upsert requests.
    /// </summary>
    public class ManagedCertificateChallengeRequest
    {
        /// <summary>
        /// Optional ACME challenge type. Defaults to dns-01 for hub-managed validation.
        /// </summary>
        public string? ChallengeType { get; set; } = SupportedChallengeTypes.CHALLENGE_TYPE_DNS;

        /// <summary>
        /// Optional provider type id for direct provider-based challenge configuration.
        /// </summary>
        public string? ProviderTypeId { get; set; }

        /// <summary>
        /// Optional stored credential id for direct provider-based challenge configuration.
        /// </summary>
        public string? CredentialId { get; set; }

        /// <summary>
        /// Optional stored credential name for future lookup-based configuration.
        /// </summary>
        public string? CredentialName { get; set; }

        /// <summary>
        /// Optional domain or zone match for challenge selection.
        /// </summary>
        public string? DomainMatch { get; set; }

        /// <summary>
        /// Optional direct challenge provider parameters.
        /// </summary>
        public ICollection<ProviderParameter>? Parameters { get; set; }
    }
}
