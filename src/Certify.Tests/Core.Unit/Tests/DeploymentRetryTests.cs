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
    /// Tests for the identification and pacing of items which obtained a certificate but did not fully deploy it.
    /// Renewal scheduling is calculated from the date the certificate was obtained, so these items are not due for
    /// renewal and are only re-attempted because the deployment retry pass picks them up
    /// </summary>
    [TestClass]
    public class DeploymentRetryTests
    {
        /// <summary>
        /// An item which obtained a 90 day certificate an hour ago and deployed it successfully
        /// </summary>
        private static ManagedCertificate CreateDeployedItem()
        {
            var now = DateTimeOffset.UtcNow;

            return new ManagedCertificate
            {
                Id = "test-item",
                Name = "Test Item",
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ACME,
                DateStart = now.AddHours(-1),
                DateRenewed = now.AddHours(-1),
                DateExpiry = now.AddDays(90),
                DateLastRenewalAttempt = now.AddHours(-1),
                CertificateThumbprintHash = "ABC123",
                LastRenewalStatus = RequestState.Success,
                RenewalFailureCount = 0,
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "New certificate received OK." },
                LastBindingDeployment = new RequestStageStatus { Status = RequestState.Success, Message = "Deployed OK." },
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "test.example.com",
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>()
                }
            };
        }

        private static ManagedCertificate CreateItemWithFailedBindingDeployment()
        {
            var item = CreateDeployedItem();

            item.LastBindingDeployment = new RequestStageStatus { Status = RequestState.Error, Message = "Certificate install failed." };
            item.LastRenewalStatus = RequestState.Error;
            item.RenewalFailureCount = 1;

            return item;
        }

        private static ManagedCertificate CreateItemWithFailedDeploymentTask()
        {
            var item = CreateDeployedItem();

            item.PostRequestTasks = new ObservableCollection<DeploymentTaskConfig>
            {
                new DeploymentTaskConfig { TaskName = "Upload", LastRunStatus = RequestState.Success },
                new DeploymentTaskConfig { TaskName = "Notify", LastRunStatus = RequestState.Error, LastResult = "Endpoint unavailable" }
            };
            item.LastRenewalStatus = RequestState.Error;
            item.RenewalFailureCount = 1;

            return item;
        }

        [TestMethod, Description("An item whose certificate deployed successfully does not require a deployment retry")]
        public void FullyDeployedItemDoesNotRequireRetry()
        {
            Assert.IsFalse(CertifyManager.RequiresDeploymentRetry(CreateDeployedItem()));
        }

        [TestMethod, Description("An item whose certificate was obtained but failed to store or bind requires a deployment retry")]
        public void FailedBindingDeploymentRequiresRetry()
        {
            var item = CreateItemWithFailedBindingDeployment();

            // the certificate itself is current, so renewal scheduling will not attempt this item again for most of
            // its lifetime - the deployment retry is the only thing which recovers it
            var renewalDueCheck = ManagedCertificate.CalculateNextRenewalAttempt(item, 75, RenewalIntervalModes.PercentageLifetime);

            Assert.IsFalse(renewalDueCheck.IsRenewalDue, "The item holds a current certificate so renewal is not due");
            Assert.IsTrue(CertifyManager.RequiresDeploymentRetry(item), "Deployment should be re-attempted");
        }

        [TestMethod, Description("An item with a failed post-request deployment task requires a deployment retry")]
        public void FailedDeploymentTaskRequiresRetry()
        {
            var item = CreateItemWithFailedDeploymentTask();

            var renewalDueCheck = ManagedCertificate.CalculateNextRenewalAttempt(item, 75, RenewalIntervalModes.PercentageLifetime);

            Assert.IsFalse(renewalDueCheck.IsRenewalDue, "The item holds a current certificate so renewal is not due");
            Assert.IsTrue(CertifyManager.RequiresDeploymentRetry(item), "Deployment should be re-attempted");
        }

        [TestMethod, Description("An item whose certificate request failed is left to the renewal pass, not the deployment retry")]
        public void FailedPrimaryRequestDoesNotRequireDeploymentRetry()
        {
            var item = CreateItemWithFailedDeploymentTask();
            item.LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Error, Message = "Validation failed." };

            Assert.IsFalse(CertifyManager.RequiresDeploymentRetry(item), "A new certificate is required, redeploying the previous one would not help");
        }

        [TestMethod, Description("An item awaiting user input is not re-attempted by the deployment retry")]
        public void PausedItemDoesNotRequireDeploymentRetry()
        {
            var item = CreateItemWithFailedDeploymentTask();
            item.LastRenewalStatus = RequestState.Paused;

            Assert.AreEqual(ManagedCertificateHealth.AwaitingUser, item.Health);
            Assert.IsFalse(CertifyManager.RequiresDeploymentRetry(item));
        }

        [TestMethod, Description("An item excluded from auto renewal is not re-attempted by the deployment retry")]
        public void ItemNotIncludedInAutoRenewDoesNotRequireDeploymentRetry()
        {
            var item = CreateItemWithFailedDeploymentTask();
            item.IncludeInAutoRenew = false;

            Assert.IsFalse(CertifyManager.RequiresDeploymentRetry(item));
        }

        [TestMethod, Description("An expired certificate is not redeployed, renewal is due for it instead")]
        public void ExpiredCertificateDoesNotRequireDeploymentRetry()
        {
            var item = CreateItemWithFailedDeploymentTask();
            item.DateExpiry = DateTimeOffset.UtcNow.AddDays(-1);

            Assert.IsFalse(CertifyManager.RequiresDeploymentRetry(item));
        }

        [TestMethod, Description("An item with no certificate to deploy does not require a deployment retry")]
        public void ItemWithNoCertificateDoesNotRequireDeploymentRetry()
        {
            var item = CreateItemWithFailedDeploymentTask();
            item.CertificateThumbprintHash = null;
            item.CertificatePath = null;

            Assert.IsFalse(CertifyManager.RequiresDeploymentRetry(item));
        }

        [TestMethod, Description("A deployment retry does not immediately follow the attempt whose deployment just failed")]
        public void DeploymentRetryIsNotDueImmediatelyAfterTheFailedAttempt()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateItemWithFailedDeploymentTask();

            item.DateLastRenewalAttempt = now.AddMinutes(-1);

            Assert.IsFalse(CertifyManager.IsDeploymentRetryDue(item, now), "A retry one minute after the failed attempt is too soon");

            item.DateLastRenewalAttempt = now.AddMinutes(-6);

            Assert.IsTrue(CertifyManager.IsDeploymentRetryDue(item, now), "A retry is due once the minimum interval has passed");
        }

        [TestMethod, Description("Repeated deployment failures are spaced out using the same back off as renewal attempts")]
        public void RepeatedDeploymentFailuresAreBackedOff()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateItemWithFailedDeploymentTask();

            item.RenewalFailureCount = LifetimeHealthThresholds.FailuresBeforeBackoff;
            item.DateLastRenewalAttempt = now.AddMinutes(-30);

            Assert.IsFalse(CertifyManager.IsDeploymentRetryDue(item, now), "Once enough failures have accumulated the retry is held");

            var backoff = ManagedCertificate.CalculateFailureBackoff(item);

            Assert.IsGreaterThan(0, backoff.WaitHrs, "A back off wait should have been calculated");
            Assert.IsTrue(CertifyManager.IsDeploymentRetryDue(item, backoff.NextAttemptByDate), "The retry becomes due once the back off has elapsed");
        }

        [TestMethod, Description("The first few deployment retries are made without a back off so a brief outage recovers quickly")]
        public void EarlyDeploymentRetriesAreNotBackedOff()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateItemWithFailedDeploymentTask();

            item.RenewalFailureCount = LifetimeHealthThresholds.FailuresBeforeBackoff - 1;
            item.DateLastRenewalAttempt = now.AddMinutes(-6);

            Assert.IsTrue(CertifyManager.IsDeploymentRetryDue(item, now));
        }

        [TestMethod, Description("A subscription with an update still pending is left to the subscription pass rather than redeployed")]
        public void SubscriptionWithPendingUpdateDoesNotRequireDeploymentRetry()
        {
            var item = CreateItemWithFailedBindingDeployment();
            item.ItemType = ManagedCertificateType.SSL_ExternalSubscription;
            item.ExternalSource = new ExternalCertificateSubscription
            {
                SourceType = ExternalCertificateSourceTypes.ManagementHub,
                RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                ExternalReference = "instance-a/cert-1",
                PendingSourceVersion = "v2"
            };

            Assert.IsFalse(CertifyManager.RequiresDeploymentRetry(item), "The pending update is fetched and deployed in full by the subscription pass");

            item.ExternalSource.PendingSourceVersion = null;

            Assert.IsTrue(CertifyManager.RequiresDeploymentRetry(item), "With nothing pending the certificate it holds is redeployed");
        }
    }
}
