using Certify.Models.Config;
using Certify.Models.Providers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Models.Plugins
{
    public interface IACMEClientProvider
    {
        string GetProviderName();

        Task<bool> AddNewAccountAndAcceptTOS(string email);

        string GetAcmeBaseURI();

        Task<List<PendingAuthorization>> BeginRegistrationAndValidation(ILog log, CertRequestConfig config, string domainIdentifierId, string domain);

        Task<StatusMessage> SubmitChallenge(ILog log, string domainIdentifierId, string challengeType, AuthorizationChallengeItem attemptedChallenge);

        Task<PendingAuthorization> CheckValidationCompleted(ILog log, string challengeType, PendingAuthorization pendingAuthorization);

        Task<ProcessStepResult> PerformCertificateRequestProcess(ILog log, string primaryDnsIdentifier, string[] alternativeDnsIdentifiers, CertRequestConfig config);

        Task<StatusMessage> RevokeCertificate(ManagedCertificate managedCertificate);
    }
}