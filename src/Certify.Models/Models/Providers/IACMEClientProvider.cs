using Certify.Models.Config;
using Certify.Models.Providers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Models.Plugins
{
    public interface IACMEClientProvider
    {
        string GetProviderName();

        string GetAcmeBaseURI();

        Task<bool> AddNewAccountAndAcceptTOS(ILog log, string email);

        Task<PendingOrder> BeginCertificateOrder(ILog log, CertRequestConfig config);

        Task<StatusMessage> SubmitChallenge(ILog log, string challengeType, AuthorizationChallengeItem attemptedChallenge);

        Task<PendingAuthorization> CheckValidationCompleted(ILog log, string challengeType, PendingAuthorization pendingAuthorization);

        Task<ProcessStepResult> CompleteCertificateRequest(ILog log, CertRequestConfig config, string orderId);

        Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate);
    }

    public class PendingOrder
    {
        public List<PendingAuthorization> Authorizations { get; set; }
        public string OrderUri { get; set; }
    }
}