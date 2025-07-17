using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    // Status enums
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AccountStatus
    {
        [JsonStringEnumMemberName("valid")]
        Valid,
        [JsonStringEnumMemberName("deactivated")]
        Deactivated,
        [JsonStringEnumMemberName("revoked")]
        Revoked
    }
}
