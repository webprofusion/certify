using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class AcmeChallenge
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("status")]
        public ChallengeStatus Status { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; }
    }
}
