using System;
using System.Collections.Generic;
using System.Reflection;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Server.Core.Unit
{
    [TestClass]
    public class InstanceManagementHubTests
    {
        [TestMethod]
        public void GetExternalPushSubscriptionTargets_ReturnsOnlyMatchingPushSubscribers()
        {
            var sourceManagedCertificate = new ManagedCertificate
            {
                Id = "source-cert",
                DateRenewed = DateTimeOffset.UtcNow
            };

            var managedItemsByInstance = new List<KeyValuePair<string, ManagedInstanceItems>>
            {
                new("source-instance", new ManagedInstanceItems
                {
                    Items =
                    [
                        CreateSubscriber("source-cert", "source-instance/source-cert", ExternalCertificateRetrievalModes.Push)
                    ]
                }),
                new("target-instance-1", new ManagedInstanceItems
                {
                    Items =
                    [
                        CreateSubscriber("target-cert-1", "source-instance/source-cert", ExternalCertificateRetrievalModes.Push),
                        CreateSubscriber("target-cert-2", "source-instance/source-cert", ExternalCertificateRetrievalModes.Auto),
                        CreateSubscriber("target-cert-ignored-pull", "source-instance/source-cert", ExternalCertificateRetrievalModes.Pull),
                        CreateSubscriber("target-cert-ignored-other-source", "other-instance/source-cert", ExternalCertificateRetrievalModes.Push)
                    ]
                }),
                new("target-instance-2", new ManagedInstanceItems
                {
                    Items =
                    [
                        CreateSubscriber("target-cert-3", "source-instance/source-cert", ExternalCertificateRetrievalModes.Push, isEnabled: false),
                        CreateSubscriber("target-cert-4", "source-instance/source-cert", ExternalCertificateRetrievalModes.Push, sourceType: ExternalCertificateSourceTypes.SecretsStore),
                        CreateSubscriber("target-cert-5", "invalid-reference", ExternalCertificateRetrievalModes.Push)
                    ]
                })
            };

            var targets = InvokeGetExternalPushSubscriptionTargets("source-instance", sourceManagedCertificate, managedItemsByInstance);

            Assert.AreEqual(2, targets.Count);
            CollectionAssert.AreEquivalent(
                new[] { ("target-instance-1", "target-cert-1"), ("target-instance-1", "target-cert-2") },
                targets);
        }

        [TestMethod]
        public void GetExternalPushSubscriptionTargets_AcceptsColonDelimitedReferences()
        {
            var sourceManagedCertificate = new ManagedCertificate
            {
                Id = "source-cert"
            };

            var managedItemsByInstance = new List<KeyValuePair<string, ManagedInstanceItems>>
            {
                new("target-instance", new ManagedInstanceItems
                {
                    Items =
                    [
                        CreateSubscriber("target-cert", "source-instance:source-cert", ExternalCertificateRetrievalModes.Push)
                    ]
                })
            };

            var targets = InvokeGetExternalPushSubscriptionTargets("source-instance", sourceManagedCertificate, managedItemsByInstance);

            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual(("target-instance", "target-cert"), targets[0]);
        }

        private static ManagedCertificate CreateSubscriber(
            string managedCertificateId,
            string externalReference,
            string retrievalMode,
            bool isEnabled = true,
            string? sourceType = null)
        {
            return new ManagedCertificate
            {
                Id = managedCertificateId,
                ExternalSource = new ExternalCertificateSubscription
                {
                    IsEnabled = isEnabled,
                    SourceType = sourceType ?? ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = retrievalMode,
                    ExternalReference = externalReference
                }
            };
        }

        private static List<(string TargetInstanceId, string TargetManagedCertificateId)> InvokeGetExternalPushSubscriptionTargets(
            string sourceInstanceId,
            ManagedCertificate sourceManagedCertificate,
            IEnumerable<KeyValuePair<string, ManagedInstanceItems>> managedItemsByInstance)
        {
            var method = typeof(InstanceManagementHub).GetMethod(
                "GetExternalPushSubscriptionTargets",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "Expected GetExternalPushSubscriptionTargets to exist.");

            var result = method.Invoke(null, [sourceInstanceId, sourceManagedCertificate, managedItemsByInstance]);

            Assert.IsNotNull(result, "Expected GetExternalPushSubscriptionTargets to return a result.");

            return (List<(string TargetInstanceId, string TargetManagedCertificateId)>)result;
        }
    }
}
