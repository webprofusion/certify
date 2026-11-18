using System;
using System.Collections.Generic;
using System.Linq;
using Certify.Management;
using Certify.Models;
using Certify.Config;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Rules of migration from other certificate managers which do not depend on the source: when import is refused,
    /// how planned credentials are substituted, how identifiers are matched to DNS zones, and when the source may go
    /// </summary>
    [TestClass]
    public class ExternalMigrationTests
    {
        private static ExternalMigrationPlan Plan(params ExternalMigrationFinding[] findings)
        {
            var plan = new ExternalMigrationPlan { SourceFileHash = "HASH", ProposedItem = new ManagedCertificate { Id = "new" } };
            plan.Findings.AddRange(findings);
            return plan;
        }

        private static ExternalMigrationFinding Finding(ExternalMigrationFindingLevel level, string appliedInput = null) =>
            new() { Key = Guid.NewGuid().ToString(), Level = level, AppliedInput = appliedInput };

        private static ExternalMigrationRequest Request(string hash = "HASH", bool acknowledge = false) =>
            new() { SourceFileHash = hash, AcknowledgeAssumptions = acknowledge };

        [TestMethod]
        public void ImportIsAllowedForAPlanWithOnlyMappedFindings()
        {
            Assert.IsNull(CertifyManager.GetImportRefusal(Plan(Finding(ExternalMigrationFindingLevel.Mapped)), Request()));
        }

        [TestMethod]
        public void ImportIsRefusedWhenTheSourceChangedSincePreview()
        {
            Assert.IsNotNull(CertifyManager.GetImportRefusal(Plan(), Request(hash: "OTHER")));
            Assert.IsNotNull(CertifyManager.GetImportRefusal(Plan(), Request(hash: null)));
        }

        [TestMethod]
        public void ImportIsRefusedWithUnresolvedBlockersOrInputs()
        {
            Assert.IsNotNull(CertifyManager.GetImportRefusal(Plan(Finding(ExternalMigrationFindingLevel.Blocker)), Request()));
            Assert.IsNotNull(CertifyManager.GetImportRefusal(Plan(Finding(ExternalMigrationFindingLevel.InputNeeded)), Request()));

            Assert.IsNull(CertifyManager.GetImportRefusal(Plan(Finding(ExternalMigrationFindingLevel.Blocker, appliedInput: "cred")), Request()),
                "A blocker resolved by an input no longer blocks");
        }

        [TestMethod]
        public void ImportNeedsAssumptionsAcknowledged()
        {
            var plan = Plan(Finding(ExternalMigrationFindingLevel.Assumption));

            Assert.IsNotNull(CertifyManager.GetImportRefusal(plan, Request()));
            Assert.IsNull(CertifyManager.GetImportRefusal(plan, Request(acknowledge: true)));
        }

        [TestMethod]
        public void ImportIsRefusedWhenAlreadyImported()
        {
            var plan = Plan();
            plan.ExistingMigratedItemId = "existing";

            Assert.IsNotNull(CertifyManager.GetImportRefusal(plan, Request()));
        }

        [TestMethod]
        public void DraftCredentialKeysAreReplacedEverywhere()
        {
            var draft = ExternalMigrationCredentialDraft.KeyPrefix + "dns";
            var inline = ExternalMigrationCredentialDraft.KeyPrefix + "zone";
            var key = ExternalMigrationCredentialDraft.KeyPrefix + "key";
            var password = ExternalMigrationCredentialDraft.KeyPrefix + "pfx";
            var vault = ExternalMigrationCredentialDraft.KeyPrefix + "vault";

            var item = new ManagedCertificate
            {
                CertificatePasswordCredentialId = password,
                RequestConfig = new CertRequestConfig
                {
                    CustomPrivateKey = key,
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                    {
                        new() { ChallengeCredentialKey = draft, Parameters = new System.Collections.ObjectModel.ObservableCollection<ProviderParameter> { new() { Key = "zoneid", Value = inline } } }
                    }
                },
                PostRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>
                {
                    new() { ChallengeCredentialKey = vault, Parameters = new List<ProviderParameterSetting> { new("vault_uri", "https://v.vault.azure.net") } }
                }
            };

            CertifyManager.ApplyMigrationCredentialKeys(item, new Dictionary<string, string>
            {
                [draft] = "stored-dns",
                [inline] = "zone-123",
                [key] = "-----BEGIN PRIVATE KEY-----",
                [password] = "stored-pfx",
                [vault] = "stored-vault"
            });

            var challenge = item.RequestConfig.Challenges.Single();
            Assert.AreEqual("stored-dns", challenge.ChallengeCredentialKey);
            Assert.AreEqual("zone-123", challenge.Parameters.Single().Value, "An inline value replaces its draft key in place");
            Assert.AreEqual("-----BEGIN PRIVATE KEY-----", item.RequestConfig.CustomPrivateKey);
            Assert.AreEqual("stored-pfx", item.CertificatePasswordCredentialId);
            Assert.AreEqual("stored-vault", item.PostRequestTasks.Single().ChallengeCredentialKey);
            Assert.AreEqual("https://v.vault.azure.net", item.PostRequestTasks.Single().Parameters.Single().Value, "Ordinary values are left alone");
        }

        [TestMethod]
        public void UndefinedDraftCredentialKeyIsRefused()
        {
            var item = new ManagedCertificate
            {
                RequestConfig = new CertRequestConfig
                {
                    Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>
                    {
                        new() { ChallengeCredentialKey = ExternalMigrationCredentialDraft.KeyPrefix + "missing" }
                    }
                }
            };

            Assert.ThrowsExactly<InvalidOperationException>(() => CertifyManager.ApplyMigrationCredentialKeys(item, new Dictionary<string, string>()));
        }

        [TestMethod]
        public void IdentifiersMatchTheLongestZone()
        {
            var root = new DnsZone { Name = "example.com", ZoneId = "1" };
            var sub = new DnsZone { Name = "dev.example.com.", ZoneId = "2" };
            var other = new DnsZone { Name = "ample.com", ZoneId = "3" };

            var result = CertifyManager.MatchIdentifiersToZones(
                new[] { "example.com", "*.example.com", "www.dev.example.com", "notexample.com", "unknown.org" },
                new[] { root, sub, other });

            CollectionAssert.AreEquivalent(new[] { "example.com", "*.example.com" }, result[root]);
            CollectionAssert.AreEquivalent(new[] { "www.dev.example.com" }, result[sub]);
            Assert.IsFalse(result.ContainsKey(other), "A zone only matches at a label boundary");
            Assert.IsFalse(result.Values.SelectMany(v => v).Contains("unknown.org"));
        }

        [TestMethod]
        public void RenewalAfterCutOverVerifiesOnlyWithANewCertificate()
        {
            var now = DateTimeOffset.UtcNow;
            var info = new ExternalMigrationInfo { State = ExternalMigrationState.CutOver, ImportedCertificateThumbprint = "ABC" };

            Assert.IsFalse(info.RecordRenewalSuccess("abc", now), "The adopted certificate does not verify the migration");
            Assert.IsFalse(info.RecordRenewalSuccess(null, now));
            Assert.AreEqual(ExternalMigrationState.CutOver, info.State);

            Assert.IsTrue(info.RecordRenewalSuccess("DEF", now));
            Assert.AreEqual(ExternalMigrationState.Verified, info.State);
            Assert.AreEqual(now, info.DateVerified);

            var tested = new ExternalMigrationInfo { State = ExternalMigrationState.Tested };
            Assert.IsFalse(tested.RecordRenewalSuccess("DEF", now), "Only a cut over migration is verified by renewal");
        }

        [TestMethod]
        [DataRow(ExternalMigrationState.Imported, false, false)]
        [DataRow(ExternalMigrationState.Tested, true, false)]
        [DataRow(ExternalMigrationState.RolledBack, true, false)]
        [DataRow(ExternalMigrationState.CutOver, false, false)]
        [DataRow(ExternalMigrationState.CutOver, true, true)]
        [DataRow(ExternalMigrationState.Verified, false, true)]
        public void SourceRemovalNeedsVerificationOrConfirmationAfterCutOver(ExternalMigrationState state, bool confirmUnverified, bool expected)
        {
            Assert.AreEqual(expected, new ExternalMigrationInfo { State = state }.CanRemoveSource(confirmUnverified).IsSuccess);
        }
    }
}
