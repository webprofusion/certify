using System.Text.Json;
using Newtonsoft.Json;

namespace Certify.Server.Hub.Api.Models.Acme
{
    // Additional data models for JWS validation
    public class JwsProtectedHeader
    {
        [JsonProperty("alg")]
        public string Alg { get; set; }

        [JsonProperty("jwk")]
        public JsonWebKey Jwk { get; set; }

        [JsonProperty("kid")]
        public string Kid { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("nonce")]
        public string Nonce { get; set; }
    }
}
