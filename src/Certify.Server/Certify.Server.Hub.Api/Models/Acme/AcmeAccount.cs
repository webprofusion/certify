using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents an ACME account, including its status, contact information, terms of service agreement, and related orders.
    /// </summary>
    public class AcmeAccount
    {
        /// <summary>
        /// Gets or sets the internal identifier for the ACME account.
        /// </summary>
        [JsonPropertyName("internalId")]
        public string internalId { get; set; }

        /// <summary>
        /// Gets or sets the status of the ACME account.
        /// </summary>
        [JsonPropertyName("status")]
        public AccountStatus Status { get; set; }

        /// <summary>
        /// Gets or sets the contact information associated with the ACME account.
        /// </summary>
        [JsonPropertyName("contact")]
        public string[] Contact { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the terms of service have been agreed to.
        /// </summary>
        [JsonPropertyName("termsOfServiceAgreed")]
        public bool TermsOfServiceAgreed { get; set; }

        /// <summary>
        /// Gets or sets the orders related to the ACME account.
        /// </summary>
        [JsonPropertyName("orders")]
        public string Orders { get; set; }
    }
}
