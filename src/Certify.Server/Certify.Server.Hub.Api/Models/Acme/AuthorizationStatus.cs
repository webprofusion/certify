using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthorizationStatus
    {
        [JsonStringEnumMemberName("pending")]
        Pending,
        [JsonStringEnumMemberName("valid")]
        Valid,
        [JsonStringEnumMemberName("invalid")]
        Invalid,
        [JsonStringEnumMemberName("deactivated")]
        Deactivated,
        [JsonStringEnumMemberName("expired")]
        Expired,
        [JsonStringEnumMemberName("revoked")]
        Revoked
    }
}
