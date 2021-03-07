using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Providers
{
    public class StatusHubMessages
    {
        public const string SendProgressStateMsg = "SendProgressState";
        public const string SendManagedCertificateUpdateMsg = "SendManagedCertificateUpdate";
        public const string SendMsg = "SendMessage";
    }
    public interface IStatusReporting
    {
        Task ReportRequestProgress(RequestProgressState status);
        Task ReportManagedCertificateUpdated(ManagedCertificate item);

    }
}
