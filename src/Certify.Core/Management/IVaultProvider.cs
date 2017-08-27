using Certify.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    }
}