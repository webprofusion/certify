using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents the status of an ACME challenge.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChallengeStatus
    {
        /// <summary>
        /// The challenge is pending and has not yet been processed.
        /// </summary>
        [JsonStringEnumMemberName("pending")]
        Pending,

        /// <summary>
        /// The challenge is currently being processed.
        /// </summary>
        [JsonStringEnumMemberName("prcoessing")]
        Processing,

        /// <summary>
        /// The challenge has been successfully validated.
        /// </summary>
        [JsonStringEnumMemberName("valid")]
        Valid,

        /// <summary>
        /// The challenge has failed validation.
        /// </summary>
        [JsonStringEnumMemberName("invalid")]
        Invalid
    }
}
