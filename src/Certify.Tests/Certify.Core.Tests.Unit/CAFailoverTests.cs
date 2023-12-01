using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CAFailoverTests
    {
        private const string DEFAULTCA = "letscertify";
        private readonly CertifyManager _certifyManager;

        // TODO: This requires a valid test CA auth token to run
        //private Dictionary<string, string> ConfigSettings = new Dictionary<string, string>();

        public CAFailoverTests()
        {
            _certifyManager = new CertifyManager();
            CheckForExistingLeAccount().Wait();
            // ConfigSettings = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText("C:\\temp\\Certify\\TestConfigSettings.json"));
        }

        private async Task CheckForExistingLeAccount()
        {
            if ((await _certifyManager.GetAccountRegistrations()).Find(a => a.CertificateAuthorityId == "letsencrypt.org") == null)
            {
                var contactRegistration = new ContactRegistration
                {
                    AgreedToTermsAndConditions = true,
                    CertificateAuthorityId = "letsencrypt.org",
                    EmailAddress = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com",
                    ImportedAccountKey = "",
                    ImportedAccountURI = "",
                    IsStaging = true
                };

                // Add account
                var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
                Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegistration.EmailAddress}");
            }
        }

        private List<CertificateAuthority> GetTestCAs()
        {
            var caList = new List<CertificateAuthority> {
                new CertificateAuthority{
                    Id=DEFAULTCA,
                    Title="Let's Certify",
                    Description="Default CA with various domain features",
                    IsEnabled=true,
                    SupportedFeatures= new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString(),
                    }
                },
                new CertificateAuthority{
                    Id="letsfallback",
                    Title="Let's Fallback",
                    Description="Alternative CA with same features as default",
                    IsEnabled=true,
                    SupportedFeatures= new List<string>{
                        CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                        CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString(),
                    }
                }
                ,
                new CertificateAuthority{
                    Id="letsreluctantlyfallback",
                    Title="Let's Reluctantly Fallback",
                    Description="Alternative CA with unknown features",
                    IsEnabled=true,
                    SupportedFeatures= new List<string>{
                    }
                },
                new CertificateAuthority{
                    Id="letsjumponacall",
                    Title="Let's Jump On A Call",
                    Description="An alterative CA that only does TnAuthList certs",
                    IsEnabled=true,
                    SupportedFeatures= new List<string>{
                        CertAuthoritySupportedRequests.TNAUTHLIST.ToString(),
                    }
                },
                new CertificateAuthority{
                    Id="letspingit",
                    Title="Let's Ping It",
                    Description="An alternative CA that only does IP addresses",
                    IsEnabled=true,
                    SupportedFeatures= new List<string>{
                        CertAuthoritySupportedRequests.IP_SINGLE.ToString(),
                        CertAuthoritySupportedRequests.IP_MULTIPLE.ToString(),
                    }
                }
            };
            return caList;
        }

        private List<AccountDetails> GetTestAccounts()
        {
            var accounts = new List<AccountDetails>
            {
                new AccountDetails{ ID=DEFAULTCA+"ABC123", IsStagingAccount=false, CertificateAuthorityId=DEFAULTCA, Title="The default non-staging account"},
                new AccountDetails{ ID=DEFAULTCA+"ABC123_staging", IsStagingAccount=true, CertificateAuthorityId=DEFAULTCA, Title="A default staging account"},
                new AccountDetails{ ID="letsreluctantlyfallback_ABC234", IsStagingAccount=false, CertificateAuthorityId="letsreluctantlyfallback", Title="A fallback non-staging account with unknown features"},
                new AccountDetails{ ID="letsreluctantlyfallback_ABC234_staging", IsStagingAccount=true, CertificateAuthorityId="letsreluctantlyfallback", Title="A fallback account with unknown features"},
                new AccountDetails{ ID="letsfallback_ABC234", IsStagingAccount=false, CertificateAuthorityId="letsfallback", Title="A fallback non-staging account"},
                new AccountDetails{ ID="letsfallback_ABC234_staging", IsStagingAccount=true, CertificateAuthorityId="letsfallback", Title="A fallback account"},
                new AccountDetails{ ID="letsjumponacall_555", IsStagingAccount=false, CertificateAuthorityId="letsjumponacall", Title="A tnauthlist non-staging account"},
                new AccountDetails{ ID="letsjumponacall_555_staging", IsStagingAccount=true, CertificateAuthorityId="letsjumponacall", Title="A tnauthlist staging account"}
            };

            return accounts;
        }

        private ManagedCertificate GetBasicManagedCertificate(RequestState? lastRenewalState = null, int numberFailedRenewals = 0, string lastCA = null, CertRequestConfig customCertRequestConfig = null)
        {
            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-15),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(60),
                DateLastRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-12),
                LastRenewalStatus = lastRenewalState,
                RenewalFailureCount = numberFailedRenewals,
                LastAttemptedCA = lastCA,
                RequestConfig = customCertRequestConfig == null ? new CertRequestConfig
                {
                    SubjectAlternativeNames = new List<string> { "test.com", "anothertest.com", "www.test.com" }.ToArray()
                } : customCertRequestConfig
            };

            return managedCertificate;
        }

        [TestMethod, Description("Failover to an alternate CA when an item has repeatedly failed")]
        public void TestBasicFailoverOccurs()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 3, lastCA: DEFAULTCA);

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == "letsfallback", "Fallback CA should be selected");
            Assert.IsTrue(selectedAccount.IsFailoverSelection, "Account should be marked as a failover choice");
        }

        [TestMethod, Description("Failover to an alternate CA after previous alternative also failed")]
        public void TestDoubleFailoverOccurs()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 5, lastCA: "letsfallback");

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId != "letsfallback", "Alternative Fallback CA should be selected");
            Assert.IsTrue(selectedAccount.IsFailoverSelection, "Account should be marked as a failover choice");
        }

        [TestMethod, Description("No failover expected")]
        public void TestBasicNoFailover()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(null, 0, null);

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == DEFAULTCA, "Default CA should be selected");
            Assert.IsFalse(selectedAccount.IsFailoverSelection, "Account should not be marked as a failover choice");
        }

        [TestMethod, Description("No failover expected on one account")]
        public void TestNoFailoverOneAccount()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(null, 0, null);

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts.GetRange(3, 1), managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == DEFAULTCA, "Default CA should be selected");
            Assert.IsFalse(selectedAccount.IsFailoverSelection, "Account should not be marked as a failover choice");
        }

        [TestMethod, Description("No fallback accounts available")]
        public void TestBasicNoFallbacks()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 3, lastCA: DEFAULTCA);

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts.FindAll(a => a.IsStagingAccount == false), managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == DEFAULTCA, "Default CA should be selected");
            Assert.IsFalse(selectedAccount.IsFailoverSelection, "Account should not be marked as a failover choice");
        }

        [TestMethod, Description("Next fallback is null")]
        public void TestBasicNextFallbackNull()
        {
            // setup
            var accounts = GetTestAccounts().FindAll(a => a.ID != "letsreluctantlyfallback_ABC234_staging");
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 3, lastCA: "letsfallback");

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            accounts.Add(new AccountDetails
            {
                ID = "letsfallback_ABC234_staging_isfailover",
                IsStagingAccount = true,
                IsFailoverSelection = true,
                CertificateAuthorityId = "letsfallback",
                Title = "A fallback account with is failover"
            });

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == DEFAULTCA, "Default CA should be selected");
            Assert.IsFalse(selectedAccount.IsFailoverSelection, "Account should not be marked as a failover choice");
        }

        [TestMethod, Description("Failover to an alternate CA when an item has repeatedly failed, with wildcard domain")]
        public void TestBasicFailoverOccursWildcardDomainCA()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 3, lastCA: DEFAULTCA,
                new CertRequestConfig { SubjectAlternativeNames = new List<string> { "test.com", "anothertest.com", "www.test.com", "*.wildtest.com" }.ToArray() });

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == "letsfallback", "Fallback CA should be selected");
            Assert.IsTrue(selectedAccount.IsFailoverSelection, "Account should be marked as a failover choice");
        }

        [TestMethod, Description("Failover to an alternate CA when an item has repeatedly failed, with single domain CA")]
        public void TestBasicFailoverOccursSingleDnsCA()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 3, lastCA: DEFAULTCA,
                new CertRequestConfig { SubjectAlternativeNames = new List<string> { "test.com" }.ToArray() });

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == "letsreluctantlyfallback", "Reluctant Fallback CA should be selected");
            Assert.IsTrue(selectedAccount.IsFailoverSelection, "Account should be marked as a failover choice");
        }

        [TestMethod, Description("Failover to an alternate CA when an item has repeatedly failed, with single IP CA")]
        public void TestBasicFailoverOccursSingleIP()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 3, lastCA: DEFAULTCA,
                new CertRequestConfig { SubjectIPAddresses = new List<string> { "192.168.48.64" }.ToArray() });

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == "letsreluctantlyfallback", "Fallback CA should be selected");
            Assert.IsTrue(selectedAccount.IsFailoverSelection, "Account should be marked as a failover choice");
        }

        [TestMethod, Description("Failover to an alternate CA when an item has repeatedly failed, with Multiple IP CA")]
        public void TestBasicFailoverOccursMultipleIP()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 3, lastCA: DEFAULTCA,
                new CertRequestConfig { SubjectIPAddresses = new List<string> { "192.168.48.64", "192.168.48.66" }.ToArray() });

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == "letsreluctantlyfallback", "Fallback CA should be selected");
            Assert.IsTrue(selectedAccount.IsFailoverSelection, "Account should be marked as a failover choice");
        }

        [TestMethod, Description("Failover to an alternate CA when an item has repeatedly failed, with Optional Lifetime Days CA")]
        public void TestBasicFailoverOccursOptionalLifetimeDays()
        {
            // setup
            var accounts = GetTestAccounts();
            var caList = GetTestCAs();

            var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 3, lastCA: DEFAULTCA,
                new CertRequestConfig
                {
                    SubjectAlternativeNames = new List<string> { "test.com", "anothertest.com", "www.test.com", "*.wildtest.com" }.ToArray(),
                    PreferredExpiryDays = 7,
                });

            // perform check
            var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

            var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

            // assert result
            Assert.IsTrue(selectedAccount.CertificateAuthorityId == "letsreluctantlyfallback", "Fallback CA should be selected");
            Assert.IsTrue(selectedAccount.IsFailoverSelection, "Account should be marked as a failover choice");
        }
    }

    // TODO: This test requires a valid test CA auth token to run
    //[TestMethod, Description("Failover to an alternate CA when an item has repeatedly failed, with TnAuthList CA")]
    //public void TestBasicFailoverOccursTnAuthList()
    //{
    //    // setup
    //    var accounts = GetTestAccounts();
    //    var caList = GetTestCAs();

    //    var managedCertificate = GetBasicManagedCertificate(RequestState.Error, 3, lastCA: DEFAULTCA,
    //        new CertRequestConfig {
    //            //SubjectAlternativeNames = new List<string> { "test.com", "anothertest.com", "www.test.com" }.ToArray(),
    //            AuthorityTokens = new ObservableCollection<TkAuthToken> {
    //                new TkAuthToken{
    //                    Token = ConfigSettings["TestAuthToken"],
    //                    Crl =ConfigSettings["TestAuthTokenCRL"]
    //                }
    //            }
    //        });

    //    // perform check
    //    var defaultCAAccount = accounts.FirstOrDefault(a => a.CertificateAuthorityId == DEFAULTCA && a.IsStagingAccount == managedCertificate.UseStagingMode);

    //    var selectedAccount = RenewalManager.SelectCAWithFailover(caList, accounts, managedCertificate, defaultCAAccount);

    //    // assert result
    //    Assert.IsTrue(selectedAccount.CertificateAuthorityId == "letsreluctantlyfallback", "Fallback CA should be selected");
    //    Assert.IsTrue(selectedAccount.IsFailoverSelection, "Account should be marked as a failover choice");
    //}
}
