using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.ACME.Anvil;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CertifyManagerAccountTests
    {
        private readonly CertifyManager _certifyManager;

        public CertifyManagerAccountTests()
        {
            _certifyManager = new CertifyManager();
            _certifyManager.Init().Wait();
            var testCredentialsPath = Path.Combine(EnvironmentUtil.GetAppDataFolder(), "Tests", ".env.test_accounts");
            DotNetEnv.Env.Load(testCredentialsPath);
            SetupAccount().Wait();
        }

        private async Task SetupAccount()
        {
            var currentAccounts = await _certifyManager.GetAccountRegistrations();
            if (currentAccounts.Count == 0)
            {
                var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
                var contactRegistration = new ContactRegistration
                {
                    AgreedToTermsAndConditions = true,
                    CertificateAuthorityId = "letsencrypt.org",
                    EmailAddress = contactRegEmail,
                    ImportedAccountKey = "",
                    ImportedAccountURI = "",
                    IsStaging = true
                };

                // Add account
                var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
                Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            }
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetAccountDetails()")]
        public async Task TestCertifyManagerGetAccountDetails()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when passed in managed certificate is null")]
        public async Task TestCertifyManagerGetAccountDetailsNullItem()
        {
            var caAccount = await _certifyManager.GetAccountDetails(null);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when allowCache is false")]
        public async Task TestCertifyManagerGetAccountDetailsAllowCacheFalse()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert, false);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when CertificateAuthorityId is defined in passed ManagedCertificate")]
        public async Task TestCertifyManagerGetAccountDetailsDefinedCertificateAuthorityId()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true, CertificateAuthorityId = "letsencrypt.org" });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
            Assert.AreEqual("letsencrypt.org", caAccount.CertificateAuthorityId, $"Unexpected certificate authority id '{caAccount.CertificateAuthorityId}'");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when OverrideAccountDetails is defined in CertifyManager")]
        public async Task TestCertifyManagerGetAccountDetailsDefinedOverrideAccountDetails()
        {
            var testUrl = "test.com";
            var account = new AccountDetails
            {
                AccountKey = "",
                AccountURI = "",
                Title = "Dev",
                Email = "test@certifytheweb.com",
                CertificateAuthorityId = "letsencrypt.org",
                StorageKey = "dev",
                IsStagingAccount = true,
            };
            _certifyManager.OverrideAccountDetails = account;

            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
            Assert.AreEqual("test@certifytheweb.com", caAccount.Email);

            _certifyManager.OverrideAccountDetails = null;
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when there is no matching account")]
        public async Task TestCertifyManagerGetAccountDetailsNoMatches()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true, CertificateAuthorityId = "sectigo-ev" });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert);
            Assert.IsNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when there is no matching account")]
        public async Task TestCertifyManagerGetAccountDetailsIsResumeOrder()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true, CertificateAuthorityId = "letsencrypt.org", LastAttemptedCA = "zerossl.com" });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert, true, false, true);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when allowFailover is true")]
        public async Task TestCertifyManagerGetAccountDetailsAllowFailover()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert, true, true);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.AddAccount()")]
        public async Task TestCertifyManagerAddAccount()
        {
            AccountDetails accountDetails = null;
            try
            {
                // Setup account registration info
                var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
                var contactRegistration = new ContactRegistration
                {
                    AgreedToTermsAndConditions = true,
                    CertificateAuthorityId = "letsencrypt.org",
                    EmailAddress = contactRegEmail,
                    ImportedAccountKey = "",
                    ImportedAccountURI = "",
                    IsStaging = true
                };

                // Add account
                var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
                Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
                accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
                Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            }
            finally
            {
                // Cleanup added account
                if (accountDetails != null)
                {
                    await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
                }
            }
        }

        [TestMethod, Description("Happy path test for using CertifyManager.RemoveAccount()")]
        public async Task TestCertifyManagerRemoveAccount()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Remove account
            var removeAccountRes = await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
            Assert.IsTrue(removeAccountRes.IsSuccess, $"Expected account removal to be successful for {contactRegEmail}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when AgreedToTermsAndConditions is false")]
        public async Task TestCertifyManagerAddAccountDidNotAgree()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = false,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "You must agree to the terms and conditions of the Certificate Authority to register with them.", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when CertificateAuthorityId is a bad value")]
        public async Task TestCertifyManagerAddAccountBadCaId()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "bad_ca.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "Invalid Certificate Authority specified.", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when ImportedAccountKey is a blank value")]
        public async Task TestCertifyManagerAddAccountMissingAccountKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "https://acme-staging-v02.api.letsencrypt.org/acme/acct/123403114",
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "To import account details both the existing account URI and account key in PEM format are required. ", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when ImportedAccountURI is a blank value")]
        public async Task TestCertifyManagerAddAccountMissingAccountUri()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = DotNetEnv.Env.GetString("RESTORE_KEY_PEM")?.Replace("\\r", "\r")?.Replace("\\n", "\n")?.Replace("'", ""),
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "To import account details both the existing account URI and account key in PEM format are required. ", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when ImportedAccountKey is a bad value")]
        public async Task TestCertifyManagerAddAccountBadAccountKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "tHiSiSnOtApEm",
                ImportedAccountURI = DotNetEnv.Env.GetString("RESTORE_ACCOUNT_URI"),
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "The provided account key was invalid or not supported for import. A PEM (text) format RSA or ECDA private key is required.", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when ImportedAccountKey and ImportedAccountURI are valid")]
        public async Task TestCertifyManagerAddAccountImport()
        {
            // Setup account registration info
            //var contactRegEmail = "admin.98b9a6@test.com";
            var contactRegEmail = DotNetEnv.Env.GetString("RESTORE_ACCOUNT_EMAIL");
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = DotNetEnv.Env.GetString("RESTORE_KEY_PEM")?.Replace("\\r", "\r")?.Replace("\\n", "\n")?.Replace("'", ""),
                ImportedAccountURI = DotNetEnv.Env.GetString("RESTORE_ACCOUNT_URI"),
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Remove account
            var removeAccountRes = await _certifyManager.RemoveAccount(accountDetails.StorageKey);
            Assert.IsTrue(removeAccountRes.IsSuccess, $"Expected account removal to be successful for {contactRegEmail}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for using CertifyManager.RemoveAccount() with a bad storage key")]
        public async Task TestCertifyManagerRemoveAccountBadKey()
        {
            // Attempt to remove account with bad storage key
            var badStorageKey = "8da1a662-18ed-4787-a0b1-dc36db5a866b";
            var removeAccountRes = await _certifyManager.RemoveAccount(badStorageKey, true);
            Assert.IsFalse(removeAccountRes.IsSuccess, $"Expected account removal to be unsuccessful for storage key {badStorageKey}");
            Assert.AreEqual(removeAccountRes.Message, "Account not found.", "Unexpected error message");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetAccountAndACMEProvider()")]
        public async Task TestCertifyManagerGetAccountAndAcmeProvider()
        {
            AccountDetails accountDetails = null;
            try
            {
                // Setup account registration info
                var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
                var contactRegistration = new ContactRegistration
                {
                    AgreedToTermsAndConditions = true,
                    CertificateAuthorityId = "letsencrypt.org",
                    EmailAddress = contactRegEmail,
                    ImportedAccountKey = "",
                    ImportedAccountURI = "",
                    IsStaging = true
                };

                // Add account
                var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
                Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
                accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
                Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

                var (account, certAuthority, acmeProvider) = await _certifyManager.GetAccountAndACMEProvider(accountDetails.StorageKey);
                Assert.IsNotNull(account, $"Expected account returned by GetAccountAndACMEProvider() to not be null for storage key {accountDetails.StorageKey}");
                Assert.IsNotNull(certAuthority, $"Expected certAuthority returned by GetAccountAndACMEProvider() to not be null for storage key {accountDetails.StorageKey}");
                Assert.IsNotNull(acmeProvider, $"Expected acmeProvider returned by GetAccountAndACMEProvider() to not be null for storage key {accountDetails.StorageKey}");
            }
            finally
            {
                // Cleanup added account
                if (accountDetails != null)
                {
                    await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
                }
            }
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountAndACMEProvider() with a bad storage key")]
        public async Task TestCertifyManagerGetAccountAndAcmeProviderBadKey()
        {
            // Attempt to retrieve account with bad storage key
            var badStorageKey = "8da1a662-18ed-4787-a0b1-dc36db5a866b";
            var (account, certAuthority, acmeProvider) = await _certifyManager.GetAccountAndACMEProvider(badStorageKey);
            Assert.IsNull(account, $"Expected account returned by GetAccountAndACMEProvider() to be null for storage key {badStorageKey}");
            Assert.IsNull(certAuthority, $"Expected certAuthority returned by GetAccountAndACMEProvider() to be null for storage key {badStorageKey}");
            Assert.IsNull(acmeProvider, $"Expected acmeProvider returned by GetAccountAndACMEProvider() to be null for storage key {badStorageKey}");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.UpdateAccountContact()")]
        public async Task TestCertifyManagerUpdateAccountContact()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Update account
            var newContactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var newContactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = newContactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };
            var updateAccountRes = await _certifyManager.UpdateAccountContact(accountDetails.StorageKey, newContactRegistration);
            Assert.IsTrue(updateAccountRes.IsSuccess, $"Expected account creation to be successful for {newContactRegEmail}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == newContactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {newContactRegEmail}");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Test for using CertifyManager.UpdateAccountContact() when AgreedToTermsAndConditions is false")]
        public async Task TestCertifyManagerUpdateAccountContactNoAgreement()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Update account
            var newContactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var newContactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = false,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = newContactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };
            var updateAccountRes = await _certifyManager.UpdateAccountContact(accountDetails.StorageKey, newContactRegistration);
            Assert.IsFalse(updateAccountRes.IsSuccess, $"Expected account creation to not be successful for {newContactRegEmail}");
            Assert.AreEqual(updateAccountRes.Message, "You must agree to the terms and conditions of the Certificate Authority to register with them.", "Unexpected error message");
            var newAccountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == newContactRegEmail);
            Assert.IsNull(newAccountDetails, $"Expected none of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {newContactRegEmail}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Test for using CertifyManager.UpdateAccountContact() when passed storage key doesn't exist")]
        public async Task TestCertifyManagerUpdateAccountContactBadKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Update account
            var newContactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var newContactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = newContactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };
            var badStorageKey = Guid.NewGuid().ToString();
            var updateAccountRes = await _certifyManager.UpdateAccountContact(badStorageKey, newContactRegistration);
            Assert.IsFalse(updateAccountRes.IsSuccess, $"Expected account creation to not be successful for {newContactRegEmail}");
            Assert.AreEqual(updateAccountRes.Message, "Account not found.", "Unexpected error message");
            var newAccountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == newContactRegEmail);
            Assert.IsNull(newAccountDetails, $"Expected none of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {newContactRegEmail}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Happy path test for using CertifyManager.ChangeAccountKey()")]
        public async Task TestCertifyManagerChangeAccountKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            var firstAccountKey = accountDetails.AccountKey;

            // Update account key
            var newKeyPem = KeyFactory.NewKey(KeyAlgorithm.ES256).ToPem();
            var changeAccountKeyRes = await _certifyManager.ChangeAccountKey(accountDetails.StorageKey, newKeyPem);
            Assert.IsTrue(changeAccountKeyRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            Assert.AreEqual(changeAccountKeyRes.Message, "Completed account key rollover", "Unexpected message for CertifyManager.GetAccountRegistrations() success");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            Assert.AreNotEqual(firstAccountKey, accountDetails.AccountKey, $"Expected account key for {contactRegEmail} to have changed after successful CertifyManager.ChangeAccountKey()");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Happy path test for using CertifyManager.ChangeAccountKey() with no passed in new account key")]
        public async Task TestCertifyManagerChangeAccountKeyNull()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            var firstAccountKey = accountDetails.AccountKey;

            // Update account key
            var changeAccountKeyRes = await _certifyManager.ChangeAccountKey(accountDetails.StorageKey);
            Assert.IsTrue(changeAccountKeyRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            Assert.AreEqual(changeAccountKeyRes.Message, "Completed account key rollover", "Unexpected message for CertifyManager.GetAccountRegistrations() success");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            Assert.AreNotEqual(firstAccountKey, accountDetails.AccountKey, $"Expected account key for {contactRegEmail} to have changed after successful CertifyManager.ChangeAccountKey()");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Test for using CertifyManager.ChangeAccountKey() when passed an invalid storage key")]
        public async Task TestCertifyManagerChangeAccountKeyBadStorageKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account key update to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            var firstAccountKey = accountDetails.AccountKey;

            // Attempt to update account key
            var newKeyPem = KeyFactory.NewKey(KeyAlgorithm.ES256).ToPem();
            var badStorageKey = Guid.NewGuid().ToString();
            var changeAccountKeyRes = await _certifyManager.ChangeAccountKey(badStorageKey, newKeyPem);
            Assert.IsFalse(changeAccountKeyRes.IsSuccess, $"Expected account key update to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(changeAccountKeyRes.Message, "Failed to match account to known ACME provider", "Unexpected error message for CertifyManager.GetAccountRegistrations() failure");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            Assert.AreEqual(firstAccountKey, accountDetails.AccountKey, $"Expected account key for {contactRegEmail} not to have changed after unsuccessful CertifyManager.ChangeAccountKey()");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Test for using CertifyManager.ChangeAccountKey() when passed an invalid new account key")]
        public async Task TestCertifyManagerChangeAccountKeyBadAccountKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account key update to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            var firstAccountKey = accountDetails.AccountKey;

            // Attempt to update account key
            var badKeyPem = KeyFactory.NewKey(KeyAlgorithm.ES256).ToPem().Substring(20);
            var changeAccountKeyRes = await _certifyManager.ChangeAccountKey(accountDetails.StorageKey, badKeyPem);
            Assert.IsFalse(changeAccountKeyRes.IsSuccess, $"Expected account key update to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(changeAccountKeyRes.Message, "Failed to use provide key for account rollover", "Unexpected error message for CertifyManager.GetAccountRegistrations() failure");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            Assert.AreEqual(firstAccountKey, accountDetails.AccountKey, $"Expected account key for {contactRegEmail} not to have changed after unsuccessful CertifyManager.ChangeAccountKey()");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Happy path test for using CertifyManager.UpdateCertificateAuthority() to add a new custom CA")]
        public async Task TestCertifyManagerUpdateCertificateAuthorityAdd()
        {
            CertificateAuthority newCustomCa = null;
            try
            {
                newCustomCa = new CertificateAuthority
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Test Custom CA",
                    IsCustom = true,
                    IsEnabled = true,
                    SupportedFeatures = new List<string>
                    {
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    }
                };
                var updateCaRes = await _certifyManager.UpdateCertificateAuthority(newCustomCa);
                Assert.IsTrue(updateCaRes.IsSuccess, $"Expected Custom CA creation for CA with ID {newCustomCa.Id} to be successful");
                Assert.AreEqual(updateCaRes.Message, "OK", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() success");
                var certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
                var newCaDetails = certificateAuthorities.Find(c => c.Id == newCustomCa.Id);
                Assert.IsNotNull(newCaDetails, $"Expected one of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {newCustomCa.Id}");
            }
            finally
            {
                if (newCustomCa != null)
                {
                    await _certifyManager.RemoveCertificateAuthority(newCustomCa.Id);
                }
            }
        }

        [TestMethod, Description("Happy path test for using CertifyManager.UpdateCertificateAuthority() to update an existing custom CA")]
        public async Task TestCertifyManagerUpdateCertificateAuthorityUpdate()
        {
            CertificateAuthority newCustomCa = null;
            try
            {
                newCustomCa = new CertificateAuthority
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Test Custom CA",
                    IsCustom = true,
                    IsEnabled = true,
                    AllowInternalHostnames = false,
                    SupportedFeatures = new List<string>
                    {
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    }
                };

                // Add new CA
                var addCaRes = await _certifyManager.UpdateCertificateAuthority(newCustomCa);
                Assert.IsTrue(addCaRes.IsSuccess, $"Expected Custom CA creation for CA with ID {newCustomCa.Id} to be successful");
                Assert.AreEqual(addCaRes.Message, "OK", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() success");
                var certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
                var newCaDetails = certificateAuthorities.Find(c => c.Id == newCustomCa.Id);
                Assert.IsNotNull(newCaDetails, $"Expected one of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {newCustomCa.Id}");
                Assert.IsFalse(newCaDetails.AllowInternalHostnames);

                var updatedCustomCa = new CertificateAuthority
                {
                    Id = newCustomCa.Id,
                    Title = "Test Custom CA",
                    IsCustom = true,
                    IsEnabled = true,
                    AllowInternalHostnames = true,
                    SupportedFeatures = new List<string>
                    {
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    }
                };

                // Update existing CA
                var updateCaRes = await _certifyManager.UpdateCertificateAuthority(updatedCustomCa);
                Assert.IsTrue(updateCaRes.IsSuccess, $"Expected Custom CA update for CA with ID {updatedCustomCa.Id} to be successful");
                Assert.AreEqual(updateCaRes.Message, "OK", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() success");
                certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
                newCaDetails = certificateAuthorities.Find(c => c.Id == updatedCustomCa.Id);
                Assert.IsNotNull(newCaDetails, $"Expected one of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {updatedCustomCa.Id}");
                Assert.IsTrue(newCaDetails.AllowInternalHostnames);
            }
            finally
            {
                if (newCustomCa != null)
                {
                    await _certifyManager.RemoveCertificateAuthority(newCustomCa.Id);
                }
            }
        }

        [TestMethod, Description("Test for using CertifyManager.UpdateCertificateAuthority() on a default CA")]
        public async Task TestCertifyManagerUpdateCertificateAuthorityDefaultCa()
        {
            var certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
            var defaultCa = certificateAuthorities.First();
            var newCustomCa = new CertificateAuthority
            {
                Id = defaultCa.Id,
                Title = "Test Custom CA",
                IsCustom = true,
                IsEnabled = true,
                AllowInternalHostnames = false,
                SupportedFeatures = new List<string>
                {
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                }
            };

            // Attempt to update default CA
            var updateCaRes = await _certifyManager.UpdateCertificateAuthority(newCustomCa);
            Assert.IsFalse(updateCaRes.IsSuccess, $"Expected CA update for default CA with ID {defaultCa.Id} to be unsuccessful");
            Assert.AreEqual(updateCaRes.Message, "Default Certificate Authorities cannot be modified.", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() failure");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.RemoveCertificateAuthority()")]
        public async Task TestCertifyManagerRemoveCertificateAuthority()
        {
            var newCustomCa = new CertificateAuthority
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Test Custom CA",
                IsCustom = true,
                IsEnabled = true,
                SupportedFeatures = new List<string>
                {
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                }
            };

            // Add custom CA
            var updateCaRes = await _certifyManager.UpdateCertificateAuthority(newCustomCa);
            Assert.IsTrue(updateCaRes.IsSuccess, $"Expected Custom CA creation for CA with ID {newCustomCa.Id} to be successful");
            Assert.AreEqual(updateCaRes.Message, "OK", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() success");
            var certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
            var newCaDetails = certificateAuthorities.Find(c => c.Id == newCustomCa.Id);
            Assert.IsNotNull(newCaDetails, $"Expected one of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {newCustomCa.Id}");

            // Delete custom CA
            var deleteCaRes = await _certifyManager.RemoveCertificateAuthority(newCustomCa.Id);
            Assert.IsTrue(deleteCaRes.IsSuccess, $"Expected Custom CA deletion for CA with ID {newCustomCa.Id} to be successful");
            Assert.AreEqual(deleteCaRes.Message, "OK", "Unexpected result message for CertifyManager.RemoveCertificateAuthority() success");
            certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
            newCaDetails = certificateAuthorities.Find(c => c.Id == newCustomCa.Id);
            Assert.IsNull(newCaDetails, $"Expected none of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {newCustomCa.Id}");
        }

        [TestMethod, Description("Test for using CertifyManager.RemoveCertificateAuthority() when passed a bad custom CA ID")]
        public async Task TestCertifyManagerRemoveCertificateAuthorityBadId()
        {
            var badId = Guid.NewGuid().ToString();

            // Delete custom CA
            var deleteCaRes = await _certifyManager.RemoveCertificateAuthority(badId);
            Assert.IsFalse(deleteCaRes.IsSuccess, $"Expected Custom CA deletion for CA with ID {badId} to be unsuccessful");
            Assert.AreEqual(deleteCaRes.Message, $"The certificate authority {badId} was not found in the list of custom CAs and could not be removed.", "Unexpected result message for CertifyManager.RemoveCertificateAuthority() failure");
        }
    }
}
