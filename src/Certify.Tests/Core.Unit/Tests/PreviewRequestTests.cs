using System;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// A preview reports what a request would do without doing it. Nothing about the item is stored and the instance
    /// the caller holds is not touched, so previewing an item never changes what a later real request sees - in
    /// particular a recorded deployment failure, which is what selects the item for redeployment, survives a preview
    /// </summary>
    [TestClass]
    public class PreviewRequestTests
    {
        private static ManagedCertificate CreateItemWithUndeployedCertificate()
        {
            var now = DateTimeOffset.UtcNow;

            return new ManagedCertificate
            {
                Id = "preview-item",
                Name = "Preview Item",
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ACME,
                DateStart = now.AddDays(-1),
                DateRenewed = now.AddDays(-1),
                DateExpiry = now.AddDays(89),
                DateLastRenewalAttempt = now.AddMinutes(-10),
                CertificateThumbprintHash = "ABC123",
                LastRenewalStatus = RequestState.Error,
                RenewalFailureCount = 2,
                RenewalFailureMessage = "Certificate install failed.",
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "New certificate received OK." },
                LastBindingDeployment = new RequestStageStatus { Status = RequestState.Error, Message = "Certificate install failed." },
                RequestConfig = new CertRequestConfig { PrimaryDomain = "test.example.com" }
            };
        }

        private static void AssertItemUntouched(ManagedCertificate item, DateTimeOffset? lastAttempt)
        {
            Assert.AreEqual(lastAttempt, item.DateLastRenewalAttempt, "No attempt was made");
            Assert.AreEqual(2, item.RenewalFailureCount, "The failure count which paces retries is untouched");
            Assert.AreEqual(RequestState.Error, item.LastRenewalStatus);
            Assert.AreEqual("Certificate install failed.", item.RenewalFailureMessage);
            Assert.AreEqual(RequestState.Success, item.LastPrimaryRequest?.Status);
            Assert.AreEqual(RequestState.Error, item.LastBindingDeployment?.Status, "The recorded deployment failure, which selects the item for redeployment, survives the preview");
        }

        [TestMethod, Description("A preview neither stores the item nor changes the instance the caller holds")]
        public async Task PreviewLeavesTheItemUntouched()
        {
            // the manager has no data store, so any attempt to store the item would put it into degraded mode
            var manager = new CertifyManager();
            var item = CreateItemWithUndeployedCertificate();
            var lastAttempt = item.DateLastRenewalAttempt;

            var result = await manager.PerformCertificateRequest(null, item, isPreview: true);

            Assert.IsFalse(manager.IsInDegradedMode, "A preview must not attempt to store the item");
            Assert.AreNotSame(item, result.ManagedItem, "A preview works on its own copy of the item");
            Assert.IsTrue(result.IsSuccess, "The request would be made, which is not a failure whatever the last real request did");
            AssertItemUntouched(item, lastAttempt);
            Assert.IsTrue(ManagedCertificate.RequiresRedeployment(item), "The item is still due to be redeployed by a real request");
        }

        [TestMethod, Description("A preview of a subscription does not contact its source or apply its pending update")]
        public async Task PreviewOfSubscriptionDoesNotRequestFromTheSource()
        {
            var manager = new CertifyManager();
            var item = CreateItemWithUndeployedCertificate();
            var lastAttempt = item.DateLastRenewalAttempt;

            item.ItemType = ManagedCertificateType.SSL_ExternalSubscription;
            item.ExternalSource = new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.ManagementHub,
                RetrievalMode = ExternalCertificateRetrievalModes.Pull,
                ExternalReference = "instance-a/cert-1",
                PendingSourceVersion = "v2"
            };

            var result = await manager.PerformCertificateRequest(null, item, isPreview: true);

            Assert.IsFalse(manager.IsInDegradedMode, "A preview must not attempt to store the item");
            Assert.IsTrue(result.IsSubscriptionUpdateDeferred, "Nothing was fetched or deployed, so no deployment tasks apply");
            Assert.AreEqual("v2", item.ExternalSource.PendingSourceVersion, "The pending update is still waiting for a real request");
            AssertItemUntouched(item, lastAttempt);
        }
    }
}
