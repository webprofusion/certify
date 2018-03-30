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

        Task<List<PendingAuthorization>> BeginRegistrationAndValidation(ILog log, CertRequestConfig config);

        Task<StatusMessage> SubmitChallenge(ILog log, string challengeType, AuthorizationChallengeItem attemptedChallenge);

        Task<PendingAuthorization> CheckValidationCompleted(ILog log, string challengeType, PendingAuthorization pendingAuthorization);

        Task<ProcessStepResult> PerformCertificateRequestProcess(ILog log, string primaryDnsIdentifier, string[] alternativeDnsIdentifiers, CertRequestConfig config);

        Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate);
    }
}