using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents an ACME challenge used for domain validation.
    /// </summary>
    public class AcmeChallenge
    {
        /// <summary>
        /// Gets or sets the type of the ACME challenge.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the status of the ACME challenge.
        /// </summary>
        [JsonPropertyName("status")]
        public ChallengeStatus Status { get; set; }

        /// <summary>
        /// Gets or sets the URL of the ACME challenge.
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// Gets or sets the token for the ACME challenge.
        /// </summary>
        [JsonPropertyName("token")]
        public string Token { get; set; }
    }
}
