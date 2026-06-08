using System;
using System.Collections.Concurrent;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ExternalSubscriptionPushFanoutTests
    {
        [TestMethod]
        public void GetExternalPushSubscriptionTargets_MatchesSubscribedTarget_ForHubPushMode()
        {
            var sourceInstanceId = "source-instance";
            var sourceManagedCertificate = new ManagedCertificate
            {
                Id = "source-cert",
                DateRenewed = DateTimeOffset.UtcNow
            };

            var subscriber = new ManagedCertificate
            {
                Id = "subscriber-cert",
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = "source-instance/source-cert"
                }
            };

            var nonSubscriber = new ManagedCertificate
            {
                Id = "non-subscriber-cert",
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Pull,
                    ExternalReference = "source-instance/source-cert"
                }
            };

            var managedItemsByInstance = new ConcurrentDictionary<string, ManagedInstanceItems>
            {
                ["target-instance"] = new ManagedInstanceItems
                {
                    InstanceId = "target-instance",
                    Items = [subscriber, nonSubscriber]
                }
            };

            var targets = InstanceManagementHub.GetExternalPushSubscriptionTargets(sourceInstanceId, sourceManagedCertificate, managedItemsByInstance);

            Assert.AreEqual(1, targets.Count, "Expected one push subscriber target.");
            Assert.AreEqual("target-instance", targets[0].TargetInstanceId);
            Assert.AreEqual("subscriber-cert", targets[0].TargetManagedCertificateId);
        }

        [TestMethod]
        public void GetExternalPushSubscriptionTargets_MatchesSubscribedTarget_ForColonSeparatedReference()
        {
            var sourceInstanceId = "source-instance";
            var sourceManagedCertificate = new ManagedCertificate
            {
                Id = "source-cert",
                DateRenewed = DateTimeOffset.UtcNow
            };

            var subscriber = new ManagedCertificate
            {
                Id = "subscriber-cert",
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Push,
                    ExternalReference = "source-instance:source-cert"
                }
            };

            var managedItemsByInstance = new ConcurrentDictionary<string, ManagedInstanceItems>
            {
                ["target-instance"] = new ManagedInstanceItems
                {
                    InstanceId = "target-instance",
                    Items = [subscriber]
                }
            };

            var targets = InstanceManagementHub.GetExternalPushSubscriptionTargets(sourceInstanceId, sourceManagedCertificate, managedItemsByInstance);

            Assert.AreEqual(1, targets.Count, "Expected one push subscriber target for colon-separated hub reference.");
            Assert.AreEqual("target-instance", targets[0].TargetInstanceId);
            Assert.AreEqual("subscriber-cert", targets[0].TargetManagedCertificateId);
        }

        [TestMethod]
        public void GetExternalPushSubscriptionTargets_MatchesSubscribedTarget_ForPushOnlyMode()
        {
            var sourceInstanceId = "source-instance";
            var sourceManagedCertificate = new ManagedCertificate
            {
                Id = "source-cert",
                DateRenewed = DateTimeOffset.UtcNow
            };

            var subscriber = new ManagedCertificate
            {
                Id = "subscriber-cert",
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Push,
                    ExternalReference = "source-instance/source-cert"
                }
            };

            var managedItemsByInstance = new ConcurrentDictionary<string, ManagedInstanceItems>
            {
                ["target-instance"] = new ManagedInstanceItems
                {
                    InstanceId = "target-instance",
                    Items = [subscriber]
                }
            };

            var targets = InstanceManagementHub.GetExternalPushSubscriptionTargets(sourceInstanceId, sourceManagedCertificate, managedItemsByInstance);

            Assert.AreEqual(1, targets.Count, "Expected one push-only subscriber target.");
            Assert.AreEqual("target-instance", targets[0].TargetInstanceId);
            Assert.AreEqual("subscriber-cert", targets[0].TargetManagedCertificateId);
        }

        [TestMethod]
        public void GetExternalPushSubscriptionTargets_DoesNotTargetSourceCertificateItself()
        {
            var sourceInstanceId = "source-instance";
            var sourceManagedCertificate = new ManagedCertificate
            {
                Id = "source-cert"
            };

            var selfItem = new ManagedCertificate
            {
                Id = "source-cert",
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Push,
                    ExternalReference = "source-instance/source-cert"
                }
            };

            var managedItemsByInstance = new ConcurrentDictionary<string, ManagedInstanceItems>
            {
                ["source-instance"] = new ManagedInstanceItems
                {
                    InstanceId = "source-instance",
                    Items = [selfItem]
                }
            };

            var targets = InstanceManagementHub.GetExternalPushSubscriptionTargets(sourceInstanceId, sourceManagedCertificate, managedItemsByInstance);

            Assert.AreEqual(0, targets.Count, "Source item should not be targeted for its own push update.");
        }

        [TestMethod]
        public void CurrentSyncStatus_ReturnsUpdateAvailable_WhenPendingSourceVersionExists()
        {
            var source = new ExternalCertificateSubscription
            {
                PendingSourceVersion = "source-version-1"
            };

            Assert.AreEqual("Update Available", source.CurrentSyncStatus);
        }

        [TestMethod]
        public void TryParseManagementHubReference_ReturnsTrue_ForSlashSeparatedReference()
        {
            var success = ManagedCertificate.TryParseManagementHubReference("source-instance/source-cert", out var instanceId, out var managedCertificateId);

            Assert.IsTrue(success);
            Assert.AreEqual("source-instance", instanceId);
            Assert.AreEqual("source-cert", managedCertificateId);
        }

        [TestMethod]
        public void TryParseManagementHubReference_ReturnsTrue_ForColonSeparatedReference()
        {
            var success = ManagedCertificate.TryParseManagementHubReference("source-instance:source-cert", out var instanceId, out var managedCertificateId);

            Assert.IsTrue(success);
            Assert.AreEqual("source-instance", instanceId);
            Assert.AreEqual("source-cert", managedCertificateId);
        }

        [TestMethod]
        public void HasManagedCertificateVersionChanged_ReturnsTrue_WhenThumbprintChanges()
        {
            var previous = new ManagedCertificate
            {
                Id = "source-cert",
                CertificateThumbprintHash = "thumbprint-1"
            };

            var updated = new ManagedCertificate
            {
                Id = "source-cert",
                CertificateThumbprintHash = "thumbprint-2"
            };

            Assert.IsTrue(InstanceManagementHub.HasManagedCertificateVersionChanged(previous, updated));
        }

        [TestMethod]
        public void HasManagedCertificateVersionChanged_ReturnsFalse_ForNonCertificateMetadataUpdate()
        {
            var previous = new ManagedCertificate
            {
                Id = "source-cert",
                CertificateThumbprintHash = "thumbprint-1",
                Name = "Old name"
            };

            var updated = new ManagedCertificate
            {
                Id = "source-cert",
                CertificateThumbprintHash = "thumbprint-1",
                Name = "New name"
            };

            Assert.IsFalse(InstanceManagementHub.HasManagedCertificateVersionChanged(previous, updated));
        }

        [TestMethod]
        public void ShouldPollSource_ReturnsFalse_WhenSubscriptionRetryIsOnHoldAfterRepeatedFailures()
        {
            CoreAppSettings.Current.RenewalIntervalDays = 30;
            CoreAppSettings.Current.RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var now = DateTimeOffset.UtcNow;
            var item = new ManagedCertificate
            {
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                DateRenewed = now.AddDays(-35),
                DateStart = now.AddDays(-35),
                DateExpiry = now.AddDays(55),
                DateLastRenewalAttempt = now.AddMinutes(-10),
                LastRenewalStatus = RequestState.Error,
                RenewalFailureCount = 5
            };

            var source = new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.ManagementHub,
                RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                PollIntervalMinutes = 5,
                DateLastPoll = now.AddMinutes(-10)
            };

            Assert.IsFalse(CertifyManager.IsAutomaticSubscriptionRetryDue(item, now));
            Assert.IsFalse(CertifyManager.ShouldPollSource(item, source, now));
        }

        [TestMethod]
        public void ShouldPollSource_ReturnsFalse_ForManagementHub_WhenRenewalNotDueAndNoPendingUpdate()
        {
            CoreAppSettings.Current.RenewalIntervalDays = 30;
            CoreAppSettings.Current.RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var now = DateTimeOffset.UtcNow;
            var item = new ManagedCertificate
            {
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                DateRenewed = now.AddDays(-5),
                DateStart = now.AddDays(-5),
                DateExpiry = now.AddDays(85)
            };

            var source = new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.ManagementHub,
                RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                PollIntervalMinutes = 5,
                DateLastPoll = now.AddMinutes(-10)
            };

            Assert.IsFalse(CertifyManager.IsAutomaticSubscriptionRetryDue(item, now));
            Assert.IsFalse(CertifyManager.ShouldPollSource(item, source, now));
        }

        [TestMethod]
        public void ShouldPollSource_ReturnsTrue_ForNonHubSource_WhenRenewalNotDueButPollIntervalElapsed()
        {
            CoreAppSettings.Current.RenewalIntervalDays = 30;
            CoreAppSettings.Current.RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var now = DateTimeOffset.UtcNow;
            var item = new ManagedCertificate
            {
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                DateRenewed = now.AddDays(-5),
                DateStart = now.AddDays(-5),
                DateExpiry = now.AddDays(85)
            };

            var source = new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.SecretsStore,
                RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                PollIntervalMinutes = 5,
                DateLastPoll = now.AddMinutes(-10)
            };

            Assert.IsFalse(CertifyManager.IsAutomaticSubscriptionRetryDue(item, now));
            Assert.IsTrue(CertifyManager.ShouldPollSource(item, source, now));
        }

        [TestMethod]
        public void ShouldPollSource_ReturnsFalse_WhenScheduledRetryIsDueButRenewalBackoffIsStillActive()
        {
            CoreAppSettings.Current.RenewalIntervalDays = 30;
            CoreAppSettings.Current.RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var now = DateTimeOffset.UtcNow;
            var item = new ManagedCertificate
            {
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                DateRenewed = now.AddDays(-1),
                DateStart = now.AddDays(-1),
                DateExpiry = now.AddDays(89),
                DateLastRenewalAttempt = now.AddMinutes(-10),
                DateNextScheduledRenewalAttempt = now.AddMinutes(-1),
                LastRenewalStatus = RequestState.Warning,
                RenewalFailureCount = 5
            };

            var source = new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.ManagementHub,
                RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                PollIntervalMinutes = 5,
                DateLastPoll = now.AddMinutes(-10)
            };

            Assert.IsFalse(CertifyManager.IsAutomaticSubscriptionRetryDue(item, now));
            Assert.IsFalse(CertifyManager.ShouldPollSource(item, source, now));
        }

        [TestMethod]
        public void HasPendingExternalCertificateUpdate_ReturnsTrue_WhenPendingVersionExists()
        {
            var source = new ExternalCertificateSubscription
            {
                PendingSourceVersion = "source-version-1"
            };

            Assert.IsTrue(CertifyManager.HasPendingExternalCertificateUpdate(source));
        }

        [TestMethod]
        public void HasPendingExternalSourceUpdate_ReturnsTrue_WhenPendingVersionExists()
        {
            var source = new ExternalCertificateSubscription
            {
                PendingSourceVersion = "source-version-1"
            };

            Assert.IsTrue(CertifyManager.HasPendingExternalSourceUpdate(source));
        }

        [TestMethod]
        public void ShouldUseDefaultPfxPasswordCredential_ReturnsFalse_ForExternalSubscription()
        {
            var item = new ManagedCertificate
            {
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    ExternalReference = "source-instance/source-cert"
                }
            };

            Assert.IsFalse(CertifyManager.ShouldUseDefaultPfxPasswordCredential(item));
        }

        [TestMethod]
        public void ShouldUseDefaultPfxPasswordCredential_ReturnsTrue_ForStandardManagedCertificate()
        {
            var item = new ManagedCertificate
            {
                ItemType = ManagedCertificateType.SSL_ACME
            };

            Assert.IsTrue(CertifyManager.ShouldUseDefaultPfxPasswordCredential(item));
        }

        [TestMethod]
        public void GetExternalSubscriptionPfxLoadErrorMessage_IncludesPasswordCredentialGuidance()
        {
            var message = CertifyManager.GetExternalSubscriptionPfxLoadErrorMessage();

            StringAssert.Contains(message, "deployable PFX data");
            StringAssert.Contains(message, "different password credential setting");
        }

        [TestMethod]
        public void ShouldProcessExternalManagedCertificate_ReturnsFalse_WhenNotDueAndNoPendingUpdate()
        {
            CoreAppSettings.Current.RenewalIntervalDays = 30;
            CoreAppSettings.Current.RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var now = DateTimeOffset.UtcNow;
            var item = new ManagedCertificate
            {
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                DateRenewed = now.AddDays(-5),
                DateStart = now.AddDays(-5),
                DateExpiry = now.AddDays(85)
            };

            var source = new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.ManagementHub,
                RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                PollIntervalMinutes = 5,
                DateLastPoll = now.AddMinutes(-10)
            };

            Assert.IsFalse(CertifyManager.ShouldProcessExternalManagedCertificate(item, source, now));
        }

        [TestMethod]
        public void ShouldProcessExternalManagedCertificate_ReturnsTrue_WhenPendingSourceUpdateExistsEvenIfNotDue()
        {
            CoreAppSettings.Current.RenewalIntervalDays = 30;
            CoreAppSettings.Current.RenewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var now = DateTimeOffset.UtcNow;
            var item = new ManagedCertificate
            {
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                DateRenewed = now.AddDays(-5),
                DateStart = now.AddDays(-5),
                DateExpiry = now.AddDays(85)
            };

            var source = new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.ManagementHub,
                RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                PendingSourceVersion = "source-version-1",
                DateLastPoll = now
            };

            Assert.IsTrue(CertifyManager.ShouldProcessExternalManagedCertificate(item, source, now));
        }

        [TestMethod]
        public void ClearExternalManagedCertificateRenewalTrigger_ClearsScheduledRenewalAndPendingVersion_WhenNoPendingAssetExists()
        {
            var item = new ManagedCertificate
            {
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow.AddMinutes(-1)
            };

            var source = new ExternalCertificateSubscription
            {
                PendingSourceVersion = "source-version-1"
            };

            CertifyManager.ClearExternalManagedCertificateRenewalTrigger(item, source, clearPendingSourceVersion: true);

            Assert.IsNull(item.DateNextScheduledRenewalAttempt);
            Assert.IsNull(source.PendingSourceVersion);
        }
    }
}
