using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Shared;

#nullable disable

namespace Certify.Models.Providers
{
    public interface IACMEClientProvider
    {
        string GetProviderName();

        string GetAcmeBaseURI();

        Task<bool> InitProvider(ILog log = null, AccountDetails account = null);

        Task<AcmeDirectoryInfo> GetAcmeDirectory();

        Task<string> GetAcmeAccountStatus();

        Task<ActionResult<AccountDetails>> AddNewAccountAndAcceptTOS(ILog log, string email, string eabKeyId, string eabKey, string eabKeyAlg);

        Task<bool> DeactivateAccount(ILog log);

        Task<ActionResult<AccountDetails>> UpdateAccount(ILog log, string email, bool termsAgreed);

        Task<PendingOrder> BeginCertificateOrder(ILog log, CertRequestConfig config, string orderUri = null);

        Task<StatusMessage> SubmitChallenge(ILog log, string challengeType, PendingAuthorization pendingAuthorization);

        Task<PendingAuthorization> CheckValidationCompleted(ILog log, string challengeType, PendingAuthorization pendingAuthorization);

        Task<ProcessStepResult> CompleteCertificateRequest(ILog log, CertRequestConfig config, string orderId, string pwd, string preferredChain, bool uesModernPFXBuildAlgs);

        Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate);

        Task<bool> ChangeAccountKey(ILog log);

        Task<RenewalInfo> GetRenewalInfo(string certificateId);
    }
}
