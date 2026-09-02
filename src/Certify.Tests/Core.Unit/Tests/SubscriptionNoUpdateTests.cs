using System;
using System.Collections.ObjectModel;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how a subscription check which finds no update at the source treats the state already recorded
    /// against the item. The check reached the source and found the item already holds its current certificate, so it
    /// resolves a failure to reach or read the source. It deployed nothing, so a failure to deploy the certificate the
    /// item holds must be left exactly as it was, or the deployment retry pass loses both the failure and the count
    /// which paces its retries
    /// </summary>
    [TestClass]
    public class SubscriptionNoUpdateTests
    {
        private static ManagedCertificate CreateSubscription()
        {
            var now = DateTimeOffset.UtcNow;

            return new ManagedCertificate
            {
                Id = "subscriber-item",
                Name = "Subscriber Item",
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                DateStart = now.AddDays(-1),
                DateRenewed = now.AddDays(-1),
                DateExpiry = now.AddDays(6),
                DateLastRenewalAttempt = now.AddHours(-1),
                CertificateThumbprintHash = "ABC123",
                LastRenewalStatus = RequestState.Success,
                RenewalFailureCount = 0,
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "External certificate pulled from Management Hub." },
                LastBindingDeployment = new RequestStageStatus { Status = RequestState.Success, Message = "External certificate deployment completed successfully." },
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Pull,
                    ExternalReference = "instance-a/cert-1",
                    PollIntervalMinutes = 30
                },
                RequestConfig = new CertRequestConfig { PrimaryDomain = "sub.example.com" }
            };
        }

        /// <summary>
        /// The state a failed fetch leaves behind: a failed primary request and no deployment stage
        /// </summary>
        private static ManagedCertificate CreateSubscriptionWithFailedFetch()
        {
            var item = CreateSubscription();

            item.LastRenewalStatus = RequestState.Error;
            item.RenewalFailureCount = 2;
            item.RenewalFailureMessage = "ManagementHub source returned 503";
            item.LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Error, Message = item.RenewalFailureMessage };
            item.LastBindingDeployment = null;

            return item;
        }

        /// <summary>
        /// The state a failed certificate store or binding deployment leaves behind: the certificate was obtained
        /// </summary>
        private static ManagedCertificate CreateSubscriptionWithFailedBindingDeployment()
        {
            var item = CreateSubscription();

            item.LastRenewalStatus = RequestState.Warning;
            item.RenewalFailureCount = 3;
            item.RenewalFailureMessage = "Certificate install failed.";
            item.LastBindingDeployment = new RequestStageStatus { Status = RequestState.Error, Message = item.RenewalFailureMessage };

            return item;
        }

        /// <summary>
        /// The state a failed post-request deployment task leaves behind: the certificate was obtained and bound
        /// </summary>
        private static ManagedCertificate CreateSubscriptionWithFailedDeploymentTask()
        {
            var item = CreateSubscription();

            item.LastRenewalStatus = RequestState.Warning;
            item.RenewalFailureCount = 3;
            item.RenewalFailureMessage = "Endpoint unavailable";
            item.PostRequestTasks = new ObservableCollection<DeploymentTaskConfig>
            {
                new DeploymentTaskConfig { TaskName = "Upload", LastRunStatus = RequestState.Success },
                new DeploymentTaskConfig { TaskName = "Notify", LastRunStatus = RequestState.Error, LastResult = "Endpoint unavailable" }
            };

            return item;
        }

        [TestMethod, Description("A healthy item has no recorded failure of either kind")]
        public void HealthyItemHasNoRecordedFailure()
        {
            var item = CreateSubscription();

            Assert.IsFalse(ManagedCertificate.HasRecordedDeploymentFailure(item));
            Assert.IsFalse(CertifyManager.HasRecordedSourceFailure(item));
        }

        [TestMethod, Description("A failure to reach the source is resolved by a check which reaches it")]
        public void FailedFetchIsASourceFailure()
        {
            var item = CreateSubscriptionWithFailedFetch();

            Assert.IsTrue(CertifyManager.HasRecordedSourceFailure(item), "The source answered this time, so the recorded failure no longer applies");
            Assert.IsFalse(ManagedCertificate.HasRecordedDeploymentFailure(item));
        }

        [TestMethod, Description("A failure to deploy the fetched certificate is not resolved by finding no newer one")]
        public void FailedBindingDeploymentIsADeploymentFailure()
        {
            var item = CreateSubscriptionWithFailedBindingDeployment();

            Assert.IsTrue(ManagedCertificate.HasRecordedDeploymentFailure(item));
            Assert.IsFalse(CertifyManager.HasRecordedSourceFailure(item), "Nothing was deployed by the check, so the failure stands");

            // this is what keeps the item eligible for the deployment retry pass after the check
            Assert.IsTrue(ManagedCertificate.RequiresRedeployment(item));
        }

        [TestMethod, Description("A source failure recorded after a failed binding deployment leaves the deployment failure in place")]
        public void SourceFailureLeavesTheBindingDeploymentFailureInPlace()
        {
            // the deployment failure is what selects the item for redeployment once the source answers again. Were it
            // cleared along with the outcome of the previous request, a source outage would leave the item looking
            // healthy as soon as it resolved, with the certificate the item holds never installed
            var item = CreateSubscriptionWithFailedBindingDeployment();

            CertifyManager.SetSubscriptionSourceFailure(item, item.ExternalSource, "ManagementHub source returned 503");

            Assert.IsTrue(CertifyManager.HasRecordedSourceFailure(item));
            Assert.AreEqual(RequestState.Error, item.LastBindingDeployment?.Status, "The deployment failure is untouched by the source failure");
            Assert.IsFalse(ManagedCertificate.RequiresRedeployment(item), "Until the source answers again there is no certificate to redeploy");

            // what the no-update check records once the source has answered
            item.LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "No updated certificate was available from Management Hub." };

            Assert.IsTrue(ManagedCertificate.RequiresRedeployment(item), "With the source answering, the certificate the item holds is redeployed");
        }

        [TestMethod, Description("A failed deployment task is not resolved by finding no newer certificate")]
        public void FailedDeploymentTaskIsADeploymentFailure()
        {
            var item = CreateSubscriptionWithFailedDeploymentTask();

            Assert.IsTrue(ManagedCertificate.HasRecordedDeploymentFailure(item));
            Assert.IsFalse(CertifyManager.HasRecordedSourceFailure(item));
            Assert.IsTrue(ManagedCertificate.RequiresRedeployment(item));
        }

        [TestMethod, Description("An item which has never been attempted has nothing to resolve")]
        public void NeverAttemptedItemHasNoRecordedFailure()
        {
            var item = CreateSubscription();
            item.LastRenewalStatus = null;
            item.LastPrimaryRequest = null;
            item.LastBindingDeployment = null;

            Assert.IsFalse(CertifyManager.HasRecordedSourceFailure(item));
            Assert.IsFalse(ManagedCertificate.HasRecordedDeploymentFailure(item));
        }

        [TestMethod, Description("A subscription with a failed deployment task keeps its failure count across a no-update check")]
        public void DeploymentFailureCountIsKeptAcrossNoUpdateCheck()
        {
            // the count is what spaces out the deployment retries. It must survive a check which found nothing newer,
            // or a pull subscription whose task keeps failing has its retries restarted from zero on every poll
            var item = CreateSubscriptionWithFailedDeploymentTask();
            var now = DateTimeOffset.UtcNow;

            item.RenewalFailureCount = LifetimeHealthThresholds.FailuresBeforeBackoff;
            item.DateLastRenewalAttempt = now.AddMinutes(-30);

            Assert.IsFalse(CertifyManager.HasRecordedSourceFailure(item), "The check must not record success against this item");
            Assert.IsTrue(ManagedCertificate.IsHeldByFailureBackoff(item, now), "With the count intact the retry stays held by the back off");
        }

        /// <summary>
        /// The state left behind when a deployment task failed on an earlier update and the source then stopped
        /// answering: a source failure recorded against the primary request stage alongside the older task failure
        /// </summary>
        private static ManagedCertificate CreateSubscriptionWithFailedFetchAndOldTaskFailure()
        {
            var item = CreateSubscriptionWithFailedDeploymentTask();

            item.LastRenewalStatus = RequestState.Error;
            item.RenewalFailureCount = 5;
            item.RenewalFailureMessage = "ManagementHub source returned 503";
            item.LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Error, Message = item.RenewalFailureMessage };

            return item;
        }

        [TestMethod, Description("A source failure is recognised even when the item also carries an older task failure")]
        public void SourceFailureAlongsideOldTaskFailureIsStillASourceFailure()
        {
            // judging the item by its overall status and the presence of a deployment failure would read the task
            // failure as the reason and leave the source failure in place, so the item would stay reported as unable to
            // reach its source for as long as the task kept failing
            var item = CreateSubscriptionWithFailedFetchAndOldTaskFailure();

            Assert.IsTrue(CertifyManager.HasRecordedSourceFailure(item), "The source failure is recorded against the primary request stage, whatever else has failed");
            Assert.IsFalse(ManagedCertificate.RequiresRedeployment(item), "Until the source answers again there is no certificate to redeploy");
        }

        [TestMethod, Description("Resolving the source failure leaves the deployment failure in place and hands the item to the deployment retry")]
        public void ResolvingTheSourceLeavesTheDeploymentFailureInPlace()
        {
            var item = CreateSubscriptionWithFailedFetchAndOldTaskFailure();

            // what the no-update check records once the source has answered
            item.LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "No updated certificate was available from Management Hub." };

            var (status, message) = CertifyManager.ResolveRecordedRenewalStatus(item);

            Assert.AreEqual(RequestState.Error, status, "Nothing was deployed, so the failed deployment task still describes the item");
            Assert.AreEqual("Endpoint unavailable", message, "The status now explains the deployment failure rather than the resolved source failure");
            Assert.IsTrue(ManagedCertificate.RequiresRedeployment(item), "With the source answering, the certificate it holds can be redeployed");
        }

        [TestMethod, Description("Resolving the source failure on an item with nothing else wrong makes it healthy")]
        public void ResolvingTheSourceOnAnOtherwiseHealthyItemIsASuccess()
        {
            var item = CreateSubscriptionWithFailedFetch();

            item.LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "No updated certificate was available from Management Hub." };

            var (status, _) = CertifyManager.ResolveRecordedRenewalStatus(item);

            Assert.AreEqual(RequestState.Success, status);
        }
    }
}
