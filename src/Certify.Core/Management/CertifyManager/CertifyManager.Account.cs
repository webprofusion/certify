using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Newtonsoft.Json;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        /// <summary>
        /// Get the applicable Account Details for this managed item
        /// </summary>
        /// <param name="item"></param>
        /// <returns>Account Details or null if there is no matching account</returns>
        private async Task<AccountDetails> GetAccountDetailsForManagedItem(ManagedCertificate item)
        {
            // determine the current current contact
            string currentCA = CoreAppSettings.Current.DefaultCertificateAuthority ?? StandardCertAuthorities.LETS_ENCRYPT;
            if (!string.IsNullOrEmpty(item.CertificateAuthorityId))
            {
                currentCA = item.CertificateAuthorityId;
            }

            var accounts = await GetAccountRegistrations();

            // get current account details for this CA (depending on whether this managed certificate uses staging mode or not)
            var account = accounts.FirstOrDefault(a => a.CertificateAuthorityId == currentCA && a.IsStagingAccount == item.UseStagingMode);

            return account;

        }
        private async Task<AccountDetails> GetAccountDetailsFromCredential(Models.Config.StoredCredential credential)
        {
            var json = await _credentialsManager.GetUnlockedCredential(credential.StorageKey);

            if (json != null)
            {
                var account = JsonConvert.DeserializeObject<AccountDetails>(json);

                if (account.CertificateAuthorityId == null)
                {
                    account.CertificateAuthorityId = StandardCertAuthorities.LETS_ENCRYPT;
                }

                return account;
            }
            else
            {
                return null;
            }
        }

        public async Task<List<AccountDetails>> GetAccountRegistrations()
        {
            var list = new List<AccountDetails>();

            var acmeCredentials = await _credentialsManager.GetStoredCredentials(StandardAuthTypes.STANDARD_ACME_ACCOUNT);
            if (acmeCredentials.Any())
            {
             
                // got stored acme accounts
                foreach (var c in acmeCredentials)
                {
                    var acc = await GetAccountDetailsFromCredential(c);
                    if (acc != null)
                    {
                        list.Add(acc);
                    }
                }
               
            }
            else
            {
                // no stored acme accounts, need to migrate or there are no existing accounts
            }

            return list;
        }

        public async Task<Models.Config.ActionResult> AddAccount(ContactRegistration reg)
        {
            // there is one registered contact per account type (per CA, Prod or Staging)

            // attempt to register the new contact
            if (reg.AgreedToTermsAndConditions)
            {
                _serviceLog?.Information($"Registering contact with ACME CA {_acmeClientProvider.GetAcmeBaseURI()}]: {reg.EmailAddress}");

                var addAccountResult= await _acmeClientProvider.AddNewAccountAndAcceptTOS(_serviceLog, reg.EmailAddress);
                if (addAccountResult.IsSuccess)
                {

                    // store new account details as credentials
                    await StoreAccountAsCredential(addAccountResult.Result);
                }

                return addAccountResult;
            }
            else
            {
                // did not agree to terms
                return new Models.Config.ActionResult { IsSuccess = false, Message = "You must agree to the terms and conditions of the Certificate Authority to register with them." };
            }
        }

        private async Task StoreAccountAsCredential(AccountDetails account)
        {
            await _credentialsManager.UpdateCredential(new Models.Config.StoredCredential
            {
                StorageKey = Guid.NewGuid().ToString(),
                ProviderType = StandardAuthTypes.STANDARD_ACME_ACCOUNT,
                Secret = JsonConvert.SerializeObject(account),
                Title = $"{account.CertificateAuthorityId}_{(account.IsStagingAccount?"Staging":"Production")}"
            });
        }

        private async Task PerformAccountUpgrades()
        {
            // check if there are no registered contacts, if so see if we are upgrading from a vault

            var accounts = await GetAccountRegistrations();

            if (accounts.Count() == 0)
            {
                // if we have no accounts we need to check for required upgrades
                // contacts may be JSON or legacy vault 

                if (_acmeClientProvider is Providers.ACME.Certes.CertesACMEProvider)
                {
                    var provider = (Providers.ACME.Certes.CertesACMEProvider)_acmeClientProvider;
                    var acc = provider.GetCurrentAcmeAccount();
                    acc.CertificateAuthorityId = StandardCertAuthorities.LETS_ENCRYPT;
                    accounts.Add(acc);

                    await StoreAccountAsCredential(acc);
                }

                if (accounts.Count() == 0)
                {
                    // still no accounts, check for old vault upgrade
                    var acmeVaultMigration = new Models.Compat.ACMEVaultUpgrader();

                    if (acmeVaultMigration.HasACMEVault())
                    {
                        var email = acmeVaultMigration.GetContact();

                        if (!string.IsNullOrEmpty(email))
                        {
                            var addedOK = await _acmeClientProvider.AddNewAccountAndAcceptTOS(_serviceLog, email);

                            _serviceLog?.Information("Account upgrade completed (vault)");
                        }
                    }
                }
            }
        }
    }
}
