using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class ExternalAccountBinding
    {
        [JsonPropertyName("protected")]
        public string Protected { get; set; }

        [JsonPropertyName("payload")]
        public string Payload { get; set; }

        [JsonPropertyName("signature")]
        public string Signature { get; set; }
    }
}
