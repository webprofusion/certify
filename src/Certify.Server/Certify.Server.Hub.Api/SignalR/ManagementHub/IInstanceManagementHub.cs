using Certify.Models.Hub;

namespace Certify.Server.Hub.Api.SignalR.ManagementHub
{
    /// <summary>
    /// Interface for instance management hub events
    /// </summary>
    public interface IInstanceManagementHub
    {
        /// <summary>
        /// Send command to an instance or the current caller if instance not provided
        /// </summary>
        /// <param name="cmd"></param>
        /// <returns></returns>
        Task SendCommandRequest(InstanceCommandRequest cmd);

        /// <summary>
        /// Receive command result from an instance
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        Task ReceiveCommandResult(InstanceCommandResult result);

        /// <summary>
        /// Receive adhoc message from an instance
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        Task ReceiveInstanceMessage(InstanceMessage message);
    }
}
