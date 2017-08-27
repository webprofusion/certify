using Certes;
using Certes.Acme;
using Certify.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management.APIProviders
{
    public class CertesProvider : IACMEClientProvider, IVaultProvider
    {
        private AcmeClient _client;

        public CertesProvider()
        {
            _client = new AcmeClient(WellKnownServers.LetsEncrypt);
        }

        public List<RegistrationItem> GetContactRegistrations()
        {
            System.Diagnostics.Debug.WriteLine("Certes: GetContactRegistration not implemented");
            return new List<RegistrationItem>();
        }

        public void DeleteContactRegistration(string id)
        {
            System.Diagnostics.Debug.WriteLine("Certes: DeleteContactRegistration not implemented");
        }

        public List<IdentifierItem> GetDomainIdentifiers()
        {
            System.Diagnostics.Debug.WriteLine("Certes: GetDomainIdentifiers not implemented");
            return new List<IdentifierItem>();
        }

        public List<CertificateItem> GetCertificates()
        {
            System.Diagnostics.Debug.WriteLine("Certes: GetCertificates not implemented");
            return new List<CertificateItem>();
        }

        public bool HasRegisteredContacts()
        {
            System.Diagnostics.Debug.WriteLine("Certes: HasRegisteredContacts not implemented");
            return false;
        }

        public string GetVaultSummary()
        {
            System.Diagnostics.Debug.WriteLine("Certes: GetVaultSummary not implemented");
            return null;
        }

        #region IACMEClientProvider methods

        public bool AddNewRegistrationAndAcceptTOS(string email)
        {
            Task.Run(async () =>
            {
                // Create new registration
                var account = await _client.NewRegistraton("mailto:" + email);

                // Accept terms of services
                account.Data.Agreement = account.GetTermsOfServiceUri();
                account = await _client.UpdateRegistration(account);
            });

            return true;
        }

        public string GetAcmeBaseURI()
        {
            System.Diagnostics.Debug.WriteLine("Certes: GetAcmeBaseURI not implemented");
            return null;
        }

        #endregion IACMEClientProvider methods
    }
}