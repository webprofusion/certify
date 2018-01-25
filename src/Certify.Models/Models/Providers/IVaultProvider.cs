using System.Collections.Generic;

namespace Certify.Models.Plugins
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

        void PerformVaultCleanup();
    }
}