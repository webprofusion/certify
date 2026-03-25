using System;
using System.Collections.Concurrent;
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
                    IsEnabled = true,
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
                    IsEnabled = true,
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
                    IsEnabled = true,
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
                    IsEnabled = true,
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
        public void CurrentSyncStatus_ReturnsUpdateAvailable_WhenPendingSourceVersionExistsWithoutPendingAsset()
        {
            var source = new ExternalCertificateSubscription
            {
                PendingSourceVersion = "source-version-1"
            };

            Assert.AreEqual("Update Available", source.CurrentSyncStatus);
        }

        [TestMethod]
        public void CurrentSyncStatus_ReturnsPendingDeployment_WhenPendingAssetExists()
        {
            var source = new ExternalCertificateSubscription
            {
                PendingSourceVersion = "source-version-1",
                PendingCertificatePath = "c:\\temp\\external-cert.pfx"
            };

            Assert.AreEqual("Pending Deployment", source.CurrentSyncStatus);
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
    }
}
