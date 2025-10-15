using System.Text.Json;
using Newtonsoft.Json;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents a JSON Web Key (JWK) as used in ACME protocol for cryptographic operations.
    /// </summary>
    public class JsonWebKey
    {
        /// <summary>
        /// Gets or sets the key type parameter for the JWK.
        /// </summary>
        [JsonProperty("kty")]
        public required string Kty { get; set; }

        /// <summary>
        /// Gets or sets the modulus value for RSA keys.
        /// </summary>
        [JsonProperty("n")]
        public string N { get; set; }

        /// <summary>
        /// Gets or sets the exponent value for RSA keys.
        /// </summary>
        [JsonProperty("e")]
        public string E { get; set; }

        /// <summary>
        /// Gets or sets the curve name for EC keys.
        /// </summary>
        [JsonProperty("crv")]
        public string Crv { get; set; }

        /// <summary>
        /// Gets or sets the X coordinate for EC keys.
        /// </summary>
        [JsonProperty("x")]
        public string X { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate for EC keys.
        /// </summary>
        [JsonProperty("y")]
        public string Y { get; set; }
    }
}
