using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class AccountRequest
    {
        [JsonPropertyName("contact")]
        public string[] Contact { get; set; }

        [JsonPropertyName("termsOfServiceAgreed")]
        public bool TermsOfServiceAgreed { get; set; }

        [JsonPropertyName("externalAccountBinding")]
        public JwsPayload ExternalAccountBinding { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }
}
