using System.Linq;
using Certify.Core.Management.Challenges;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Shared.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class DnsAccountLabelTests
    {
        // example from draft-ietf-acme-dns-account-label-03 section 3.2
        private const string AccountUrl = "https://example.com/acme/acct/ExampleAccount";
        private const string Label = "_ujmmovf2vn55tgye";

        [TestMethod, Description("The account label matches the draft example")]
        public void TestAccountLabel()
        {
            Assert.AreEqual(Label, DnsAccountLabel.GetAccountLabel(AccountUrl));
        }

        [TestMethod, Description("The record name prefixes the account label and _acme-challenge, and a wildcard uses its base domain")]
        public void TestRecordName()
        {
            Assert.AreEqual($"{Label}._acme-challenge.example.org", DnsAccountLabel.GetRecordName(AccountUrl, "example.org"));
            Assert.AreEqual($"{Label}._acme-challenge.example.org", DnsAccountLabel.GetRecordName(AccountUrl, "*.example.org"));
        }

        [TestMethod, Description("Only an account label under the domain's own _acme-challenge name is recognised")]
        public void TestIsAccountRecordName()
        {
            Assert.IsTrue(DnsAccountLabel.IsAccountRecordName($"{Label}._acme-challenge.example.org", "example.org"));
            Assert.IsTrue(DnsAccountLabel.IsAccountRecordName("_UJMMOVF2VN55TGYE._acme-challenge.example.org", "example.org"));
            Assert.IsFalse(DnsAccountLabel.IsAccountRecordName("_acme-challenge.example.org", "example.org"));
            Assert.IsFalse(DnsAccountLabel.IsAccountRecordName($"{Label}._acme-challenge.other.example.org", "example.org"));
            Assert.IsFalse(DnsAccountLabel.IsAccountRecordName("_ujmmovf2vn55tgy1._acme-challenge.example.org", "example.org"), "1 is not a base32 character");
            Assert.IsFalse(DnsAccountLabel.IsAccountRecordName("www._acme-challenge.example.org", "example.org"));
        }

        [TestMethod, Description("Both dns challenge types use DNS providers, which are defined for dns-01")]
        public void TestUsesDnsProvider()
        {
            Assert.IsTrue(SupportedChallengeTypes.UsesDnsProvider(SupportedChallengeTypes.CHALLENGE_TYPE_DNS));
            Assert.IsTrue(SupportedChallengeTypes.UsesDnsProvider(SupportedChallengeTypes.CHALLENGE_TYPE_DNS_ACCOUNT));
            Assert.IsFalse(SupportedChallengeTypes.UsesDnsProvider(SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST));
            Assert.AreEqual(SupportedChallengeTypes.CHALLENGE_TYPE_DNS, SupportedChallengeTypes.GetProviderChallengeType(SupportedChallengeTypes.CHALLENGE_TYPE_DNS_ACCOUNT));
            Assert.AreEqual(SupportedChallengeTypes.CHALLENGE_TYPE_HTTP, SupportedChallengeTypes.GetProviderChallengeType(SupportedChallengeTypes.CHALLENGE_TYPE_HTTP));
        }

        [TestMethod, Description("A many-to-one CNAME delegation keeps the account label and _acme-challenge labels")]
        public void TestDelegationKeepsAccountLabel()
        {
            var recordName = DnsAccountLabel.GetRecordName(AccountUrl, "www.example.org");

            Assert.AreEqual($"{Label}._acme-challenge.auth.example.net", DnsChallengeHelper.ApplyChallengeDelegationRule("www.example.org", recordName, "*.example.org:auth.example.net"));
            Assert.AreEqual($"{Label}._acme-challenge.www.auth.example.net", DnsChallengeHelper.ApplyChallengeDelegationRule("www.example.org", recordName, "*.example.org:*.auth.example.net"));
            Assert.AreEqual("_acme-challenge.auth.example.net", DnsChallengeHelper.ApplyChallengeDelegationRule("www.example.org", "_acme-challenge.www.example.org", "*.example.org:auth.example.net"));
        }

        [TestMethod, Description("An order stops with a clear message when the CA did not offer the configured challenge type")]
        public void TestUnavailableChallengeTypeMessage()
        {
            var item = new ManagedCertificate
            {
                Id = "item-1",
                Name = "shop.example.com",
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "shop.example.com",
                    SubjectAlternativeNames = ["shop.example.com"],
                    Challenges = [new CertRequestChallengeConfig { ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS_ACCOUNT, ChallengeProvider = "DNS01.Manual" }]
                }
            };

            PendingAuthorization Authorization(params string[] challengeTypes) => new PendingAuthorization
            {
                Identifier = new CertIdentifierItem { IdentifierType = CertIdentifierType.Dns, Value = "shop.example.com", IsAuthorizationPending = true },
                Challenges = challengeTypes.Select(t => new AuthorizationChallengeItem { ChallengeType = t }).ToList()
            };

            var message = Certify.Management.CertifyManager.GetUnavailableChallengeTypeMessage(item, [Authorization(SupportedChallengeTypes.CHALLENGE_TYPE_HTTP, SupportedChallengeTypes.CHALLENGE_TYPE_DNS)]);

            Assert.IsNotNull(message);
            StringAssert.Contains(message, "did not offer a dns-account-01 challenge for shop.example.com");
            StringAssert.Contains(message, "http-01, dns-01");

            Assert.IsNull(Certify.Management.CertifyManager.GetUnavailableChallengeTypeMessage(item, [Authorization(SupportedChallengeTypes.CHALLENGE_TYPE_DNS, SupportedChallengeTypes.CHALLENGE_TYPE_DNS_ACCOUNT)]));

            var alreadyValid = Authorization(SupportedChallengeTypes.CHALLENGE_TYPE_HTTP);
            alreadyValid.IsValidated = true;
            Assert.IsNull(Certify.Management.CertifyManager.GetUnavailableChallengeTypeMessage(item, [alreadyValid]), "an authorization which is already valid needs no challenge");
        }

        [TestMethod, Description("The account label is read from a dns-account-01 record name for any domain, including a delegation target")]
        public void TestAccountLabelFromRecordName()
        {
            Assert.AreEqual(Label, DnsAccountLabel.GetAccountLabelFromRecordName($"{Label}._acme-challenge.example.org"));
            Assert.AreEqual(Label, DnsAccountLabel.GetAccountLabelFromRecordName("_UJMMOVF2VN55TGYE._ACME-challenge.auth.example.net."));
            Assert.IsNull(DnsAccountLabel.GetAccountLabelFromRecordName("_acme-challenge.example.org"));
            Assert.IsNull(DnsAccountLabel.GetAccountLabelFromRecordName($"{Label}._acme-challenge."));
            Assert.IsNull(DnsAccountLabel.GetAccountLabelFromRecordName("_ujmmovf2vn55tgy1._acme-challenge.example.org"));
            Assert.IsNull(DnsAccountLabel.GetAccountLabelFromRecordName(null));
        }

        [TestMethod, Description("A hub managed challenge resolves a dns-account-01 name to the identifier's own name under the caller's account label")]
        public void TestManagedChallengeAccountRecordNames()
        {
            CollectionAssert.AreEqual(new[] { $"{Label}._acme-challenge.example.org" }, ManagedChallengeAccess.GetDnsChallengeRecordNames("*.Example.org", $"{Label}._acme-challenge.example.org.").ToList());

            // a name delegated elsewhere still resolves to the identifier's own name, which must then delegate to it
            CollectionAssert.AreEqual(new[] { $"{Label}._acme-challenge.example.org" }, ManagedChallengeAccess.GetDnsChallengeRecordNames("example.org", $"{Label}._acme-challenge.other.org").ToList());

            CollectionAssert.AreEqual(new[] { "_acme-challenge.example.org", "_acme-challenge-test.example.org" }, ManagedChallengeAccess.GetDnsChallengeRecordNames("example.org", "x._acme-challenge.example.org").ToList());
            Assert.AreEqual(0, ManagedChallengeAccess.GetDnsChallengeRecordNames("not a host", $"{Label}._acme-challenge.example.org").Count);
        }

        [TestMethod, Description("A delegated dns-account-01 name is recognised as the delegation of the identifier's own name")]
        public void TestManagedChallengeAcceptsDelegatedAccountRecordName()
        {
            var delegated = $"{Label}._acme-challenge.auth.example.net";
            var recordName = ManagedChallengeAccess.GetDnsChallengeRecordNames("www.example.org", delegated).Single();

            Assert.IsTrue(ManagedChallengeAccess.IsSameDnsName(DnsChallengeHelper.ApplyChallengeDelegationRule("www.example.org", recordName, "*.example.org:auth.example.net"), delegated));
            Assert.IsFalse(ManagedChallengeAccess.IsSameDnsName(DnsChallengeHelper.ApplyChallengeDelegationRule("www.example.org", recordName, "*.example.org:other.example.net"), delegated));
        }

        [TestMethod, Description("The challenge UIs list the account a request uses for each CA, production and staging, once")]
        public void TestRequestAccountsOnePerCA()
        {
            var accounts = new[]
            {
                new AccountDetails { CertificateAuthorityId = "letsencrypt.org", AccountURI = "https://le/acct/1" },
                new AccountDetails { CertificateAuthorityId = "letsencrypt.org", AccountURI = "https://le/acct/2" },
                new AccountDetails { CertificateAuthorityId = "letsencrypt.org", IsStagingAccount = true, AccountURI = "https://le-staging/acct/3" },
                new AccountDetails { CertificateAuthorityId = "google", AccountURI = "https://google/acct/4" },
                new AccountDetails { CertificateAuthorityId = "removed.example", AccountURI = "https://removed/acct/5" },
                new AccountDetails { CertificateAuthorityId = "google", IsStagingAccount = true, AccountURI = "" }
            };

            var listed = AccountDetails.GetRequestAccounts(accounts, ["letsencrypt.org", "google"]).Select(a => a.AccountURI).ToList();

            CollectionAssert.AreEqual(new[] { "https://le/acct/1", "https://le-staging/acct/3", "https://google/acct/4" }, listed, "the first account per CA and environment, as a request picks it, and none for an unknown CA");
            Assert.AreEqual(4, AccountDetails.GetRequestAccounts(accounts, null).Count, "with no CA list every CA is kept");
        }

        [TestMethod, Description("A many-to-one delegation keeps the account label when the identifier and record name differ in IDN form")]
        public void TestDelegationKeepsAccountLabelForIdn()
        {
            var recordName = $"{Label}._acme-challenge.xn--bcher-kva.example.org";

            Assert.AreEqual($"{Label}._acme-challenge.auth.example.net", DnsChallengeHelper.ApplyChallengeDelegationRule("bücher.example.org", recordName, "*.example.org:auth.example.net"));
            Assert.AreEqual($"{Label}._acme-challenge.auth.example.net", DnsChallengeHelper.ApplyChallengeDelegationRule("xn--bcher-kva.example.org", $"{Label}._acme-challenge.bücher.example.org", "*.example.org:auth.example.net"));
        }
    }
}
