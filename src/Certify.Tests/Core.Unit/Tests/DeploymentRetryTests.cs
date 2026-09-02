using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how an item which obtained a certificate but did not fully deploy it is identified and paced.
    /// Renewal scheduling counts from the date the certificate was obtained, so such an item is not due for renewal;
    /// the scheduler instead reports it as due for a request which deploys the certificate it already holds, subject
    /// to the same failure hold and maintenance window as any other attempt
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

        /// <summary>
        /// A fully deployed item whose on-demand (manual-trigger) task failed the last time a person ran it
        /// </summary>
        private static ManagedCertificate CreateItemWithFailedManualTask()
        {
            var item = CreateDeployedItem();

            item.PostRequestTasks = new ObservableCollection<DeploymentTaskConfig>
            {
                new DeploymentTaskConfig { TaskName = "Upload", TaskTrigger = TaskTriggerType.ON_SUCCESS, LastRunStatus = RequestState.Success },
                new DeploymentTaskConfig { TaskName = "Export on demand", TaskTrigger = TaskTriggerType.MANUAL, LastRunStatus = RequestState.Error, LastResult = "Export path not found" }
            };

            return item;
        }

        /// <summary>
        /// The renewal plan the scheduler produces for the item, with a 75% of lifetime renewal target
        /// </summary>
        private static RenewalDueInfo GetPlan(ManagedCertificate item, DateTimeOffset? checkDate = null)
        {
            return ManagedCertificate.CalculateNextRenewalAttempt(item, 75, RenewalIntervalModes.PercentageLifetime, checkDate);
        }

        private static RequestState InvokeResolveOverallRenewalStatus(ManagedCertificate managedCertificate, CertificateRequestResult requestResult)
        {
            var method = typeof(CertifyManager).GetMethod("ResolveOverallRenewalStatus", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);

            return (RequestState)method.Invoke(null, new object[] { managedCertificate, requestResult, true });
        }

        [TestMethod, Description("An item whose certificate deployed successfully is not due for redeployment")]
        public void FullyDeployedItemIsNotRedeployed()
        {
            var item = CreateDeployedItem();

            Assert.IsFalse(ManagedCertificate.RequiresRedeployment(item));

            var plan = GetPlan(item);

            Assert.IsFalse(plan.IsRenewalDue);
            Assert.IsFalse(plan.IsRedeployOnly);
        }

        [TestMethod, Description("An item whose certificate was obtained but failed to store or bind is due for redeployment")]
        public void FailedBindingDeploymentIsDueForRedeployment()
        {
            var item = CreateItemWithFailedBindingDeployment();

            Assert.IsTrue(ManagedCertificate.RequiresRedeployment(item));

            // the certificate itself is current, so renewal is nowhere near due - the scheduler reports the redeploy
            // instead, which is what the renewal pass acts on and what the renewal plan shows in the UI
            var plan = GetPlan(item);

            Assert.IsTrue(plan.IsRenewalDue, "The item is due for an attempt");
            Assert.IsTrue(plan.IsRedeployOnly, "The attempt deploys the certificate already held rather than requesting a new one");
            Assert.IsFalse(plan.IsRenewalOnHold);
            StringAssert.Contains(plan.Reason, "Redeployment", "The plan explains what will happen");
        }

        [TestMethod, Description("An item with a failed automated deployment task is due for redeployment")]
        public void FailedDeploymentTaskIsDueForRedeployment()
        {
            var item = CreateItemWithFailedDeploymentTask();

            Assert.IsTrue(ManagedCertificate.RequiresRedeployment(item));

            var plan = GetPlan(item);

            Assert.IsTrue(plan.IsRenewalDue);
            Assert.IsTrue(plan.IsRedeployOnly);
        }

        [TestMethod, Description("An item whose certificate request failed is left to renewal, not redeployment")]
        public void FailedPrimaryRequestIsNotRedeployed()
        {
            var item = CreateItemWithFailedDeploymentTask();
            item.LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Error, Message = "Validation failed." };

            Assert.IsFalse(ManagedCertificate.RequiresRedeployment(item), "A new certificate is required, redeploying the previous one would not help");
            Assert.IsFalse(GetPlan(item).IsRedeployOnly);
        }

        [TestMethod, Description("An item awaiting user input is not redeployed")]
        public void PausedItemIsNotRedeployed()
        {
            var item = CreateItemWithFailedDeploymentTask();
            item.LastRenewalStatus = RequestState.Paused;

            Assert.AreEqual(ManagedCertificateHealth.AwaitingUser, item.Health);
            Assert.IsFalse(ManagedCertificate.RequiresRedeployment(item));
        }

        [TestMethod, Description("An expired certificate is not redeployed, renewal is due for it instead")]
        public void ExpiredCertificateIsNotRedeployed()
        {
            var item = CreateItemWithFailedDeploymentTask();
            item.DateExpiry = DateTimeOffset.UtcNow.AddDays(-1);

            Assert.IsFalse(ManagedCertificate.RequiresRedeployment(item));

            var plan = GetPlan(item);

            Assert.IsTrue(plan.IsRenewalDue, "An expired certificate is due for renewal");
            Assert.IsFalse(plan.IsRedeployOnly);
        }

        [TestMethod, Description("An item with no certificate to deploy is not redeployed")]
        public void ItemWithNoCertificateIsNotRedeployed()
        {
            var item = CreateItemWithFailedDeploymentTask();
            item.CertificateThumbprintHash = null;
            item.CertificatePath = null;

            Assert.IsFalse(ManagedCertificate.RequiresRedeployment(item));
        }

        [TestMethod, Description("Renewal takes precedence over redeployment for an item which is due for both")]
        public void RenewalDueTakesPrecedenceOverRedeployment()
        {
            // a 90 day certificate at 89% of its lifetime whose deployment failed: a new certificate is needed anyway,
            // and deploying it is part of renewal
            var now = DateTimeOffset.UtcNow;
            var item = CreateItemWithFailedBindingDeployment();

            item.DateStart = now.AddDays(-80);
            item.DateRenewed = now.AddDays(-80);
            item.DateExpiry = now.AddDays(10);

            var plan = GetPlan(item, now);

            Assert.IsTrue(plan.IsRenewalDue);
            Assert.IsFalse(plan.IsRedeployOnly, "A certificate which is due is replaced, not redeployed");
        }

        [TestMethod, Description("A redeployment is due on the next pass while the item is within its first attempts")]
        public void RedeploymentIsDueOnTheNextPassWhileWithinTheFirstAttempts()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateItemWithFailedDeploymentTask();

            item.DateLastRenewalAttempt = now.AddMinutes(-1);

            Assert.IsFalse(ManagedCertificate.IsHeldByFailureBackoff(item, now), "The first few attempts are made without delay, so a brief problem recovers quickly");

            var plan = GetPlan(item, now);

            Assert.IsTrue(plan.IsRedeployOnly);
            Assert.IsFalse(plan.IsRenewalOnHold);
        }

        [TestMethod, Description("Repeated deployment failures are spaced out by the same back off as renewal attempts")]
        public void RepeatedDeploymentFailuresAreBackedOff()
        {
            var now = DateTimeOffset.UtcNow;
            var item = CreateItemWithFailedDeploymentTask();

            item.RenewalFailureCount = LifetimeHealthThresholds.FailuresBeforeBackoff;
            item.DateLastRenewalAttempt = now.AddMinutes(-30);

            Assert.IsTrue(ManagedCertificate.IsHeldByFailureBackoff(item, now), "Once enough failures have accumulated the next attempt is held");

            var plan = GetPlan(item, now);

            Assert.IsTrue(plan.IsRenewalOnHold, "The plan reports the hold");
            Assert.IsTrue(plan.IsRedeployOnly, "The plan still describes the attempt as a redeployment");
            StringAssert.Contains(plan.Reason, "on hold");

            var backoff = ManagedCertificate.CalculateFailureBackoff(item);

            Assert.IsGreaterThan(0, backoff.WaitHrs, "A back off wait should have been calculated");
            Assert.IsFalse(ManagedCertificate.IsHeldByFailureBackoff(item, backoff.NextAttemptByDate), "The attempt becomes due once the back off has elapsed");
            Assert.IsFalse(GetPlan(item, backoff.NextAttemptByDate).IsRenewalOnHold);
        }

        [TestMethod, Description("A redeployment is deferred to the item's maintenance window like any other attempt")]
        public void RedeploymentIsDeferredByTheMaintenanceWindow()
        {
            // a window which only opens on a day other than today, so the check is always outside it
            var otherDay = DateTimeOffset.Now.DayOfWeek == DayOfWeek.Sunday ? MaintenanceDays.Wednesday : MaintenanceDays.Sunday;

            var prefs = new RenewalPrefs
            {
                RenewalIntervalDays = 75,
                RenewalIntervalMode = RenewalIntervalModes.PercentageLifetime,
                DefaultMaintenanceWindowId = "window-1",
                MaintenanceWindows = new List<MaintenanceWindow>
                {
                    new MaintenanceWindow
                    {
                        Id = "window-1",
                        Name = "Overnight",
                        IsEnabled = true,
                        Days = otherDay,
                        StartTime = TimeSpan.FromHours(2),
                        EndTime = TimeSpan.FromHours(4)
                    }
                }
            };

            var plan = RenewalScheduleCalculator.CalculateNextRenewalAttempt(CreateItemWithFailedBindingDeployment(), prefs);

            Assert.IsTrue(plan.IsRedeployOnly);
            Assert.IsTrue(plan.IsDeferredByMaintenanceWindow, "A deployment outside the maintenance window is what the window exists to prevent");
            StringAssert.Contains(plan.Reason, "Limited to Maintenance Window", "The plan reports the window the redeployment is waiting for");
        }

        [TestMethod, Description("A subscription with an update still pending is left to the subscription pass rather than redeployed")]
        public void SubscriptionWithPendingUpdateIsNotRedeployed()
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

            Assert.IsFalse(ManagedCertificate.RequiresRedeployment(item), "The pending update is fetched and deployed in full by the subscription pass");

            item.ExternalSource.PendingSourceVersion = null;

            Assert.IsTrue(ManagedCertificate.RequiresRedeployment(item), "With nothing pending the certificate it holds is redeployed");
        }

        [TestMethod, Description("A manual-trigger task which failed when last run on demand does not make the item due for redeployment")]
        public void FailedManualTaskDoesNotRequireRedeployment()
        {
            // an automated request never runs a manual-trigger task, so a redeploy could never clear its failure.
            // Counting it would select the item for redeployment after every back off, for the life of the certificate
            var item = CreateItemWithFailedManualTask();

            Assert.IsFalse(ManagedCertificate.HasRecordedDeploymentFailure(item), "A manual task is not part of automated deployment");
            Assert.IsFalse(ManagedCertificate.RequiresRedeployment(item));
        }

        [TestMethod, Description("A failed manual-trigger task does not make an automated request fail")]
        public void FailedManualTaskDoesNotFailTheRequest()
        {
            var item = CreateItemWithFailedManualTask();

            var requestResult = new CertificateRequestResult(item, isSuccess: true, "Certificate issued.")
            {
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "Certificate issued." }
            };

            Assert.AreEqual(RequestState.Success, InvokeResolveOverallRenewalStatus(item, requestResult),
                "The request did everything it was asked to do; the manual task was not part of it");
        }

        [TestMethod, Description("A failed automated task still makes the item due for redeployment when a manual task is also configured")]
        public void FailedAutomatedTaskStillRequiresRedeploymentAlongsideManualTask()
        {
            var item = CreateItemWithFailedManualTask();

            item.PostRequestTasks[0].LastRunStatus = RequestState.Error;
            item.PostRequestTasks[0].LastResult = "Endpoint unavailable";
            item.PostRequestTasks[1].LastRunStatus = RequestState.Success;

            Assert.IsTrue(ManagedCertificate.HasRecordedDeploymentFailure(item));
            Assert.IsTrue(ManagedCertificate.RequiresRedeployment(item), "The automated task's failure is what the redeploy exists to retry");
        }
    }
}
