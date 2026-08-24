using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for distinguishing externally managed certificates (discovered via an external certificate manager
    /// provider) from certificate subscriptions (stored items which fetch their certificate from an external source)
    /// </summary>
    [TestClass]
    public class ManagedCertificateItemTypeTests
    {
        private static ManagedCertificate ExternallyManagedItem() => new()
        {
            Id = "ext-certbot-abc123",
            Name = "Discovered Item",
            ItemType = ManagedCertificateType.SSL_ExternallyManaged,
            SourceId = "Certbot"
        };

        private static ManagedCertificate SubscriptionItem() => new()
        {
            Id = "1234-5678",
            Name = "Subscription Item",
            ItemType = ManagedCertificateType.SSL_ExternalSubscription,
            ExternalSource = new ExternalCertificateSubscription { SourceType = ExternalCertificateSourceTypes.ManagementHub }
        };

        private static ManagedCertificate LegacySubscriptionItem() => new()
        {
            Id = "1234-5678",
            Name = "Legacy Subscription Item",
            ItemType = ManagedCertificateType.SSL_ExternallyManaged,
            ExternalSource = new ExternalCertificateSubscription { SourceType = ExternalCertificateSourceTypes.ManagementHub }
        };

        private static ManagedCertificate AcmeItem() => new()
        {
            Id = "1234-5678",
            Name = "Standard Item",
            ItemType = ManagedCertificateType.SSL_ACME
        };

        [TestMethod, Description("An externally managed item is not a certificate subscription")]
        public void ExternallyManagedItemIsNotASubscription()
        {
            var item = ExternallyManagedItem();

            Assert.IsTrue(item.IsExternallyManaged, "An item with the external id prefix is externally managed.");
            Assert.IsFalse(item.IsSubscription, "An externally managed item has no subscription of its own.");
            Assert.IsTrue(item.IsExternalSourceItem, "An externally managed item takes its certificate from an external source.");
        }

        [TestMethod, Description("A certificate subscription is not reported as externally managed")]
        public void SubscriptionItemIsNotExternallyManaged()
        {
            var item = SubscriptionItem();

            Assert.IsTrue(item.IsSubscription, "An item of the subscription type is a certificate subscription.");
            Assert.IsFalse(item.IsExternallyManaged, "A subscription is stored by this instance and is not externally managed.");
            Assert.IsTrue(item.IsExternalSourceItem, "A subscription takes its certificate from an external source.");
        }

        [TestMethod, Description("A subscription stored using the legacy item type is still recognised as a subscription")]
        public void LegacySubscriptionItemIsRecognised()
        {
            var item = LegacySubscriptionItem();

            Assert.IsTrue(item.IsSubscription, "A legacy subscription has the previous item type plus a configured external source.");
            Assert.IsFalse(item.IsExternallyManaged);
        }

        [TestMethod, Description("A standard ACME item is neither externally managed nor a subscription")]
        public void AcmeItemIsNeither()
        {
            var item = AcmeItem();

            Assert.IsFalse(item.IsSubscription);
            Assert.IsFalse(item.IsExternallyManaged);
            Assert.IsFalse(item.IsExternalSourceItem);
        }

        [TestMethod, Description("A subscription stored using the legacy item type adopts the current item type")]
        public void LegacySubscriptionItemTypeIsMigrated()
        {
            var item = LegacySubscriptionItem();

            Assert.IsTrue(item.NormalizeSubscriptionItemType(), "A legacy subscription needs to be stored after migration.");
            Assert.AreEqual(ManagedCertificateType.SSL_ExternalSubscription, item.ItemType);

            Assert.IsFalse(item.NormalizeSubscriptionItemType(), "A migrated subscription does not need storing again.");
        }

        [TestMethod, Description("Migration does not alter items which are not certificate subscriptions")]
        public void MigrationLeavesOtherItemTypesAlone()
        {
            var acmeItem = AcmeItem();
            Assert.IsFalse(acmeItem.NormalizeSubscriptionItemType());
            Assert.AreEqual(ManagedCertificateType.SSL_ACME, acmeItem.ItemType);

            // an externally managed item carries no external source, so is never mistaken for a subscription
            var externallyManaged = ExternallyManagedItem();
            Assert.IsFalse(externallyManaged.NormalizeSubscriptionItemType());
            Assert.AreEqual(ManagedCertificateType.SSL_ExternallyManaged, externallyManaged.ItemType);
        }

        [TestMethod, Description("An external source is only retained for items which take their certificate externally")]
        public void NormalizeClearsExternalSourceForStandardItems()
        {
            var item = AcmeItem();
            item.ExternalSource = new ExternalCertificateSubscription { SourceType = ExternalCertificateSourceTypes.ManagementHub };

            item.NormalizeExternalSourceSettings();

            Assert.IsNull(item.ExternalSource, "A standard item does not keep an external source configuration.");

            var subscription = SubscriptionItem();
            subscription.NormalizeExternalSourceSettings();

            Assert.IsNotNull(subscription.ExternalSource, "A certificate subscription keeps its external source configuration.");
        }

        [TestMethod, Description("Editor defaults are not user configuration, so switching an unconfigured subscription off discards nothing")]
        public void SubscriptionUserConfigurationIsOnlyUserSuppliedSettings()
        {
            var defaults = new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.ManagementHub,
                RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                PollIntervalMinutes = 30
            };

            Assert.IsFalse(defaults.HasUserConfiguration, "Editor defaults alone are not user configuration.");

            Assert.IsTrue(new ExternalCertificateSubscription { ExternalReference = "instance/cert" }.HasUserConfiguration);
            Assert.IsTrue(new ExternalCertificateSubscription { SourceConnection = "https://hub.example.com" }.HasUserConfiguration);
            Assert.IsTrue(new ExternalCertificateSubscription { CredentialKey = "cred-key" }.HasUserConfiguration);
            Assert.IsTrue(new ExternalCertificateSubscription { SourceItemName = "www.example.com" }.HasUserConfiguration);
        }
    }
}
