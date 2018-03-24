using System.Collections.Generic;

namespace Certify.Models.Plugins
{
    public interface IVaultProvider
    {
        List<RegistrationItem> GetContactRegistrations();

        List<CertificateItem> GetCertificates();

        void DeleteContactRegistration(string id);

        void EnableSensitiveFileEncryption();
    }
}