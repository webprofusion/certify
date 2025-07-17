using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class AcmeAccount
    {
        [JsonPropertyName("status")]
        public AccountStatus Status { get; set; }

        [JsonPropertyName("contact")]
        public string[] Contact { get; set; }

        [JsonPropertyName("termsOfServiceAgreed")]
        public bool TermsOfServiceAgreed { get; set; }

        [JsonPropertyName("orders")]
        public string Orders { get; set; }
    }
}
