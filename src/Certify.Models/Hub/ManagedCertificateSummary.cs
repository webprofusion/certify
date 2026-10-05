using System;
using System.Collections.Generic;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Summary information for a managed certificate
    /// </summary>
    public record ManagedCertificateSummary
    {
        public string? InstanceId { get; set; } = string.Empty;
        public string? InstanceTitle { get; set; } = string.Empty;

        public string? OS { get; set; } = string.Empty;
        public string? ClientDetails { get; set; } = string.Empty;
        /// <summary>
        /// Id for this managed item
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Friendly name for this item, not necessarily related to the domains
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// List of all identifiers included in this managed certificate (e.g. dns domain names)
        /// </summary>
        public IEnumerable<CertIdentifierItem> Identifiers { get; set; } = new List<CertIdentifierItem>();

        /// <summary>
        /// Primary identifier (e.g. primary subject domain name)
        /// </summary>
        public CertIdentifierItem? PrimaryIdentifier { get; set; } = new CertIdentifierItem();

        /// <summary>
        /// Date request/renewal was last attempted (if any)
        /// </summary>
        public DateTimeOffset? DateRenewed { get; set; }

        /// <summary>
        /// Date this item will expire (if applicable)
        /// </summary>
        public DateTimeOffset? DateExpiry { get; set; }

        /// <summary>
        /// Timestamp of the most recent item fetch
        /// </summary>
        public DateTimeOffset? DateRetrieved { get; set; }

        /// <summary>
        /// Id of the CA last used for this item: the CA which issued the current certificate, otherwise the CA of the
        /// most recent attempt. Not set where no CA has been used, e.g. for externally managed certificates.
        /// </summary>
        public string? CertificateAuthorityId { get; set; }

        /// <summary>
        /// Display title for <see cref="CertificateAuthorityId"/>, or the id itself where the hub does not know the CA
        /// </summary>
        public string? CertificateAuthorityTitle { get; set; }

        /// <summary>
        /// Key type of the current certificate as a <see cref="StandardKeyTypes"/> value, otherwise the configured key
        /// type where one is set. See <see cref="StandardKeyTypes.GetDisplayName"/>.
        /// </summary>
        public string? KeyType { get; set; }

        /// <summary>
        /// Readable name of the issuer of the current certificate, e.g. "R11 (Let's Encrypt)"
        /// </summary>
        public string? Issuer { get; set; }

        /// <summary>
        /// Most recent request/renewal status for this item
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// General comments for this managed item
        /// </summary>
        public string Comments { get; set; } = string.Empty;

        /// <summary>
        /// If true, there is a certificate available (latest successful certificate order)
        /// </summary>
        public bool HasCertificate { get; set; }

        /// <summary>
        /// If true, is managed by an external certificate manager (e.g. Certbot, Posh-ACME, etc.) and is a read-only
        /// view of a certificate this instance does not own
        /// </summary>
        public bool IsExternallyManaged { get; set; }

        /// <summary>
        /// If true, is a certificate subscription which fetches its certificate from a configured external source
        /// </summary>
        public bool IsSubscription { get; set; }

        /// <summary>
        /// Tags assigned to this managed certificate
        /// </summary>
        public List<TagSummary> Tags { get; set; } = new();
    }

    /// <summary>
    /// Count of managed certificates sharing one value of a property, such as their CA or key type
    /// </summary>
    public record ManagedCertificateGroupCount
    {
        /// <summary>
        /// The shared value, as used to filter on it. Null for the items which have no value.
        /// </summary>
        public string? Key { get; set; }

        public string Title { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    /// <summary>
    /// Counts of managed certificates by CA and by key type, largest first
    /// </summary>
    public record ManagedCertificateBreakdown
    {
        public List<ManagedCertificateGroupCount> CertificateAuthorities { get; set; } = new();

        public List<ManagedCertificateGroupCount> KeyTypes { get; set; } = new();
    }

    public record ManagedCertificateSummaryResult
    {
        public IEnumerable<ManagedCertificateSummary> Results { get; set; } = new List<ManagedCertificateSummary>();
        public long TotalResults { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }
}
