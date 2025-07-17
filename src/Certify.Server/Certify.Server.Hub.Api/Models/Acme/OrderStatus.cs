using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OrderStatus
    {
        [JsonStringEnumMemberName("pending")]
        Pending,
        [JsonStringEnumMemberName("ready")]
        Ready,
        [JsonStringEnumMemberName("processing")]
        Processing,
        [JsonStringEnumMemberName("valid")]
        Valid,
        [JsonStringEnumMemberName("invalid")]
        Invalid
    }
}
