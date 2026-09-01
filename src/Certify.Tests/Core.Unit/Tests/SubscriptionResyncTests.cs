using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for which certificate subscriptions ask the hub to re-send their current source version when a connection
    /// is established. A push issued while an instance is disconnected is dropped rather than queued, so a subscription
    /// which relies on push has no other way to find out it missed one
    /// </summary>
    [TestClass]
    public class SubscriptionResyncTests
    {
        private static ManagedCertificate CreateSubscription(string retrievalMode, string sourceType = ExternalCertificateSourceTypes.ManagementHub, string externalReference = "instance-a/cert-1")
        {
            return new ManagedCertificate
            {
                Id = "subscriber-item",
                Name = "Subscriber Item",
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = sourceType,
                    RetrievalMode = retrievalMode,
                    ExternalReference = externalReference,
                    PollIntervalMinutes = 30
                },
                RequestConfig = new CertRequestConfig { PrimaryDomain = "sub.example.com" }
            };
        }

        [TestMethod, Description("A push only subscription asks the hub to re-send its current source version")]
        public void PushOnlySubscriptionRequestsResync()
        {
            var item = CreateSubscription(ExternalCertificateRetrievalModes.Push);

            Assert.IsTrue(CertifyManager.RequiresSubscriptionResync(item, out var sourceInstanceId, out var sourceManagedCertificateId));
            Assert.AreEqual("instance-a", sourceInstanceId);
            Assert.AreEqual("cert-1", sourceManagedCertificateId);
        }

        [TestMethod, Description("An auto mode subscription asks for a resync, because its fallback poll only runs once renewal is due")]
        public void AutoModeSubscriptionRequestsResync()
        {
            var item = CreateSubscription(ExternalCertificateRetrievalModes.Auto);

            Assert.IsTrue(CertifyManager.RequiresSubscriptionResync(item, out _, out _));
        }

        [TestMethod, Description("A subscription with no retrieval mode set uses the default, which can receive pushes")]
        public void DefaultRetrievalModeSubscriptionRequestsResync()
        {
            var item = CreateSubscription(ExternalCertificateRetrievalModes.Auto);
            item.ExternalSource.RetrievalMode = null;

            Assert.IsFalse(CertifyManager.RequiresSubscriptionResync(item, out _, out _),
                "A null retrieval mode falls back to Pull, which checks its source on its own interval");
        }

        [TestMethod, Description("A pull only subscription does not ask for a resync, it checks its source on its own interval")]
        public void PullOnlySubscriptionDoesNotRequestResync()
        {
            var item = CreateSubscription(ExternalCertificateRetrievalModes.Pull);

            Assert.IsFalse(CertifyManager.RequiresSubscriptionResync(item, out _, out _));
        }

        [TestMethod, Description("A subscription from a source the hub does not serve does not ask the hub for a resync")]
        public void NonHubSourceDoesNotRequestResync()
        {
            var item = CreateSubscription(ExternalCertificateRetrievalModes.Push, sourceType: ExternalCertificateSourceTypes.SecretsStore);

            Assert.IsFalse(CertifyManager.RequiresSubscriptionResync(item, out _, out _));
        }

        [TestMethod, Description("A subscription whose source reference cannot be resolved does not ask for a resync")]
        public void UnresolvableSourceReferenceDoesNotRequestResync()
        {
            var item = CreateSubscription(ExternalCertificateRetrievalModes.Push, externalReference: "not-a-valid-reference");

            Assert.IsFalse(CertifyManager.RequiresSubscriptionResync(item, out _, out _));
        }

        [TestMethod, Description("A subscription which has not been configured yet does not ask for a resync")]
        public void UnconfiguredSubscriptionDoesNotRequestResync()
        {
            var item = CreateSubscription(ExternalCertificateRetrievalModes.Push);
            item.ExternalSource.ExternalReference = null;

            Assert.IsFalse(item.IsActionableSubscription);
            Assert.IsFalse(CertifyManager.RequiresSubscriptionResync(item, out _, out _));
        }

        [TestMethod, Description("An item which is not a subscription at all does not ask for a resync")]
        public void NonSubscriptionDoesNotRequestResync()
        {
            var item = new ManagedCertificate
            {
                Id = "normal-item",
                Name = "Normal Item",
                ItemType = ManagedCertificateType.SSL_ACME,
                RequestConfig = new CertRequestConfig { PrimaryDomain = "test.example.com" }
            };

            Assert.IsFalse(CertifyManager.RequiresSubscriptionResync(item, out _, out _));
        }
    }
}
