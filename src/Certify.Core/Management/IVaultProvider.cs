using Certify.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Management
{
    public interface IVaultProvider
    {
        List<RegistrationItem> GetContactRegistrations();

        List<IdentifierItem> GetDomainIdentifiers();

        List<CertificateItem> GetCertificates();

        bool HasRegisteredContacts();

        void DeleteContactRegistration(string id);

        string GetVaultSummary();

        ActionLogItem GetLastActionLogItem();

        List<string> GetActionSummary();

        void EnableSensitiveFileEncryption();

        string ComputeDomainIdentifierId(string domain);

        IdentifierItem GetDomainIdentifier(string domain);

        PendingAuthorization BeginRegistrationAndValidation(CertRequestConfig config, string domainIdentifierId, string challengeType, string domain);

        PendingAuthorization PerformIISAutomatedChallengeResponse(IISManager iisManager, ManagedSite managedSite, PendingAuthorization pendingAuth);

        Task<APIResult> TestChallengeResponse(IISManager iisManager, ManagedSite managedSite, bool isPreviewMode);

        void SubmitChallenge(string domainIdentifierId, string challengeType);

        bool CompleteIdentifierValidationProcess(string alias);

        ProcessStepResult PerformCertificateRequestProcess(string primaryDnsIdentifier, string[] alternativeDnsIdentifiers);

        Task<APIResult> RevokeCertificate(ManagedSite managedSite);

        void PerformVaultCleanup();
    }
}