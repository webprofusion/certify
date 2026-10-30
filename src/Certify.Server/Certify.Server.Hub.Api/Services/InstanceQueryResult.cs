namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// How an instance answered a query the hub sent it, so a caller can say why nothing usable came back
    /// </summary>
    public enum InstanceQueryOutcome
    {
        /// <summary>
        /// The instance replied
        /// </summary>
        Replied,

        /// <summary>
        /// The instance id is not one registered with this hub
        /// </summary>
        InstanceNotRegistered,

        /// <summary>
        /// The instance is registered but not connected, so the query could not be sent
        /// </summary>
        InstanceNotConnected,

        /// <summary>
        /// The query was sent but no reply arrived in time
        /// </summary>
        NoResponse,

        /// <summary>
        /// The instance replied that it does not hold the requested item
        /// </summary>
        ItemNotFound
    }

    /// <summary>
    /// The reply to a query sent to an instance, or why there was none
    /// </summary>
    /// <param name="Outcome">how the instance answered</param>
    /// <param name="Value">the reply, when the instance gave one</param>
    /// <param name="Message">why there is no reply, or any reason the instance gave with it</param>
    public record InstanceQueryResult<T>(InstanceQueryOutcome Outcome, T? Value, string? Message = null)
    {
        public bool IsReplied => Outcome == InstanceQueryOutcome.Replied;
    }
}
