using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents a request to finalize an ACME order with a Certificate Signing Request (CSR).
    /// </summary>
    public class FinalizeOrderRequest
    {
        /// <summary>
        /// Gets or sets the base64-encoded Certificate Signing Request (CSR).
        /// </summary>
        [JsonPropertyName("csr")]
        public string Csr { get; set; }
    }
}
