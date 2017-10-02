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
            _vaultManager.UseEFSForSensitiveFiles = false;
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
            var identList = _vaultManager.GetIdentifiers(reloadVaultConfig: true);
            var list = new List<IdentifierItem>();

            foreach (var r in identList)
            {
                list.Add(_vaultManager.GetDomainIdentifierItemFromIdentifierInfo(r));
            }

            return list;
        }

        public string ComputeDomainIdentifierId(string domain)
        {
            return _vaultManager.ComputeIdentifierAlias(domain);
        }

        public IdentifierItem GetDomainIdentifier(string domain)
        {
            var identifier = _vaultManager.GetIdentifier(domain.Trim().ToLower());
            if (identifier != null)
            {
                return _vaultManager.GetDomainIdentifierItemFromIdentifierInfo(identifier);
            }
            else
            {
                return null;
            }
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

        public string GetActionSummary()
        {
            return _vaultManager.GetActionLogSummary();
        }

        public void EnableSensitiveFileEncryption()
        {
            _vaultManager.UseEFSForSensitiveFiles = true;
        }

        public PendingAuthorization BeginRegistrationAndValidation(CertRequestConfig config, string domainIdentifierId, string challengeType, string domain)
        {
            return _vaultManager.BeginRegistrationAndValidation(config, domainIdentifierId, challengeType, domain);
        }

        public PendingAuthorization PerformIISAutomatedChallengeResponse(IISManager iisManager, ManagedSite managedSite, PendingAuthorization pendingAuth)
        {
            var processedAuth = _vaultManager.PerformIISAutomatedChallengeResponse(iisManager, managedSite, pendingAuth);
            if (_vaultManager.ActionLogs != null)
            {
                processedAuth.LogItems = new List<string>();
                foreach (var a in _vaultManager.ActionLogs)
                {
                    processedAuth.LogItems.Add(a.Result);
                }
            }
            return processedAuth;
        }

        public async Task<APIResult> TestChallengeResponse(IISManager iisManager, ManagedSite managedSite)
        {
            return await _vaultManager.TestChallengeResponse(iisManager, managedSite);
        }

        public void SubmitChallenge(string domainIdentifierId, string challengeType)
        {
            _vaultManager.SubmitChallenge(domainIdentifierId, challengeType);
        }

        public bool CompleteIdentifierValidationProcess(string alias)
        {
            return _vaultManager.CompleteIdentifierValidationProcess(alias);
        }

        public ProcessStepResult PerformCertificateRequestProcess(string primaryDnsIdentifier, string[] alternativeDnsIdentifiers)
        {
            return _vaultManager.PerformCertificateRequestProcess(primaryDnsIdentifier, alternativeDnsIdentifiers);
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