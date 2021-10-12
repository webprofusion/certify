using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Models.Utils;

namespace Certify.UI.ViewModel
{
    public partial class AppViewModel : BindableBase
    {
        /// <summary>
        /// Cached list of known certificate authorities
        /// </summary>
        public ObservableCollection<CertificateAuthority> CertificateAuthorities = new ObservableCollection<CertificateAuthority>();

        /// <summary>
        /// Refresh cached list of Certificate Authorities
        /// </summary>
        /// <returns></returns>
        public async Task RefreshCertificateAuthorityList()
        {
            var list = await _certifyClient.GetCertificateAuthorities();

            CertificateAuthorities.Clear();

            foreach (var a in list)
            {
                CertificateAuthorities.Add(a);
            }

            RaisePropertyChangedEvent(nameof(CertificateAuthorities));
        }

        /// <summary>
        /// Save updated details for a certificate authority via service
        /// </summary>
        /// <param name="ca"></param>
        /// <returns></returns>
        public async Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority ca)
        {
            var result = await _certifyClient.UpdateCertificateAuthority(ca);

            if (result.IsSuccess)
            {
                await this.RefreshCertificateAuthorityList();
            }
            return result;
        }

        /// <summary>
        /// Delete specific certificate authority via service
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<ActionResult> DeleteCertificateAuthority(string id)
        {
            var result = await _certifyClient.DeleteCertificateAuthority(id);

            if (result.IsSuccess)
            {
                await this.RefreshCertificateAuthorityList();
            }
            return result;
        }

        /// <summary>
        /// Cached list of current ACME accounts
        /// </summary>
        public ObservableCollection<AccountDetails> AccountDetails = new ObservableCollection<AccountDetails>();

        /// <summary>
        /// Refresh cached list of ACME accounts
        /// </summary>
        /// <returns></returns>
        public async Task RefreshAccountsList()
        {
            var list = await _certifyClient.GetAccounts();

            AccountDetails.Clear();

            var tmpList = new List<AccountDetails>();
            foreach (var a in list)
            {
                var ca = CertificateAuthorities.FirstOrDefault(c => c.Id == a.CertificateAuthorityId);
                a.Title = $"{ca?.Title ?? "[Unknown CA]"} [{(a.IsStagingAccount ? "Staging" : "Production")}]";
                tmpList.Add(a);
            }

            tmpList
                .OrderBy(t => t.Title)
                .ToList()
                .ForEach(a => AccountDetails.Add(a));
        }

        /// <summary>
        /// True if one or more ACME accounts registered
        /// </summary>
        public virtual bool HasRegisteredContacts => AccountDetails.Any();

        /// <summary>
        /// Add ACME contaxt registration via service
        /// </summary>
        /// <param name="reg"></param>
        /// <returns></returns>
        internal async Task<ActionResult> AddContactRegistration(ContactRegistration reg)
        {
            try
            {
                var result = await _certifyClient.AddAccount(reg);

                RaisePropertyChangedEvent(nameof(HasRegisteredContacts));
                return result;
            }
            catch (Exception exp)
            {
                return new ActionResult("Contact Registration could not be completed. [" + exp.Message + "]", false);
            }
        }

        /// <summary>
        /// Remove stored ACME account registration and refresh cached list
        /// </summary>
        /// <param name="storageKey"></param>
        /// <returns></returns>
        internal async Task<ActionResult> RemoveAccount(string storageKey)
        {
            var result = await _certifyClient.RemoveAccount(storageKey);

            await RefreshAccountsList();
            RaisePropertyChangedEvent(nameof(HasRegisteredContacts));

            return result;
        }

        /* Stored Credentials */

        private object _storedCredentialsLock = new object();

        /// <summary>
        /// Cached collection of stored credentials
        /// </summary>
        public ObservableCollection<StoredCredential> StoredCredentials { get; set; }

        /// <summary>
        /// Update a given stored credential via service
        /// </summary>
        /// <param name="credential"></param>
        /// <returns></returns>
        public async Task<StoredCredential> UpdateCredential(StoredCredential credential)
        {
            var result = await _certifyClient.UpdateCredentials(credential);
            await RefreshStoredCredentialsList();

            return result;
        }

