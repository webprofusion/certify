using System.Text.Json;
using Newtonsoft.Json;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class JwsPayload
    {

        [JsonProperty("protected")]
        public string Protected { get; set; }

        [JsonProperty("payload")]
        public string Payload { get; set; }

        [JsonProperty("signature")]
        public string Signature { get; set; }
    }
}
