using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    public class DirectoryMeta
    {
        [JsonPropertyName("termsOfService")]
        public string TermsOfService { get; set; }

        [JsonPropertyName("website")]
        public string Website { get; set; }

        [JsonPropertyName("caaIdentities")]
        public string[] CaaIdentities { get; set; }

        [JsonPropertyName("externalAccountRequired")]
        public bool ExternalAccountRequired { get; set; }
    }
}
