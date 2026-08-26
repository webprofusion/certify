using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Reporting;

namespace Certify.Providers
{
    public class StatusHubMessages
    {
        public const string SendProgressStateMsg = "SendProgressState";
        public const string SendManagedCertificateUpdateMsg = "SendManagedCertificateUpdate";
        public const string SendMsg = "SendMessage";

        /// <summary>
        /// Sent as the first argument of <see cref="SendMsg"/> for a service diagnostic which needs operator
        /// action, with a serialized DiagnosticActionRequired as the second. Sent over the existing message
        /// channel so that clients already subscribed to it receive these without any change.
        /// </summary>
        public const string NotificationActionRequired = "NotificationActionRequired";
    }
    public interface IStatusReporting
    {
        Task ReportRequestProgress(RequestProgressState status);
        Task ReportManagedCertificateUpdated(ManagedCertificate item);

        /// <summary>
        /// Report a service level diagnostic which requires operator action, such as the data store being
        /// unreachable at startup.
        /// </summary>
        Task ReportDiagnosticActionRequired(DiagnosticActionRequired diagnostic);
    }
}
