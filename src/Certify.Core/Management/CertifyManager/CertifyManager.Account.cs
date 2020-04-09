using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Providers.ACME.Certes;
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
            var matchingAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == currentCA && a.IsStagingAccount == item.UseStagingMode);

            if (matchingAccount == null)
            {
                var log = ManagedCertificateLog.GetLogger(item.Id, new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Error));
                log?.Error($"Failed to match ACME account for managed certificate. Cannot continue request. :: {item.Name} CA: {currentCA} {(item.UseStagingMode ? "[Staging Mode]" : "[Production]")}");
            }

            return matchingAccount;
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

                // new provider needed for this new account
                var storageKey = Guid.NewGuid().ToString();

                _certificateAuthorities.TryGetValue(reg.CertificateAuthorityId, out var certAuthority);

                if (certAuthority == null)
                {
                    return new ActionResult("Invalid Certificate Authority specified.", false);
                }

                var acmeProvider = await GetACMEProvider(storageKey, reg.IsStaging ? certAuthority.StagingAPIEndpoint : certAuthority.ProductionAPIEndpoint);

                _serviceLog?.Information($"Registering account with ACME CA {acmeProvider.GetAcmeBaseURI()}]: {reg.EmailAddress}");

                var addAccountResult = await acmeProvider.AddNewAccountAndAcceptTOS(_serviceLog, reg.EmailAddress);

                if (addAccountResult.IsSuccess)
                {
                    // store new account details as credentials
                    addAccountResult.Result.ID = storageKey;
                    addAccountResult.Result.StorageKey = storageKey;
                    addAccountResult.Result.CertificateAuthorityId = certAuthority.Id;
                    addAccountResult.Result.IsStagingAccount = reg.IsStaging;

                    await StoreAccountAsCredential(addAccountResult.Result);
                }

                return addAccountResult;
            }
            else
            {
                // did not agree to terms
                return new ActionResult("You must agree to the terms and conditions of the Certificate Authority to register with them.", false);
            }
        }

        private async Task StoreAccountAsCredential(AccountDetails account)
        {
            await _credentialsManager.UpdateCredential(new Models.Config.StoredCredential
            {
                StorageKey = account.ID ?? Guid.NewGuid().ToString(),
                ProviderType = StandardAuthTypes.STANDARD_ACME_ACCOUNT,
                Secret = JsonConvert.SerializeObject(account),
                Title = $"{account.CertificateAuthorityId}_{(account.IsStagingAccount ? "Staging" : "Production")}"
            });
        }

        public async Task<ActionResult> RemoveAccount(string storageKey)
        {

            var accounts = await GetAccountRegistrations();
            var account = accounts.FirstOrDefault(a => a.StorageKey == storageKey);
            if (account != null)
            {
                _serviceLog?.Information($"Deleting account {storageKey}: " + account.AccountURI);

                var resultOk = await _credentialsManager.DeleteCredential(storageKey);
                return new ActionResult("RemoveAccount", resultOk);
            }
            else
            {
                return new ActionResult("Account not found.", false);
            }
        }

        private async Task PerformAccountUpgrades()
        {
            // check if there are no registered contacts, if so see if we are upgrading from a vault

            var accounts = await GetAccountRegistrations();

            if (!accounts.Any())
            {
                // if we have no accounts we need to check for required upgrades
                // contacts may be JSON or legacy vault 

                // create provider pointing to legacy storage
                var apiEndpoint = _certificateAuthorities[StandardCertAuthorities.LETS_ENCRYPT].ProductionAPIEndpoint;
                var provider = new CertesACMEProvider(apiEndpoint, Management.Util.GetAppDataFolder() + "\\certes", Util.GetUserAgent());
                await provider.InitProvider(_serviceLog);

                var acc = (provider as CertesACMEProvider).GetCurrentAcmeAccount();
                if (acc != null)
                {
                    // we have a legacy certes account to migrate to the newer account store
                    var newId = Guid.NewGuid().ToString();
                    acc.ID = newId;
                    acc.StorageKey = newId;
                    acc.IsStagingAccount = false;
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
                            var registerResult = await provider.AddNewAccountAndAcceptTOS(_serviceLog, email);
                            if (registerResult.IsSuccess)
                            {
                                var newId = Guid.NewGuid().ToString();
                                acc = registerResult.Result;
                                acc.ID = newId;
                                acc.StorageKey = newId;
                                acc.IsStagingAccount = false;
                                acc.CertificateAuthorityId = StandardCertAuthorities.LETS_ENCRYPT;
                                accounts.Add(acc);
                                await StoreAccountAsCredential(acc);
                                _serviceLog?.Information("Account upgrade completed (vault)");
                            }
                            else
                            {
                                _serviceLog?.Information($"Account upgrade failed (vault):{registerResult?.Message}");
                            }

                        }
                    }
                }
            }
        }

        public async Task<List<CertificateAuthority>> GetCertificateAuthorities()
        {
            return _certificateAuthorities.Values.ToList();
        }
    }
}
