using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class FinalizeOrderRequest
    {
        [JsonPropertyName("csr")]
        public string Csr { get; set; }
    }
}
