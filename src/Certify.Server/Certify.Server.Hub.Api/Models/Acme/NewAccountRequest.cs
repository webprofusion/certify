using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class NewAccountRequest
    {
        [JsonPropertyName("contact")]
        public string[] Contact { get; set; }

        [JsonPropertyName("termsOfServiceAgreed")]
        public bool TermsOfServiceAgreed { get; set; }

        [JsonPropertyName("externalAccountBinding")]
        public ExternalAccountBinding ExternalAccountBinding { get; set; }
    }
}
