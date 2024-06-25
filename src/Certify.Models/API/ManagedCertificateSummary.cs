using System;
using System.Collections.Generic;

namespace Certify.Models.API
{
    /// <summary>
    /// Summary information for a managed certificate
    /// </summary>
    public class ManagedCertificateSummary
    {
        public string? InstanceId { get; set; } = string.Empty;
        public string? InstanceTitle { get; set; } = string.Empty;
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
    }

    public class ManagedCertificateSummaryResult
    {
        public IEnumerable<ManagedCertificateSummary> Results { get; set; } = new List<ManagedCertificateSummary>();
        public long TotalResults { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }
}
