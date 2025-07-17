using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChallengeStatus
    {
        [JsonStringEnumMemberName("pending")]
        Pending,
        [JsonStringEnumMemberName("prcoessing")]
        Processing,
        [JsonStringEnumMemberName("valid")]
        Valid,
        [JsonStringEnumMemberName("invalid")]
        Invalid
    }
}
