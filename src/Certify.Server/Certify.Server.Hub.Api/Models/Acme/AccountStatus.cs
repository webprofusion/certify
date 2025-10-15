using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents the status of an ACME account.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AccountStatus
    {
        /// <summary>
        /// The account is valid and active.
        /// </summary>
        [JsonStringEnumMemberName("valid")]
        Valid,

        /// <summary>
        /// The account has been deactivated.
        /// </summary>
        [JsonStringEnumMemberName("deactivated")]
        Deactivated,

        /// <summary>
        /// The account has been revoked.
        /// </summary>
        [JsonStringEnumMemberName("revoked")]
        Revoked
    }
}
