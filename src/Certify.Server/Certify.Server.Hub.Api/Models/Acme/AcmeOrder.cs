using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents an ACME order, including its status, identifiers, authorizations, and certificate details.
    /// </summary>
    public class AcmeOrder
    {
        /// <summary>
        /// Gets or sets the unique identifier of the ACME order.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the current status of the ACME order.
        /// </summary>
        [JsonPropertyName("status")]
        public OrderStatus Status { get; set; }

        /// <summary>
        /// Gets or sets the expiration date and time of the order.
        /// </summary>
        [JsonPropertyName("expires")]
        public DateTime Expires { get; set; }

        /// <summary>
        /// Gets or sets the identifiers for the order, such as the Common Name (CN) and Subject Alternative Names (SANs).
        /// </summary>
        [JsonPropertyName("identifiers")]
        public AcmeIdentifier[] Identifiers { get; set; }

        /// <summary>
        /// Gets or sets the date and time before which the order is not valid.
        /// </summary>
        [JsonPropertyName("notBefore")]
        public DateTime? NotBefore { get; set; }

        /// <summary>
        /// Gets or sets the date and time after which the order is no longer valid.
        /// </summary>
        [JsonPropertyName("notAfter")]
        public DateTime? NotAfter { get; set; }

        /// <summary>
        /// Gets or sets the list of authorization URLs associated with the order.
        /// </summary>
        [JsonPropertyName("authorizations")]
        public List<string> Authorizations { get; set; }

        /// <summary>
        /// Gets or sets the URL to finalize the order.
        /// </summary>
        [JsonPropertyName("finalize")]
        public string Finalize { get; set; }

        /// <summary>
        /// Gets or sets the URL of the certificate issued for the order.
        /// </summary>
        [JsonPropertyName("certificate")]
        public string Certificate { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the managed certificate associated with the order.
        /// Internal hub bookkeeping only; not part of the ACME order resource.
        /// </summary>
        [JsonIgnore]
        public string ManagedCertificateId { get; set; }

        /// <summary>
        /// UTC timestamp when the order was created. Used for stale-order cleanup.
        /// </summary>
        [JsonIgnore]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Hub instance that owns the temporary managed certificate for this order.
        /// </summary>
        [JsonIgnore]
        public string HubInstanceId { get; set; }

        /// <summary>
        /// Raw authorization identifiers (not URLs) used for reliable cleanup.
        /// </summary>
        [JsonIgnore]
        public List<string> AuthorizationIds { get; set; } = [];

        /// <summary>
        /// The account KID which created and owns this order. Only this account may finalize,
        /// read or download the certificate for the order.
        /// Internal hub bookkeeping only; not part of the ACME order resource.
        /// </summary>
        [JsonIgnore]
        public string AccountKid { get; set; }
    }
}
