using System;
using System.Collections.Generic;
using System.Linq;
using Certify.Models.Shared.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class DnsPersistRecordTests
    {
        // test vector from draft-ietf-acme-dns-persist-02 section 10.2
        private const string Thumbprint = "NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs";
        private const string AccountUrl = "https://ca.example/acct/123";
        private const string HashPrefix = "https://ca.example/account-hash/";
        private const string DomainHash = "https://ca.example/account-hash/sha-256/5SQm7n6tPh2-PlLbCKGnViTXX5z19SCN4cPGQHSk-kw";
        private const string OptOutHash = "https://ca.example/account-hash/sha-256/NpDnSwUthQK8zCgFdefYxAdAVPnygMLbs9US6oO-5ug";

        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

        private static DnsPersistChallengeInfo HashedChallenge() => new DnsPersistChallengeInfo
        {
            Variant = DnsPersistVariant.HashedAccountUri,
            IssuerDomainNames = new List<string> { "authority.example", "ca.example.net" },
            AccountHashPrefix = HashPrefix,
            AccountUrl = AccountUrl,
            AccountThumbprint = Thumbprint
        };

        private static DnsPersistChallengeInfo PlainChallenge() => new DnsPersistChallengeInfo
        {
            Variant = DnsPersistVariant.PlainAccountUrl,
            IssuerDomainNames = new List<string> { "authority.example" },
            AccountUrl = AccountUrl
        };

        [TestMethod, Description("Hashed account URI matches the draft-02 test vector for a domain")]
        public void TestHashedAccountUriForDomain()
        {
            Assert.AreEqual(DomainHash, DnsPersistRecordBuilder.ComputeHashedAccountUri(HashPrefix, AccountUrl, Thumbprint, "example.com"));
        }

        [TestMethod, Description("Hashed account URI matches the draft-02 test vector for the * opt-out")]
        public void TestHashedAccountUriForOptOut()
        {
            Assert.AreEqual(OptOutHash, DnsPersistRecordBuilder.ComputeHashedAccountUri(HashPrefix, AccountUrl, Thumbprint, "*"));
        }

        [TestMethod, Description("Draft-02 record for a wildcard uses the base domain and the hash of the base domain")]
        public void TestBuildDraft02WildcardRecord()
        {
            var record = DnsPersistRecordBuilder.Build(new DnsPersistRecordOptions
            {
                Variant = DnsPersistVariant.HashedAccountUri,
                Domain = "*.Example.com.",
                IssuerDomainName = "Authority.Example",
                AccountUrl = AccountUrl,
                AccountThumbprint = Thumbprint,
                AccountHashPrefix = HashPrefix,
                PolicyWildcard = true,
                PersistUntil = 1767225600
            });

            Assert.AreEqual("_validation-persist.example.com", record.Name);
            Assert.AreEqual($"authority.example; accounturi={DomainHash}; policy=wildcard; persistUntil=1767225600", record.Value);
            Assert.AreEqual(1, record.ValueStrings.Count);
            Assert.AreEqual($"_validation-persist.example.com. IN TXT \"{record.Value}\"", record.ZoneFileEntry);
        }

        [TestMethod, Description("Draft-00 and draft-01 records use the plain account URL")]
        public void TestBuildPlainAccountUriRecords()
        {
            var record = DnsPersistRecordBuilder.Build(new DnsPersistRecordOptions { Variant = DnsPersistVariant.PlainAccountUrl, Domain = "example.com", IssuerDomainName = "authority.example", AccountUrl = AccountUrl });

            Assert.AreEqual($"authority.example; accounturi={AccountUrl}", record.Value);
        }

        [TestMethod, Description("Records never have an empty issuer domain name")]
        public void TestBuildWithoutIssuerUsesPlaceholder()
        {
            var record = DnsPersistRecordBuilder.Build(new DnsPersistRecordOptions { Domain = "example.com", AccountUrl = AccountUrl });

            Assert.IsTrue(record.Value.StartsWith(DnsPersistRecordBuilder.IssuerDomainNamePlaceholder + ";"));
        }

        [TestMethod, Description("Variant detection: a hash prefix means hashed account URIs (draft-02), otherwise the plain account URL")]
        public void TestDetectVariant()
        {
            Assert.AreEqual(DnsPersistVariant.HashedAccountUri, DnsPersistRecordBuilder.DetectVariant(HashPrefix));
            Assert.AreEqual(DnsPersistVariant.PlainAccountUrl, DnsPersistRecordBuilder.DetectVariant(null));
        }

        [TestMethod, Description("Values over 255 octets are split into several character-strings")]
        public void TestSplitLongValue()
        {
            var value = new string('a', 300);
            var parts = DnsPersistRecordBuilder.SplitTxtValue(value);

            CollectionAssert.AreEqual(new[] { 255, 45 }, parts.Select(p => p.Length).ToArray());
            Assert.AreEqual(value, string.Concat(parts));
            Assert.AreEqual($"_validation-persist.example.com. IN TXT (\"{parts[0]}\" \"{parts[1]}\")", DnsPersistRecordBuilder.FormatZoneFileEntry("_validation-persist.example.com", parts));
        }

        [TestMethod, Description("The account fingerprint hex stored with an account converts to the base64url thumbprint")]
        public void TestThumbprintFromFingerprint()
        {
            var bytes = Convert.FromBase64String(Thumbprint.Replace('-', '+').Replace('_', '/') + "=");
            var fingerprint = $"SHA256 {BitConverter.ToString(bytes).Replace("-", ":")}";

            Assert.AreEqual(Thumbprint, DnsPersistRecordBuilder.GetThumbprintFromFingerprint(fingerprint));
            Assert.IsNull(DnsPersistRecordBuilder.GetThumbprintFromFingerprint("SHA256 12:34"));
        }

        [TestMethod, Description("Identifiers sharing a record are grouped, and the group needs policy=wildcard if any is a wildcard")]
        public void TestGroupByRecord()
        {
            var groups = DnsPersistRecordBuilder.GroupByRecord(new[] { "example.com", "*.example.com", "www.example.com" });

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("example.com", groups[0].Domain);
            Assert.IsTrue(groups[0].RequiresWildcard);
            Assert.IsFalse(groups[1].RequiresWildcard);
        }

        [TestMethod, Description("Issuer domain name lists that are empty, too long, or inconsistent with the directory are rejected")]
        public void TestValidateIssuerDomainNames()
        {
            Assert.IsNull(DnsPersistRecordBuilder.ValidateIssuerDomainNames(new[] { "authority.example", "ca.example.net" }, new[] { "authority.example" }, new[] { "authority.example", "ca.example.net" }));
            Assert.IsNotNull(DnsPersistRecordBuilder.ValidateIssuerDomainNames(new string[0], null, null));
            Assert.IsNotNull(DnsPersistRecordBuilder.ValidateIssuerDomainNames(Enumerable.Range(1, 11).Select(i => $"ca{i}.example").ToList(), null, null));
            Assert.IsNotNull(DnsPersistRecordBuilder.ValidateIssuerDomainNames(new[] { "ca.example.net" }, new[] { "authority.example" }, null), "D must be a subset of C");
            Assert.IsNotNull(DnsPersistRecordBuilder.ValidateIssuerDomainNames(new[] { "authority.example", "other.example" }, null, new[] { "authority.example" }), "C must be a subset of I");
        }

        [TestMethod, Description("The issuer already in DNS is preferred, then a directory name, then the first sorted name")]
        public void TestChooseIssuerDomainName()
        {
            var c = new[] { "zeta.example", "authority.example", "ca.example.net" };

            Assert.AreEqual("zeta.example", DnsPersistRecordBuilder.ChooseIssuerDomainName(c, new[] { "ca.example.net" }, new[] { "other.example", "zeta.example" }));
            Assert.AreEqual("ca.example.net", DnsPersistRecordBuilder.ChooseIssuerDomainName(c, new[] { "ca.example.net" }, null));
            Assert.AreEqual("authority.example", DnsPersistRecordBuilder.ChooseIssuerDomainName(c, null, null));
            Assert.IsNull(DnsPersistRecordBuilder.ChooseIssuerDomainName(null, null, null));
        }

        [TestMethod, Description("A draft-02 record using the domain hash or the * opt-out hash is accepted")]
        public void TestCheckDraft02Match()
        {
            var domainRecord = $"authority.example; accounturi={DomainHash}";
            var optOutRecord = $"ca.example.net; accounturi={OptOutHash}; policy=wildcard";

            Assert.IsTrue(DnsPersistRecordChecker.Check(new[] { domainRecord }, HashedChallenge(), "example.com", false, Now).IsMatch);
            Assert.IsTrue(DnsPersistRecordChecker.Check(new[] { "other.example; accounturi=x", optOutRecord }, HashedChallenge(), "*.example.com", true, Now).IsMatch);
        }

        [TestMethod, Description("A plain account URL where the CA needs a hashed one is reported")]
        public void TestCheckDraft02PlainUrlReported()
        {
            var check = DnsPersistRecordChecker.Check(new[] { $"authority.example; accounturi={AccountUrl}" }, HashedChallenge(), "example.com", false, Now);

            Assert.IsFalse(check.IsMatch);
            StringAssert.Contains(check.Problems.Single(), "plain account URL");
            CollectionAssert.AreEqual(new[] { "authority.example" }, check.IssuersInDns);
        }

        [TestMethod, Description("A hashed URI for another account (or an earlier key, which the CA still accepts) is reported")]
        public void TestCheckDraft02OtherAccountReported()
        {
            var otherHash = DnsPersistRecordBuilder.ComputeHashedAccountUri(HashPrefix, "https://ca.example/acct/456", Thumbprint, "example.com");
            var check = DnsPersistRecordChecker.Check(new[] { $"authority.example; accounturi={otherHash}" }, HashedChallenge(), "example.com", false, Now);

            Assert.IsFalse(check.IsMatch);
            StringAssert.Contains(check.Problems.Single(), "different account");
            StringAssert.Contains(check.Problems.Single(), "account key change the CA still accepts it");
        }

        [TestMethod, Description("Plain account URL records are checked for draft-00, and hashed ones reported")]
        public void TestCheckDraft00()
        {
            Assert.IsTrue(DnsPersistRecordChecker.Check(new[] { $"Authority.Example. ; accounturi={AccountUrl}" }, PlainChallenge(), "example.com", false, Now).IsMatch);

            var wrongAccount = DnsPersistRecordChecker.Check(new[] { "authority.example; accounturi=https://ca.example/acct/999" }, PlainChallenge(), "example.com", false, Now);
            StringAssert.Contains(wrongAccount.Problems.Single(), "different ACME account");

            var hashed = DnsPersistRecordChecker.Check(new[] { $"authority.example; accounturi={DomainHash}" }, PlainChallenge(), "example.com", false, Now);
            StringAssert.Contains(hashed.Problems.Single(), "hashed account URI");
        }

        [TestMethod, Description("Missing policy=wildcard, expired persistUntil and malformed records are reported")]
        public void TestCheckProblems()
        {
            var noWildcard = DnsPersistRecordChecker.Check(new[] { $"authority.example; accounturi={AccountUrl}" }, PlainChallenge(), "*.example.com", true, Now);
            StringAssert.Contains(noWildcard.Problems.Single(), "policy=wildcard");

            var expired = DnsPersistRecordChecker.Check(new[] { $"authority.example; accounturi={AccountUrl}; persistUntil=1721952000" }, PlainChallenge(), "example.com", false, Now);
            StringAssert.Contains(expired.Problems.Single(), "expired");

            var duplicate = DnsPersistRecordChecker.Check(new[] { $"authority.example; accounturi={AccountUrl}; accounturi={AccountUrl}" }, PlainChallenge(), "example.com", false, Now);
            StringAssert.Contains(duplicate.Problems.Single(), "more than once");

            var badTimestamp = DnsPersistRecordChecker.Check(new[] { $"authority.example; accounturi={AccountUrl}; persistUntil=soon" }, PlainChallenge(), "example.com", false, Now);
            StringAssert.Contains(badTimestamp.Problems.Single(), "persistUntil");

            var none = DnsPersistRecordChecker.Check(new string[0], PlainChallenge(), "example.com", false, Now);
            StringAssert.Contains(none.Problems.Single(), "No TXT record");
        }

        [TestMethod, Description("Records for other issuers are ignored and reported when nothing else is found")]
        public void TestCheckOtherIssuers()
        {
            var check = DnsPersistRecordChecker.Check(new[] { $"other.example; accounturi={AccountUrl}" }, PlainChallenge(), "example.com", false, Now);

            Assert.IsFalse(check.IsMatch);
            StringAssert.Contains(check.Problems.Single(), "other issuers (other.example)");
        }

        [TestMethod, Description("A persistUntil date coming up is a warning, not a problem")]
        public void TestCheckPersistUntilWarning()
        {
            var soon = Now.AddDays(3).ToUnixTimeSeconds();
            var check = DnsPersistRecordChecker.Check(new[] { $"authority.example; accounturi={AccountUrl}; persistUntil={soon}" }, PlainChallenge(), "example.com", false, Now);

            Assert.IsTrue(check.IsMatch);
            Assert.AreEqual(1, check.Warnings.Count);
        }

        [TestMethod, Description("A persistUntil date picked in a date picker round-trips to the same UTC date")]
        public void TestPersistUntilDateRoundTrip()
        {
            var date = new DateTime(2027, 3, 1, 15, 30, 0, DateTimeKind.Local);
            var timestamp = DnsPersistRecordBuilder.GetPersistUntil(date);

            Assert.AreEqual(new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), timestamp);
            Assert.AreEqual(new DateTime(2027, 3, 1), DnsPersistRecordBuilder.GetPersistUntilDate(timestamp));
        }

        [TestMethod, Description("Challenge issuer domain names are read from both the draft-02 and the earlier field name")]
        public void TestChallengeIssuerDomainNameFields()
        {
            var draft02 = Newtonsoft.Json.JsonConvert.DeserializeObject<ACME.Anvil.Acme.Resource.Challenge>("{\"type\":\"dns-persist-01\",\"issuerDomainNames\":[\"authority.example\"]}");
            var draft01 = Newtonsoft.Json.JsonConvert.DeserializeObject<ACME.Anvil.Acme.Resource.Challenge>("{\"type\":\"dns-persist-01\",\"accounturi\":\"https://ca.example/acct/123\",\"issuer-domain-names\":[\"ca.example.net\"]}");

            CollectionAssert.AreEqual(new[] { "authority.example" }, draft02.IssuerDomainNames.ToArray());
            CollectionAssert.AreEqual(new[] { "ca.example.net" }, draft01.IssuerDomainNames.ToArray());
            Assert.AreEqual("https://ca.example/acct/123", draft01.AccountUri);
        }

        [TestMethod, Description("Directory meta dns-persist-01 values are read")]
        public void TestDirectoryMetaFields()
        {
            var meta = Newtonsoft.Json.JsonConvert.DeserializeObject<ACME.Anvil.Acme.Resource.DirectoryMeta>("{\"caaIdentities\":[\"authority.example\"],\"accountHashPrefix\":\"https://ca.example/account-hash/\",\"issuerDomainNames\":[\"authority.example\",\"ca.example.net\"]}");

            Assert.AreEqual(HashPrefix, meta.AccountHashPrefix);
            CollectionAssert.AreEqual(new[] { "authority.example", "ca.example.net" }, meta.IssuerDomainNames.ToArray());
            CollectionAssert.AreEqual(new[] { "authority.example" }, meta.CaaIdentities.ToArray());
        }

        [TestMethod, Description("Guidance records are grouped per record name with real directory values")]
        public void TestBuildGuidanceRecords()
        {
            var directory = new DnsPersistDirectoryInfo
            {
                AccountUri = AccountUrl,
                AccountThumbprint = Thumbprint,
                AccountHashPrefix = HashPrefix,
                IssuerDomainNames = new List<string> { "authority.example" },
                IsDirectoryAvailable = true
            };

            var records = DnsPersistRecordBuilder.BuildGuidanceRecords(new[] { "example.com", "*.example.com" }, directory, AccountUrl, directory.GetKnownIssuerDomainNames().First(), false, null, false);

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual($"authority.example; accounturi={DomainHash}; policy=wildcard", records[0].Value);

            var optOut = DnsPersistRecordBuilder.BuildGuidanceRecords(new[] { "example.com", "example.org" }, directory, AccountUrl, "authority.example", false, null, true);
            Assert.IsTrue(optOut.All(r => r.AccountUri == OptOutHash));
        }
    }
}