        /// <summary>
        /// Delete a given credential via service
        /// </summary>
        /// <param name="credentialKey"></param>
        /// <returns></returns>
        public async Task<bool> DeleteCredential(string credentialKey)
        {
            if (credentialKey == null)
            {
                return false;
            }

            var result = await _certifyClient.DeleteCredential(credentialKey);
            await RefreshStoredCredentialsList();

            return result;
        }

        /// <summary>
        /// Test a given credential (if the credential type supports it)
        /// </summary>
        /// <param name="credentialKey"></param>
        /// <returns></returns>
        public async Task<ActionResult> TestCredentials(string credentialKey)
        {
            var result = await _certifyClient.TestCredentials(credentialKey);

            return result;
        }

        /// <summary>
        /// Refresh cached list of stored credentials
        /// </summary>
        /// <returns></returns>
        public async Task RefreshStoredCredentialsList()
        {
            var list = await _certifyClient.GetCredentials();

            if (StoredCredentials == null)
            {
                StoredCredentials = new ObservableCollection<StoredCredential>();
                System.Windows.Data.BindingOperations.EnableCollectionSynchronization(StoredCredentials, _storedCredentialsLock);
            }


            StoredCredentials.Clear();
            foreach (var c in list)
            {
                StoredCredentials.Add(c);
            }

            RaisePropertyChangedEvent(nameof(StoredCredentials));
        }

        /// <summary>
        /// Cached list of challenge API providers (DNS providers etc)
        /// </summary>
        public ObservableCollection<ChallengeProviderDefinition> ChallengeAPIProviders { get; set; } = new ObservableCollection<ChallengeProviderDefinition> { };

        /// <summary>
        /// Refresh cached list of challenge API providers via service
        /// </summary>
        /// <returns></returns>
        public async Task RefreshChallengeAPIList()
        {
            var list = await _certifyClient.GetChallengeAPIList();
            System.Windows.Application.Current.Dispatcher.Invoke(delegate
            {
                ChallengeAPIProviders = new ObservableCollection<ChallengeProviderDefinition>(list);
            });
        }
        /// <summary>
        /// Get list of DNS zone for a given DNS provider and credentials key
        /// </summary>
        /// <param name="challengeProvider"></param>
        /// <param name="challengeCredentialKey"></param>
        /// <returns></returns>
        public async Task<List<DnsZone>> GetDnsProviderZones(string challengeProvider, string challengeCredentialKey) => await _certifyClient.GetDnsProviderZones(challengeProvider, challengeCredentialKey);

        /// <summary>
        /// Cached list of Deployment Task types
        /// </summary>
        public ObservableCollection<DeploymentProviderDefinition> DeploymentTaskProviders { get; set; } = new ObservableCollection<DeploymentProviderDefinition> { };

        /// <summary>
        /// Refresh cached list of deployment task types via service
        /// </summary>
        /// <returns></returns>
        public async Task RefreshDeploymentTaskProviderList()
        {
            var list = await _certifyClient.GetDeploymentProviderList();
            System.Windows.Application.Current.Dispatcher.Invoke(delegate
            {
                DeploymentTaskProviders = new ObservableCollection<DeploymentProviderDefinition>(list.OrderBy(l => l.Title));
            });
        }

        /// <summary>
        /// Get a specific deployment task provider definition dynamically
        /// </summary>
        /// <returns></returns>
        public async Task<DeploymentProviderDefinition> GetDeploymentTaskProviderDefinition(string id, Config.DeploymentTaskConfig config = null)
        {
            var definition = await _certifyClient.GetDeploymentProviderDefinition(id, config);
            if (definition != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(delegate
                {

                    var orig = DeploymentTaskProviders.FirstOrDefault(i => i.Id == definition.Id);
                    var index = DeploymentTaskProviders.IndexOf(orig);

                    if (orig != null)
                    {
                        DeploymentTaskProviders.Remove(orig);
                    }

                    // replace definition in list
                    DeploymentTaskProviders.Insert(index >= 0 ? index : 0, definition);
                });
            }

            return definition;
        }

        /// <summary>
        /// Perform validation for a given deployment task configuration via the providers own validation method
        /// </summary>
        /// <param name="deploymentTaskValidationInfo"></param>
        /// <returns></returns>
        public async Task<List<ActionResult>> ValidateDeploymentTask(DeploymentTaskValidationInfo deploymentTaskValidationInfo) => await _certifyClient.ValidateDeploymentTask(deploymentTaskValidationInfo);
    }
}
