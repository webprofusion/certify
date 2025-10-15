using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents metadata for an ACME directory, including terms of service, website, CAA identities, and external account requirements.
    /// </summary>
    public class DirectoryMeta
    {
        /// <summary>
        /// Gets or sets the URL of the terms of service.
        /// </summary>
        [JsonPropertyName("termsOfService")]
        public string TermsOfService { get; set; }

        /// <summary>
        /// Gets or sets the website URL for the ACME service.
        /// </summary>
        [JsonPropertyName("website")]
        public string Website { get; set; }

        /// <summary>
        /// Gets or sets the list of CAA identities recognized by the ACME server.
        /// </summary>
        [JsonPropertyName("caaIdentities")]
        public string[] CaaIdentities { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether an external account is required.
        /// </summary>
        [JsonPropertyName("externalAccountRequired")]
        public bool ExternalAccountRequired { get; set; }
    }
}
