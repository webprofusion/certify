using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents an error response in the ACME protocol.
    /// </summary>
    public class AcmeError
    {
        /// <summary>
        /// Gets or sets the type of the ACME error.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the detail message of the ACME error.
        /// </summary>
        [JsonPropertyName("detail")]
        public string Detail { get; set; }
    }
}
