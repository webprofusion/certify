using Certify.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management.APIProviders
{
    public class ACMESharpProvider : IACMEClientProvider, IVaultProvider
    {
        private VaultManager _vaultManager;

        public ACMESharpProvider()
        {
            _vaultManager = new VaultManager(Properties.Settings.Default.VaultPath, ACMESharp.Vault.Providers.LocalDiskVault.VAULT);
        }

        public List<RegistrationItem> GetContactRegistrations()
        {
            var reg = _vaultManager.GetRegistrations(reloadVaultConfig: true);
            var list = new List<RegistrationItem>();

            foreach (var r in reg)
            {
                list.Add(new RegistrationItem { Id = r.Id.ToString(), Name = r.Registration.Contacts.First(), Contacts = r.Registration.Contacts });
            }

            return list;
        }

        public void DeleteContactRegistration(string id)
        {
            _vaultManager.DeleteRegistrationInfo(Guid.Parse(id));
        }

        public List<IdentifierItem> GetDomainIdentifiers()
        {
            var reg = _vaultManager.GetIdentifiers(reloadVaultConfig: true);
            var list = new List<IdentifierItem>();

            foreach (var r in reg)
            {
                list.Add(new IdentifierItem { Id = r.Id.ToString(), Name = r.Dns, Dns = r.Dns, Status = r.Authorization?.Status });
            }

            return list;
        }

        public List<CertificateItem> GetCertificates()
        {
            var certs = _vaultManager.GetCertificates(reloadVaultConfig: true);
            var list = new List<CertificateItem>();

            foreach (var i in certs)
            {
                list.Add(new CertificateItem { Id = i.Id.ToString(), Name = i.IdentifierDns });
            }

            return list;
        }

        public bool HasRegisteredContacts()
        {
            return _vaultManager.HasContacts(true);
        }

        public string GetVaultSummary()
        {
            return _vaultManager.GetVaultPath();
        }

        #region IACMEClientProvider methods

        public bool AddNewRegistrationAndAcceptTOS(string email)
        {
            return _vaultManager.AddNewRegistrationAndAcceptTOS("mailto:" + email);
        }

        public string GetAcmeBaseURI()
        {
            return _vaultManager.GetACMEBaseURI();
        }

        #endregion IACMEClientProvider methods
    }
}