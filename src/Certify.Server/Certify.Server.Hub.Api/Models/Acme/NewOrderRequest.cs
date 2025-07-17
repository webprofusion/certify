using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class NewOrderRequest
    {
        [JsonPropertyName("identifiers")]
        public AcmeIdentifier[] Identifiers { get; set; }

        [JsonPropertyName("notBefore")]
        public DateTime? NotBefore { get; set; }

        [JsonPropertyName("notAfter")]
        public DateTime? NotAfter { get; set; }
    }
}
