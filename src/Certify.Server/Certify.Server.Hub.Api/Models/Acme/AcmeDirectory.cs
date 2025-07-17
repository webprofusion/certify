using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    // ACME Data Models
    public class AcmeDirectory
    {
        [JsonPropertyName("newNonce")]
        public string NewNonce { get; set; }

        [JsonPropertyName("newAccount")]
        public string NewAccount { get; set; }

        [JsonPropertyName("newOrder")]
        public string NewOrder { get; set; }

        [JsonPropertyName("revokeCert")]
        public string RevokeCert { get; set; }

        [JsonPropertyName("keyChange")]
        public string KeyChange { get; set; }

        [JsonPropertyName("meta")]
        public DirectoryMeta Meta { get; set; }
    }
}
