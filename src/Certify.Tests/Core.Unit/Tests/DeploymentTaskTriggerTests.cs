using System;
using Certify.Config;
using Certify.Core.Management.DeploymentTasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class DeploymentTaskTriggerTests
    {
        private static bool InvokeShouldContinueAfterPreviousTaskFailure(TaskTriggerType taskTrigger, bool primaryRequestSucceeded)
            => DeploymentTaskRunner.ShouldContinueAfterPreviousTaskFailure(taskTrigger, primaryRequestSucceeded);

        private static bool InvokeShouldSkipTaskBecausePreviousTaskFailed(bool previousTaskFailed, bool runIfLastStepFailed, TaskTriggerType taskTrigger, bool primaryRequestSucceeded)
            => DeploymentTaskRunner.ShouldSkipTaskBecausePreviousTaskFailed(previousTaskFailed, runIfLastStepFailed, taskTrigger, primaryRequestSucceeded);

        [DataTestMethod]
        [DataRow(TaskTriggerType.ANY_STATUS, false, true)]
        [DataRow(TaskTriggerType.ON_ERROR, false, true)]
        [DataRow(TaskTriggerType.ON_TASK_ERROR, false, true)]
        [DataRow(TaskTriggerType.ON_SUCCESS, false, false)]
        [DataRow(TaskTriggerType.ANY_STATUS, true, false)]
        [DataRow(TaskTriggerType.ON_TASK_ERROR, true, true)]
        public void ShouldContinueAfterPreviousTaskFailure_ReturnsExpectedResult(TaskTriggerType taskTrigger, bool primaryRequestSucceeded, bool expected)
        {
            var result = InvokeShouldContinueAfterPreviousTaskFailure(taskTrigger, primaryRequestSucceeded);

            Assert.AreEqual(expected, result);
        }

        [DataTestMethod]
        [DataRow(true, true, TaskTriggerType.ON_SUCCESS, true, false)]
        [DataRow(true, false, TaskTriggerType.ON_SUCCESS, true, true)]
        [DataRow(true, false, TaskTriggerType.ANY_STATUS, false, false)]
        [DataRow(false, false, TaskTriggerType.ON_SUCCESS, true, false)]
        public void ShouldSkipTaskBecausePreviousTaskFailed_HonorsRunIfLastStepFailed(bool previousTaskFailed, bool runIfLastStepFailed, TaskTriggerType taskTrigger, bool primaryRequestSucceeded, bool expected)
        {
            var result = InvokeShouldSkipTaskBecausePreviousTaskFailed(previousTaskFailed, runIfLastStepFailed, taskTrigger, primaryRequestSucceeded);

            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [DataRow(true, 30, true)]
        [DataRow(true, -1, false)]
        [DataRow(false, 30, false)]
        public void HasUsableCertificate_RequiresAnUnexpiredCertificateWeHold(bool hasCertificate, int daysUntilExpiry, bool expected)
        {
            // an individually executed task, and a manual subscription request which found no fresh certificate at the
            // source, both deploy the last good certificate - so ON_SUCCESS applies whenever that certificate is usable
            var managedCert = new ManagedCertificate
            {
                Id = "test-item",
                CertificateThumbprintHash = hasCertificate ? "abc123" : null,
                DateExpiry = DateTimeOffset.UtcNow.AddDays(daysUntilExpiry)
            };

            Assert.AreEqual(expected, ManagedCertificate.HasUsableCertificate(managedCert));
        }

        private static ManagedCertificate GetManagedCertificateWithPostRequestTask()
        {
            return new ManagedCertificate
            {
                Id = "test-item",
                PostRequestTasks = [new DeploymentTaskConfig { Id = "task-1", TaskTrigger = TaskTriggerType.ON_SUCCESS }]
            };
        }

        [TestMethod]
        public void ShouldPerformPostRequestTasks_AutomaticSubscriptionWithoutUpdate_SkipsTasks()
        {
            // an automatic poll which found no certificate to apply should simply try again later
            var result = new CertificateRequestResult(GetManagedCertificateWithPostRequestTask()) { IsSubscriptionUpdateDeferred = true };

            Assert.IsFalse(CertifyManager.ShouldPerformPostRequestTasks(result.ManagedItem, result, skipTasks: false));
        }

        [TestMethod]
        public void ShouldPerformPostRequestTasks_SubscriptionWithoutCertificateChange_RunsTasks()
        {
            // a manual subscription request runs its tasks even when the subscribed certificate did not change,
            // deploying the last good certificate we hold
            var result = new CertificateRequestResult(GetManagedCertificateWithPostRequestTask())
            {
                IsSubscriptionUpdateDeferred = false,
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Success }
            };

            Assert.IsTrue(CertifyManager.ShouldPerformPostRequestTasks(result.ManagedItem, result, skipTasks: false));
        }

        [TestMethod]
        public void ShouldPerformPostRequestTasks_SubscriptionFailure_RunsTasksSoErrorTriggersApply()
        {
            // a failed subscription fetch or deployment still needs ON_ERROR tasks to run
            var result = new CertificateRequestResult(GetManagedCertificateWithPostRequestTask())
            {
                IsSuccess = false,
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Error }
            };

            Assert.IsTrue(CertifyManager.ShouldPerformPostRequestTasks(result.ManagedItem, result, skipTasks: false));
        }

        [TestMethod]
        public void ShouldPerformPostRequestTasks_SkipsWhenRequestedOrNotApplicable()
        {
            var managedCert = GetManagedCertificateWithPostRequestTask();
            var result = new CertificateRequestResult(managedCert);

            Assert.IsFalse(CertifyManager.ShouldPerformPostRequestTasks(managedCert, result, skipTasks: true), "Tasks should not run when explicitly skipped.");

            Assert.IsFalse(CertifyManager.ShouldPerformPostRequestTasks(new ManagedCertificate { Id = "test-item" }, result, skipTasks: false), "Tasks should not run when there are none configured.");

            var awaitingUser = GetManagedCertificateWithPostRequestTask();
            awaitingUser.LastRenewalStatus = RequestState.Paused;
            Assert.AreEqual(ManagedCertificateHealth.AwaitingUser, awaitingUser.Health, "Test setup should produce an item awaiting user input.");
            Assert.IsFalse(CertifyManager.ShouldPerformPostRequestTasks(awaitingUser, result, skipTasks: false), "Tasks should not run while the request is awaiting user input.");
        }

        [TestMethod]
        public void IsWithinMaintenanceWindow_OutsideWindow_OnlyDefersScheduledRenewals()
        {
            // maintenance windows constrain scheduled renewals; a user initiated request is not routed through this check
            var window = new MaintenanceWindow
            {
                Id = "window-1",
                Name = "Weekends",
                IsEnabled = true,
                Days = MaintenanceDays.Sunday,
                StartTime = TimeSpan.FromHours(1),
                EndTime = TimeSpan.FromHours(2)
            };

            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = [window],
                DefaultMaintenanceWindowId = window.Id
            };

            var item = new ManagedCertificate { Id = "test-item", MaintenanceWindowId = window.Id };

            // a Monday, outside the configured Sunday window
            var outsideWindow = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

            var check = RenewalManager.IsWithinMaintenanceWindow(item, prefs, outsideWindow);

            Assert.IsFalse(check.IsWithinWindow, "Scheduled renewals should be deferred outside the maintenance window.");
        }

        [TestMethod]
        public void ResolveSubscriptionRequestState_FailedRequest_DoesNotInheritPreviousSuccess()
        {
            // a request which failed before recording any status must not report success just because the item
            // was last renewed successfully, otherwise ON_SUCCESS deployment tasks would run for a failed request
            Assert.AreEqual(RequestState.Warning, CertifyManager.ResolveSubscriptionRequestState(CertifyManager.SubscriptionRequestOutcome.Failed, storedRenewalStatus: RequestState.Success));

            Assert.AreEqual(RequestState.Warning, CertifyManager.ResolveSubscriptionRequestState(CertifyManager.SubscriptionRequestOutcome.Failed, storedRenewalStatus: null));
        }

        [DataTestMethod]
        [DataRow(RequestState.Error)]
        [DataRow(RequestState.Warning)]
        [DataRow(RequestState.Paused)]
        public void ResolveSubscriptionRequestState_FailedRequest_UsesRecordedFailureStatus(RequestState storedStatus)
        {
            Assert.AreEqual(storedStatus, CertifyManager.ResolveSubscriptionRequestState(CertifyManager.SubscriptionRequestOutcome.Failed, storedStatus));
        }

        [TestMethod]
        public void ResolveSubscriptionRequestState_CompletedRequest_IsSuccess()
        {
            Assert.AreEqual(RequestState.Success, CertifyManager.ResolveSubscriptionRequestState(CertifyManager.SubscriptionRequestOutcome.Completed, storedRenewalStatus: RequestState.Error));
        }

        [TestMethod]
        public void ResolveSubscriptionRequestState_DeferredRequest_ReportsExistingState()
        {
            // a deferred check attempted nothing, so it neither invents a new problem nor clears an existing one
            Assert.AreEqual(RequestState.Success, CertifyManager.ResolveSubscriptionRequestState(CertifyManager.SubscriptionRequestOutcome.Deferred, storedRenewalStatus: RequestState.Success));

            Assert.AreEqual(RequestState.Success, CertifyManager.ResolveSubscriptionRequestState(CertifyManager.SubscriptionRequestOutcome.Deferred, storedRenewalStatus: null));

            Assert.AreEqual(RequestState.Error, CertifyManager.ResolveSubscriptionRequestState(CertifyManager.SubscriptionRequestOutcome.Deferred, storedRenewalStatus: RequestState.Error));
        }

        [TestMethod]
        public void ShouldReportSubscriptionRequestProgress_DeferredAutomaticRequest_IsNotReported()
        {
            // a deferred automatic request attempted nothing and changed nothing, so reporting it would only add a
            // no-op entry to the request progress shown by connected UI clients (the app and the hub)
            Assert.IsFalse(CertifyManager.ShouldReportSubscriptionRequestProgress(CertifyManager.SubscriptionRequestMode.Automatic, CertifyManager.SubscriptionRequestOutcome.Deferred));
        }

        [TestMethod]
        public void ShouldReportSubscriptionRequestProgress_AutomaticRequestWhichDidSomething_IsReported()
        {
            Assert.IsTrue(CertifyManager.ShouldReportSubscriptionRequestProgress(CertifyManager.SubscriptionRequestMode.Automatic, CertifyManager.SubscriptionRequestOutcome.Completed));

            Assert.IsTrue(CertifyManager.ShouldReportSubscriptionRequestProgress(CertifyManager.SubscriptionRequestMode.Automatic, CertifyManager.SubscriptionRequestOutcome.Failed));
        }

        [TestMethod]
        public void ShouldReportSubscriptionRequestProgress_ManualRequest_IsAlwaysReported()
        {
            // the user started this request and is waiting on its outcome, including being told nothing was done
            Assert.IsTrue(CertifyManager.ShouldReportSubscriptionRequestProgress(CertifyManager.SubscriptionRequestMode.Manual, CertifyManager.SubscriptionRequestOutcome.Completed));

            Assert.IsTrue(CertifyManager.ShouldReportSubscriptionRequestProgress(CertifyManager.SubscriptionRequestMode.Manual, CertifyManager.SubscriptionRequestOutcome.Deferred));

            Assert.IsTrue(CertifyManager.ShouldReportSubscriptionRequestProgress(CertifyManager.SubscriptionRequestMode.Manual, CertifyManager.SubscriptionRequestOutcome.Failed));
        }

        [TestMethod]
        public void IsActionableSubscription_RequiresSourceTypeAndReference()
        {
            // an unconfigured subscription is still a subscription, so it must never fall through to the ACME path,
            // but there is nothing to fetch and nothing to fetch it from
            var unconfigured = new ManagedCertificate { Id = "test-item", ItemType = ManagedCertificateType.SSL_ExternalSubscription };

            Assert.IsTrue(unconfigured.IsSubscription, "An item of the subscription type is a subscription regardless of its configuration.");
            Assert.IsFalse(unconfigured.IsActionableSubscription, "A subscription with no source configuration cannot be requested.");
            Assert.IsFalse(unconfigured.IsExternallyManaged, "An unconfigured subscription is not an externally managed item.");

            var sourceTypeOnly = new ManagedCertificate
            {
                Id = "test-item",
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                ExternalSource = new ExternalCertificateSubscription { SourceType = ExternalCertificateSourceTypes.ManagementHub }
            };

            Assert.IsFalse(sourceTypeOnly.IsActionableSubscription, "A subscription with no external reference cannot be requested.");

            var configured = new ManagedCertificate
            {
                Id = "test-item",
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    ExternalReference = "source-item-1"
                }
            };

            Assert.IsTrue(configured.IsActionableSubscription);

            // a legacy subscription, stored before subscriptions had their own item type
            var legacy = new ManagedCertificate
            {
                Id = "test-item",
                ItemType = ManagedCertificateType.SSL_ExternallyManaged,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    ExternalReference = "source-item-1"
                }
            };

            Assert.IsTrue(legacy.IsActionableSubscription, "A legacy subscription is recognised by its configured external source.");
            Assert.IsFalse(legacy.IsExternallyManaged, "A legacy subscription is not an externally managed item.");
        }
    }
}
