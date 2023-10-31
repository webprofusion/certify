using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.ACME.Anvil;
using Newtonsoft.Json;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private static object _accountsLock = new object();
        private List<AccountDetails> _accounts;

        /// <summary>
        /// Get the applicable Account Details for this managed item
        /// </summary>
        /// <param name="item">managed certificate to determine account details for</param>
        /// <param name="allowCache">if true, allow use of cached account list</param>
        /// <param name="allowFailover">if true, select a fallback CA account if item has recently failed renewal, if false use same account as last renewal/attempt</param>
        /// <returns>Account Details or null if there is no matching account</returns>
        public async Task<AccountDetails> GetAccountDetails(ManagedCertificate item, bool allowCache = true, bool allowFailover = false, bool isResumedOrder = false)
        {
            if (OverrideAccountDetails != null)
            {
                return OverrideAccountDetails;
            }

            List<AccountDetails> accounts = null;

            if (allowCache)
            {
                if (_accounts?.Any() != true)
                {
                    accounts = await GetAccountRegistrations();

                    lock (_accountsLock)
                    {
                        if (accounts?.Any() == true)
                        {
                            _accounts = accounts?.ToList();
                        }
                    }
                }
                else
                {
                    accounts = _accounts;
                }
            }
            else
            {
                accounts = await GetAccountRegistrations();
            }

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

            var currentCA = GetCurrentCAId(item);
            var reusingLastCA = false;

            if (isResumedOrder && !allowFailover && !string.IsNullOrEmpty(item.LastAttemptedCA) && currentCA != item.LastAttemptedCA)
            {
                // if we have a last attempted CA and we are not looking to failover, use the same CA as last time (e.g. when resuming orders after completing challenges)
                // TODO: if item has previously failed over the CA will stick with the last one rather than the default.
                currentCA = item.LastAttemptedCA;
                reusingLastCA = true;
            }

            // get current account details for this CA (depending on whether this managed certificate uses staging mode or not)
            var defaultMatchingAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == currentCA && a.IsStagingAccount == item.UseStagingMode);

            if (defaultMatchingAccount == null && reusingLastCA)
            {
                // CA used last no longer has an account, determine default
                currentCA = GetCurrentCAId(item);
                defaultMatchingAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == currentCA && a.IsStagingAccount == item.UseStagingMode);
            }

            if (CoreAppSettings.Current.EnableAutomaticCAFailover && allowFailover)
            {
                //If failover enabled check if we want to use this or a fallback account
                defaultMatchingAccount = RenewalManager.SelectCAWithFailover(_certificateAuthorities.Values, accounts, item, defaultMatchingAccount);
            }

            if (defaultMatchingAccount == null)
            {
                var log = ManagedCertificateLog.GetLogger(item.Id, new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Error));
                log?.Error($"Failed to match ACME account for managed certificate. Cannot continue request. :: {item.Name} CA: {currentCA} {(item.UseStagingMode ? "[Staging Mode]" : "[Production]")}");
                return null;
            }
            else
            {
                // We have a matching CA account. 

                return defaultMatchingAccount;
            }
        }

        private string GetCurrentCAId(ManagedCertificate item)
        {
            // determine the current CA which is either the app default, the global CA pref, or specific to this managed cert

            var currentCA = CoreAppSettings.Current.DefaultCertificateAuthority ?? StandardCertAuthorities.LETS_ENCRYPT;

            if (item != null)
            {
                if (!string.IsNullOrEmpty(item.CertificateAuthorityId))
                {
                    currentCA = item.CertificateAuthorityId;
                }
            }

            return currentCA;
        }

        /// <summary>
        /// Get decrypted ACME accounts details
        /// </summary>
        /// <param name="credential"></param>
        /// <returns></returns>
        private async Task<AccountDetails> GetAccountDetailsFromCredential(Models.Config.StoredCredential credential)
        {
            var json = await _credentialsManager.GetUnlockedCredential(credential.StorageKey);

            if (!string.IsNullOrEmpty(json))
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
                        _serviceLog?.Error($"Failed to decrypt Account Credentials [{c.Title}] {exp.Message}");
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

                var addedAccount = await acmeProvider.AddNewAccountAndAcceptTOS(_serviceLog, reg.EmailAddress, reg.EabKeyId, reg.EabKey, reg.EabKeyAlgorithm, reg.ImportedAccountURI, reg.ImportedAccountKey);

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

                // invalidate accounts cache
                lock (_accountsLock)
                {
                    _accounts?.Clear();
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
                var (account, certAuthority, acmeProvider) = await GetAccountAndACMEProvider(storageKey);

                if (account != null)
                {
                    _serviceLog?.Information($"Updating account with ACME CA {certAuthority.Title}]: {reg.EmailAddress}");

                    var updatedAccount = await acmeProvider.UpdateAccount(_serviceLog, reg.EmailAddress, reg.AgreedToTermsAndConditions);

                    if (account != null && updatedAccount.IsSuccess)
                    {
                        // store new account details as credentials
                        account.Email = reg.EmailAddress;
                        account.AccountKey = updatedAccount.Result.AccountKey;
                        account.PreferredChain = reg.PreferredChain;
                        account.AccountFingerprint = updatedAccount.Result.AccountFingerprint;

                        await StoreAccountAsCredential(account);
                    }

                    // invalidate accounts cache
                    lock (_accountsLock)
                    {
                        _accounts?.Clear();
                    }

                    return updatedAccount;
                }
                else
                {
                    return new ActionResult("Account not found.", false);
                }
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
        public async Task<ActionResult> RemoveAccount(string storageKey, bool includeAccountDeactivation = false)
        {
            var (account, certAuthority, acmeProvider) = await GetAccountAndACMEProvider(storageKey);

            if (account != null)
            {
                _serviceLog?.Information($"Deleting account {storageKey}: " + account.AccountURI);

                var resultOk = await _credentialsManager.Delete(_itemManager, storageKey);

                // invalidate accounts cache
                lock (_accountsLock)
                {
                    _accounts?.Clear();
                }

                // attempt acme account deactivation
                if (resultOk && includeAccountDeactivation && acmeProvider != null)
                {
                    try
                    {
                        var resultOK = await acmeProvider.DeactivateAccount(_serviceLog);

                        if (!resultOK)
                        {
                            _serviceLog?.Error($"Error deactivating account with CA {storageKey} {certAuthority.Id} ");
                        }
                    }
                    catch (Exception ex)
                    {
                        _serviceLog?.Error(ex, $"Error deactivating account {storageKey}: " + ex.Message);
                    }
                }

                return new ActionResult("RemoveAccount", resultOk);
            }
            else
            {
                return new ActionResult("Account not found.", false);
            }
        }

        /// <summary>
        /// Return the account details and acme provider for the given account storage key
        /// </summary>
        /// <param name="storageKey"></param>
        /// <returns></returns>
        public async Task<(AccountDetails account, CertificateAuthority certAuthority, IACMEClientProvider acmeProvider)> GetAccountAndACMEProvider(string storageKey)
        {
            var accounts = await GetAccountRegistrations();

            AccountDetails account = accounts.FirstOrDefault(a => a.StorageKey == storageKey);
            IACMEClientProvider acmeProvider = null;
            CertificateAuthority certAuthority = null;

            if (account != null)
            {
                _certificateAuthorities.TryGetValue(account?.CertificateAuthorityId, out certAuthority);

                if (certAuthority != null)
                {
                    acmeProvider = await GetACMEProvider(storageKey, account.IsStagingAccount ? certAuthority.StagingAPIEndpoint : certAuthority.ProductionAPIEndpoint, account, certAuthority.AllowUntrustedTls);
                }
            }

            return (account, certAuthority, acmeProvider);

        }

        public async Task<ActionResult<AccountDetails>> ChangeAccountKey(string storageKey, string newKeyPEM = null)
        {
            var (account, certAuthority, acmeProvider) = await GetAccountAndACMEProvider(storageKey);

            if (account != null && acmeProvider != null)
            {
                // perform account key change
                _serviceLog?.Information($"Changing account key for {storageKey}: " + account.AccountURI);

                try
                {
                    var result = await acmeProvider.ChangeAccountKey(_serviceLog, newKeyPEM);

                    if (!result.IsSuccess)
                    {
                        // failed
                        _serviceLog?.Error(result.Message);
                        return result;
                    }
                    else
                    {
                        // ok, store updated account details
                        account.AccountFingerprint = result.Result.AccountFingerprint;
                        account.AccountKey = result.Result.AccountKey;

                        await StoreAccountAsCredential(account);

                        result.Result = account;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    var msg = $"Failed to performing account key rollover {storageKey}: " + ex.Message;
                    _serviceLog?.Error(ex, msg);
                    return new ActionResult<AccountDetails>(msg, false);
                }
            }
            else
            {
                return new ActionResult<AccountDetails>("Failed to match account to known ACME provider", false);
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
                var settingBaseFolder = EnvironmentUtil.CreateAppDataPath();
                var providerPath = System.IO.Path.Combine(settingBaseFolder, "certes");
                var provider = new AnvilACMEProvider(new AnvilACMEProviderSettings
                {
                    AcmeBaseUri = apiEndpoint,
                    ServiceSettingsBasePath = settingBaseFolder,
                    LegacySettingsPath = providerPath,
                    UserAgentName = Util.GetUserAgent(),
                    DefaultACMERetryIntervalSeconds = CoreAppSettings.Current.DefaultACMERetryInterval,
                    EnableIssuerCache = CoreAppSettings.Current.EnableIssuerCache
                });

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
                            if (registerResult?.IsSuccess ?? false)
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

            // invalidate accounts cache
            lock (_accountsLock)
            {
                _accounts?.Clear();
            }
        }

        /// <summary>
        /// Refresh cached list of known certificate authorities and return the current list
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
                _serviceLog?.Error(exp.Message);
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

            return await Task.FromResult(new ActionResult("An error occurred removing the indicated Custom CA from the Certificate Authorities list.", false));
        }
    }
}
