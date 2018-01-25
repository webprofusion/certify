using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Providers
{
    [Export(typeof(IACMEClientProvider))]
    [Export(typeof(IVaultProvider))]
    public class ACMESharpProvider : ActionLogCollector, IACMEClientProvider, IVaultProvider
    {
        private ACMESharpManager _vaultManager;

        public ACMESharpProvider()
        {
        }

        public string GetProviderName()
        {
            return "ACMESharp";
        }

        public void InitProvider(string settingsPath)
        {
            _vaultManager = new ACMESharpManager(settingsPath);
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

        public List<string> GetActionSummary()
        {
            return _vaultManager.GetActionLogSummary();
        }

        public void EnableSensitiveFileEncryption()
        {
            _vaultManager.UseEFSForSensitiveFiles = true;
        }

        public async Task<PendingAuthorization> BeginRegistrationAndValidation(CertRequestConfig config, string domainIdentifierId, string challengeType, string domain)
        {
            return await Task.FromResult(_vaultManager.BeginRegistrationAndValidation(config, domainIdentifierId, challengeType, domain));
        }

        public async Task<StatusMessage> SubmitChallenge(string domainIdentifierId, string challengeType, AuthorizationChallengeItem attemptedChallenge)
        {
            try
            {
                var state = _vaultManager.SubmitChallenge(domainIdentifierId, challengeType);

                return await Task.FromResult(new StatusMessage
                {
                    IsOK = true,
                    Message = "Submitted"
                });
            }
            catch (Exception exp)
            {
                return await Task.FromResult(new StatusMessage
                {
                    IsOK = false,
                    Message = exp.Message,
                    Result = exp
                });
            }
        }

        public Task<PendingAuthorization> CheckValidationCompleted(string alias, PendingAuthorization pendingAuthorization)
        {
            var valid = _vaultManager.CompleteIdentifierValidationProcess(alias);

            // update identifier status
            pendingAuthorization.Identifier = GetDomainIdentifier(alias);
            return Task.FromResult(pendingAuthorization);
        }

        public Task<ProcessStepResult> PerformCertificateRequestProcess(string primaryDnsIdentifier, string[] alternativeDnsIdentifiers, CertRequestConfig config)
        {
            return Task.FromResult(_vaultManager.PerformCertificateRequestProcess(primaryDnsIdentifier, alternativeDnsIdentifiers));
        }

        public async Task<StatusMessage> RevokeCertificate(ManagedSite managedSite)
        {
            return await _vaultManager.RevokeCertificate(managedSite.CertificatePath);
        }

        #region IACMEClientProvider methods

        public Task<bool> AddNewAccountAndAcceptTOS(string email)
        {
            return Task.FromResult(_vaultManager.AddNewRegistrationAndAcceptTOS("mailto:" + email));
        }

        public string GetAcmeBaseURI()
        {
            return _vaultManager.GetACMEBaseURI();
        }

        public void PerformVaultCleanup()
        {
            _vaultManager.CleanupVault();
        }

        #endregion IACMEClientProvider methods
    }
}