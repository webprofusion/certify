using Certify.Models.Config;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Models.Plugins
{
    public interface IACMEClientProvider
    {
        string GetProviderName();

        Task<bool> AddNewAccountAndAcceptTOS(string email);

        string GetAcmeBaseURI();

        string ComputeDomainIdentifierId(string domain);

        Task<List<PendingAuthorization>> BeginRegistrationAndValidation(CertRequestConfig config, string domainIdentifierId, string challengeType, string domain);

        Task<StatusMessage> SubmitChallenge(string domainIdentifierId, string challengeType, AuthorizationChallengeItem attemptedChallenge);

        Task<PendingAuthorization> CheckValidationCompleted(string alias, PendingAuthorization pendingAuthorization);

        Task<ProcessStepResult> PerformCertificateRequestProcess(string primaryDnsIdentifier, string[] alternativeDnsIdentifiers, CertRequestConfig config);

        Task<StatusMessage> RevokeCertificate(ManagedSite managedSite);

        ActionLogItem GetLastActionLogItem();
    }
}