using System.Text.Json;
using Newtonsoft.Json;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents a JSON Web Signature (JWS) payload, containing the protected header, payload, and signature.
    /// </summary>
    public class JwsPayload
    {

        /// <summary>
        /// Gets or sets the protected header of the JWS.
        /// </summary>
        [JsonProperty("protected")]
        public string Protected { get; set; }

        /// <summary>
        /// Gets or sets the payload of the JWS.
        /// </summary>
        [JsonProperty("payload")]
        public string Payload { get; set; }

        /// <summary>
        /// Gets or sets the signature of the JWS.
        /// </summary>
        [JsonProperty("signature")]
        public string Signature { get; set; }
    }
}
