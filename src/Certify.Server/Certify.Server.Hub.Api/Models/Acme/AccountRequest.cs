using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents a request to create or update an ACME account, including contact information,
    /// terms of service agreement, optional external account binding, and account status.
    /// </summary>
    public class AccountRequest
    {
        /// <summary>
        /// Gets or sets the contact information for the account (e.g., email addresses).
        /// </summary>
        [JsonPropertyName("contact")]
        public string[] Contact { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the terms of service have been agreed to.
        /// </summary>
        [JsonPropertyName("termsOfServiceAgreed")]
        public bool TermsOfServiceAgreed { get; set; }

        /// <summary>
        /// Gets or sets the external account binding information, if required by the CA.
        /// </summary>
        [JsonPropertyName("externalAccountBinding")]
        public JwsPayload ExternalAccountBinding { get; set; }

        /// <summary>
        /// Gets or sets the status of the account.
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }
    }
}
