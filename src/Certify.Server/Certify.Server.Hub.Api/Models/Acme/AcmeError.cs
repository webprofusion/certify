using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class AcmeError
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("detail")]
        public string Detail { get; set; }
    }
}
