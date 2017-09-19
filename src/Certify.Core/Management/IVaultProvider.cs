using Certify.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACMESharp.Vault.Model;

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

        void EnableSensitiveFileEncryption();

        string ComputeDomainIdentifierId(string domain);

        IdentifierItem GetDomainIdentifier(string domain);

        PendingAuthorization BeginRegistrationAndValidation(CertRequestConfig config, string domainIdentifierId, string challengeType, string domain);

        PendingAuthorization PerformIISAutomatedChallengeResponse(IISManager iisManager, ManagedSite managedSite, PendingAuthorization pendingAuth);

        void SubmitChallenge(string domainIdentifierId, string challengeType);

        bool CompleteIdentifierValidationProcess(string alias);

        ProcessStepResult PerformCertificateRequestProcess(string primaryDnsIdentifier, string[] alternativeDnsIdentifiers);
    }
}