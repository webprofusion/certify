using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class AcmeOrder
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("status")]
        public OrderStatus Status { get; set; }

        [JsonPropertyName("expires")]
        public DateTime Expires { get; set; }

        [JsonPropertyName("identifiers")]
        public AcmeIdentifier[] Identifiers { get; set; }

        [JsonPropertyName("notBefore")]
        public DateTime? NotBefore { get; set; }

        [JsonPropertyName("notAfter")]
        public DateTime? NotAfter { get; set; }

        [JsonPropertyName("authorizations")]
        public List<string> Authorizations { get; set; }

        [JsonPropertyName("finalize")]
        public string Finalize { get; set; }

        [JsonPropertyName("certificate")]
        public string Certificate { get; set; }

        [JsonPropertyName("managedCertificateId")]
        public string ManagedCertificateId { get; set; }
    }
}
