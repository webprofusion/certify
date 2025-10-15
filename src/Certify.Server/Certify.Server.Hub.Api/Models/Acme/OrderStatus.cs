using System.Text.Json.Serialization;

namespace Certify.Server.Hub.Api.Models.Acme
{
    /// <summary>
    /// Represents the status of an ACME order.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OrderStatus
    {
        /// <summary>
        /// The order is pending and awaiting further action.
        /// </summary>
        [JsonStringEnumMemberName("pending")]
        Pending,

        /// <summary>
        /// The order is ready for finalization.
        /// </summary>
        [JsonStringEnumMemberName("ready")]
        Ready,

        /// <summary>
        /// The order is currently being processed.
        /// </summary>
        [JsonStringEnumMemberName("processing")]
        Processing,

        /// <summary>
        /// The order is valid and has been completed successfully.
        /// </summary>
        [JsonStringEnumMemberName("valid")]
        Valid,

        /// <summary>
        /// The order is invalid and cannot be completed.
        /// </summary>
        [JsonStringEnumMemberName("invalid")]
        Invalid,

        /// <summary>
        /// The order is ready for internal finalization.
        /// </summary>
        [JsonStringEnumMemberName("internal-finalization-ready")]
        ReadyForInternalFinalization,

        /// <summary>
        /// The order is in progress for internal finalization.
        /// </summary>
        [JsonStringEnumMemberName("internal-finalization-inprogress")]
        InternalFinalizationInProgress
    }
}
