using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class AcmeAuthorization
    {
        [JsonPropertyName("identifier")]
        public AcmeIdentifier Identifier { get; set; }

        [JsonPropertyName("status")]
        public AuthorizationStatus Status { get; set; }

        [JsonPropertyName("expires")]
        public DateTime Expires { get; set; }

        [JsonPropertyName("challenges")]
        public List<AcmeChallenge> Challenges { get; set; }
    }
}
