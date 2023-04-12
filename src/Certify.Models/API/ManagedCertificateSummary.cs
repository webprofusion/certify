﻿using System;
using System.Collections.Generic;

namespace Certify.Models.API
{
    /// <summary>
    /// Summary information for a managed certificate
    /// </summary>
    public class ManagedCertificateSummary
    {
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
        public IEnumerable<Identifier> Identifiers { get; set; } = new List<Identifier>();

        /// <summary>
        /// Primary identifier (e.g. primary subject domain name)
        /// </summary>
        public Identifier PrimaryIdentifier { get; set; } = new Identifier();

        /// <summary>
        /// Date request/renewal was last attempted (if any)
        /// </summary>
        public DateTime? DateRenewed { get; set; }

        /// <summary>
        /// Date this item will expire (if applicable)
        /// </summary>
        public DateTime? DateExpiry { get; set; }

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

    /// <summary>
    /// An identifier to be included on a certificate
    /// </summary>
    public class Identifier
    {
        /// <summary>
        /// Identifier type (e.g. "dns", "ip")
        /// </summary>
        public string Type { get; set; } = "dns";

        /// <summary>
        /// Identifier value
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }
}
