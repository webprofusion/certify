using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents the possible status values for an ACME authorization.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthorizationStatus
    {
        /// <summary>
        /// The authorization is pending and has not yet been completed.
        /// </summary>
        [JsonStringEnumMemberName("pending")]
        Pending,
        /// <summary>
        /// The authorization is valid and has been successfully completed.
        /// </summary>
        [JsonStringEnumMemberName("valid")]
        Valid,
        /// <summary>
        /// The authorization is invalid and has failed.
        /// </summary>
        [JsonStringEnumMemberName("invalid")]
        Invalid,
        /// <summary>
        /// The authorization has been deactivated.
        /// </summary>
        [JsonStringEnumMemberName("deactivated")]
        Deactivated,
        /// <summary>
        /// The authorization has expired.
        /// </summary>
        [JsonStringEnumMemberName("expired")]
        Expired,
        /// <summary>
        /// The authorization has been revoked.
        /// </summary>
        [JsonStringEnumMemberName("revoked")]
        Revoked
    }
}
