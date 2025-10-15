using System.Text.Json;
using Newtonsoft.Json;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents the protected header of a JSON Web Signature (JWS) used in ACME protocol requests.
    /// Contains algorithm, key, key ID, URL, and nonce information.
    /// </summary>
    public class JwsProtectedHeader
    {
        /// <summary>
        /// Gets or sets the algorithm used for the JWS.
        /// </summary>
        [JsonProperty("alg")]
        public string Alg { get; set; }

        /// <summary>
        /// Gets or sets the JSON Web Key (JWK) used for the JWS.
        /// </summary>
        [JsonProperty("jwk")]
        public required JsonWebKey Jwk { get; set; }

        /// <summary>
        /// Gets or sets the key ID.
        /// </summary>
        [JsonProperty("kid")]
        public string Kid { get; set; }

        /// <summary>
        /// Gets or sets the URL.
        /// </summary>
        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>
        /// Gets or sets the nonce value.
        /// </summary>
        [JsonProperty("nonce")]
        public string Nonce { get; set; }
    }
}
