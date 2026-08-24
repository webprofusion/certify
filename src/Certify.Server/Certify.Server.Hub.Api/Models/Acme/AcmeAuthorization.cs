using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents an ACME authorization, including identifier, status, expiration, and challenges.
    /// </summary>
    public class AcmeAuthorization
    {
        /// <summary>
        /// The identifier for which authorization is being requested.
        /// </summary>
        [JsonPropertyName("identifier")]
        public AcmeIdentifier Identifier { get; set; }

        /// <summary>
        /// The current status of the authorization.
        /// </summary>
        [JsonPropertyName("status")]
        public AuthorizationStatus Status { get; set; }

        /// <summary>
        /// The expiration date and time of the authorization.
        /// </summary>
        [JsonPropertyName("expires")]
        public DateTime Expires { get; set; }

        /// <summary>
        /// The list of challenges associated with the authorization.
        /// </summary>
        [JsonPropertyName("challenges")]
        public List<AcmeChallenge> Challenges { get; set; }

        /// <summary>
        /// The account KID which owns this authorization.
        /// Internal hub bookkeeping only; not part of the ACME authorization resource.
        /// </summary>
        [JsonIgnore]
        public string AccountKid { get; set; }
    }
}
