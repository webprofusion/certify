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

            var accounts = await GetAccountRegistrations();

            if (item == null)
            {
                // if not using a specific managed item, get first account details we have
                var a = accounts
                    .Where(ac => ac.IsStagingAccount == false && !string.IsNullOrEmpty(ac.Email))
                    .FirstOrDefault();

                if (a != null)
                {
                    return a;
                }
                else
                {
                    // fallback to first staging account
                    return accounts.Where(ac => ac.IsStagingAccount == true).FirstOrDefault();
                }
            }

            // determine the current current contact
            var currentCA = CoreAppSettings.Current.DefaultCertificateAuthority ?? StandardCertAuthorities.LETS_ENCRYPT;

            if (item != null)
            {
                if (!string.IsNullOrEmpty(item.CertificateAuthorityId))
                {
                    currentCA = item.CertificateAuthorityId;
                }
            }

            // get current account details for this CA (depending on whether this managed certificate uses staging mode or not)
            var matchingAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == currentCA && a.IsStagingAccount == item.UseStagingMode);

            if (matchingAccount == null)
            {
                var log = ManagedCertificateLog.GetLogger(item.Id, new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Error));
                log?.Error($"Failed to match ACME account for managed certificate. Cannot continue request. :: {item.Name} CA: {currentCA} {(item.UseStagingMode ? "[Staging Mode]" : "[Production]")}");
            }

            return matchingAccount;
        }

        /// <summary>
        /// Get decrypted ACME accounts details
        /// </summary>
        /// <param name="credential"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Get decrypted list of all ACME accounts
        /// </summary>
        /// <returns></returns>
        public async Task<List<AccountDetails>> GetAccountRegistrations()
        {
            var list = new List<AccountDetails>();

            var acmeCredentials = await _credentialsManager.GetCredentials(StandardAuthTypes.STANDARD_ACME_ACCOUNT);
            if (acmeCredentials.Any())
            {

                // got stored acme accounts
                foreach (var c in acmeCredentials)
                {
                    try
                    {
                        var acc = await GetAccountDetailsFromCredential(c);
                        if (acc != null)
                        {
                            list.Add(acc);
                        }
                    }
                    catch (Exception exp)
                    {
                        _serviceLog.Error($"Failed to decrypt Account Credentials [{c.Title}] {exp.Message}");
                    }
                }
            }
            else
            {
                // no stored acme accounts, need to migrate or there are no existing accounts
            }

            return list;
        }

        /// <summary>
        /// Perform an ACME account registration and store the new account as a stored credential
        /// </summary>
        /// <param name="reg"></param>
        /// <returns></returns>
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

                var acmeProvider = await GetACMEProvider(storageKey, reg.IsStaging ? certAuthority.StagingAPIEndpoint : certAuthority.ProductionAPIEndpoint, null, certAuthority.AllowUntrustedTls);

                _serviceLog?.Information($"Registering account with ACME CA {acmeProvider.GetAcmeBaseURI()}]: {reg.EmailAddress}");

                var addedAccount = await acmeProvider.AddNewAccountAndAcceptTOS(_serviceLog, reg.EmailAddress, reg.EabKeyId, reg.EabKey, reg.EabKeyAlgorithm);

                if (addedAccount.IsSuccess)
                {
                    // store new account details as credentials
                    addedAccount.Result.ID = storageKey;
                    addedAccount.Result.StorageKey = storageKey;
                    addedAccount.Result.CertificateAuthorityId = certAuthority.Id;
                    addedAccount.Result.IsStagingAccount = reg.IsStaging;

                    addedAccount.Result.EabKeyId = reg.EabKeyId;
                    addedAccount.Result.EabKey = reg.EabKey;
                    addedAccount.Result.EabKeyAlgorithm = reg.EabKeyAlgorithm;
                    addedAccount.Result.PreferredChain = reg.PreferredChain;

                    await StoreAccountAsCredential(addedAccount.Result);
                }

                return addedAccount;
            }
            else
            {
                // did not agree to terms
                return new ActionResult("You must agree to the terms and conditions of the Certificate Authority to register with them.", false);
            }
        }

        /// <summary>
        /// Perform an ACME account registration and store the new account as a stored credential
        /// </summary>
        /// <param name="reg"></param>
        /// <returns></returns>
        public async Task<Models.Config.ActionResult> UpdateAccountContact(string storageKey, ContactRegistration reg)
        {
            // there is one registered contact per account type (per CA, Prod or Staging)

            // attempt to register the new contact
            if (reg.AgreedToTermsAndConditions)
            {
                _certificateAuthorities.TryGetValue(reg.CertificateAuthorityId, out var certAuthority);

                if (certAuthority == null)
                {
                    return new ActionResult("Invalid Certificate Authority specified.", false);
                }
                var existingAccounts = await GetAccountRegistrations();
                var existingAccount = existingAccounts.FirstOrDefault(a => a.StorageKey == storageKey);

                var acmeProvider = await GetACMEProvider(storageKey, reg.IsStaging ? certAuthority.StagingAPIEndpoint : certAuthority.ProductionAPIEndpoint, existingAccount, certAuthority.AllowUntrustedTls);

                _serviceLog?.Information($"Updating account with ACME CA {acmeProvider.GetAcmeBaseURI()}]: {reg.EmailAddress}");

                var updatedAccount = await acmeProvider.UpdateAccount(_serviceLog, reg.EmailAddress, reg.AgreedToTermsAndConditions);

                if (existingAccount != null && updatedAccount.IsSuccess)
                {
                    // store new account details as credentials
                    existingAccount.Email = reg.EmailAddress;
                    existingAccount.AccountKey = updatedAccount.Result.AccountKey;
                    await StoreAccountAsCredential(existingAccount);
                }

                return updatedAccount;
            }
            else
            {
                // did not agree to terms
                return new ActionResult("You must agree to the terms and conditions of the Certificate Authority to register with them.", false);
            }
        }

        /// <summary>
        /// Store an ACME account as a Stored Credential
        /// </summary>
        /// <param name="account"></param>
        /// <returns></returns>
        private async Task StoreAccountAsCredential(AccountDetails account)
        {
            await _credentialsManager.Update(new Models.Config.StoredCredential
            {
                StorageKey = account.ID ?? Guid.NewGuid().ToString(),
                ProviderType = StandardAuthTypes.STANDARD_ACME_ACCOUNT,
                Secret = JsonConvert.SerializeObject(account),
                Title = $"{account.CertificateAuthorityId}_{(account.IsStagingAccount ? "Staging" : "Production")}"
            });
        }

        /// <summary>
        /// Delete an ACME account from stored credentials
        /// </summary>
        /// <param name="storageKey"></param>
        /// <returns></returns>
        public async Task<ActionResult> RemoveAccount(string storageKey)
        {

            var accounts = await GetAccountRegistrations();
            var account = accounts.FirstOrDefault(a => a.StorageKey == storageKey);
            if (account != null)
            {
                _serviceLog?.Information($"Deleting account {storageKey}: " + account.AccountURI);

                var resultOk = await _credentialsManager.Delete(storageKey);
                return new ActionResult("RemoveAccount", resultOk);
            }
            else
            {
                return new ActionResult("Account not found.", false);
            }
        }

        /// <summary>
        /// Upgrade legacy storage of ACME account details and convert to stored credentials
        /// </summary>
        /// <returns></returns>
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
                var settingBaseFolder = EnvironmentUtil.GetAppDataFolder();
                var providerPath = System.IO.Path.Combine(settingBaseFolder, "certes");
                var provider = new CertesACMEProvider(apiEndpoint, settingBaseFolder, providerPath, Util.GetUserAgent());
                await provider.InitProvider(_serviceLog);

                var acc = provider.GetCurrentAcmeAccount();
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
                            var registerResult = await provider.AddNewAccountAndAcceptTOS(_serviceLog, email, null, null, null);
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

        /// <summary>
        /// Refresh cached list of known certificate authorities ands return the current list
        /// </summary>
        /// <returns></returns>
        public async Task<List<CertificateAuthority>> GetCertificateAuthorities()
        {
            LoadCertificateAuthorities();

            return await Task.FromResult(_certificateAuthorities.Values.OrderBy(a => a.Title).ToList());
        }

        /// <summary>
        /// Add/Update details of a custom ACME CA
        /// </summary>
        /// <param name="certificateAuthority"></param>
        /// <returns></returns>
        public async Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority certificateAuthority)
        {

            try
            {
                if (_certificateAuthorities.Any(c => c.Key == certificateAuthority.Id && c.Value.IsCustom == false))
                {
                    // can't modify built in CAs
                    return new ActionResult("Default Certificate Authorities cannot be modified.", false);
                }

                var customCAs = SettingsManager.GetCustomCertificateAuthorities();

                var customCa = customCAs.FirstOrDefault(c => c.Id == certificateAuthority.Id);

                if (customCa != null)
                {
                    // replace
                    customCAs.Remove(customCa);
                    customCAs.Add(certificateAuthority);

                    _certificateAuthorities.TryUpdate(certificateAuthority.Id, certificateAuthority, customCa);
                }
                else
                {
                    // add
                    customCAs.Add(certificateAuthority);

                    _certificateAuthorities.TryAdd(certificateAuthority.Id, certificateAuthority);
                }

                //store updated CAs
                if (SettingsManager.SaveCustomCertificateAuthorities(customCAs))
                {
                    return new ActionResult("OK", true);
                }
            }
            catch (Exception exp)
            {
                // failed to load custom CAs
                _serviceLog.Error(exp.Message);
            }

            return await Task.FromResult(new ActionResult("An error occurred saving the updated Certificate Authorities list.", false));

        }

        /// <summary>
        /// Remove a custom ACME CA
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<ActionResult> RemoveCertificateAuthority(string id)
        {
            var customCAs = SettingsManager.GetCustomCertificateAuthorities();

            var customCa = customCAs.FirstOrDefault(c => c.Id == id);

            if (customCa != null)
            {
                customCAs.Remove(customCa);

                if (SettingsManager.SaveCustomCertificateAuthorities(customCAs))
                {
                    return new ActionResult("OK", true);
                }
            }

            return await Task.FromResult(new ActionResult("An error occurred saving the updated Certificate Authorities list.", false));
        }
    }
}
