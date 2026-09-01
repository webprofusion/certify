using System;
using System.Collections.Generic;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how a certificate subscription behaves while it waits for a maintenance window. Waiting for a window
    /// is normal operation rather than a failure, so the wait must not be reported against the item as a problem, and
    /// must not overwrite the recorded outcome of the deployment which actually took place
    /// </summary>
    [TestClass]
    public class SubscriptionMaintenanceWindowTests
    {
        private static RenewalPrefs CreatePrefsWithClosedWindow()
        {
            // a window which only opens on a day other than today, so the check is always outside it
            var otherDay = DateTimeOffset.Now.DayOfWeek == DayOfWeek.Sunday
                ? MaintenanceDays.Wednesday
                : MaintenanceDays.Sunday;

            return new RenewalPrefs
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
        }

        private static ManagedCertificate CreateSubscriptionWithPendingUpdate()
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
                DateLastRenewalAttempt = now.AddDays(-1),
                CertificateThumbprintHash = "ABC123",
                LastRenewalStatus = RequestState.Success,
                RenewalFailureCount = 0,
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "External certificate pulled from Management Hub." },
                LastBindingDeployment = new RequestStageStatus { Status = RequestState.Success, Message = "External certificate deployment completed successfully." },
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = "instance-a/cert-1",
                    PollIntervalMinutes = 30,
                    PendingSourceVersion = "v2"
                },
                RequestConfig = new CertRequestConfig { PrimaryDomain = "sub.example.com" }
            };
        }

        [TestMethod, Description("A subscription waiting for its maintenance window is reported as deferred by the renewal plan")]
        public void WaitingForWindowIsReportedByTheRenewalPlan()
        {
            var item = CreateSubscriptionWithPendingUpdate();
            var prefs = CreatePrefsWithClosedWindow();

            var plan = RenewalScheduleCalculator.CalculateNextRenewalAttempt(item, prefs);

            Assert.IsTrue(plan.IsDeferredByMaintenanceWindow, "The renewal plan is the channel which reports the wait to the UI");
            Assert.Contains("Overnight", plan.Reason, "The reason should name the window the item is waiting for");
        }

        [TestMethod, Description("Deferring for a maintenance window leaves the item completely untouched")]
        public void DeferringForWindowDoesNotChangeTheItem()
        {
            var manager = new CertifyManager();
            var item = CreateSubscriptionWithPendingUpdate();

            var result = manager.DeferSubscriptionForMaintenanceWindow(item, "Limited to Maintenance Window 'Overnight'. Next window: 01/01/2026 02:00");

            Assert.AreEqual(CertifyManager.SubscriptionRequestOutcome.Deferred, result.Outcome);
            Assert.Contains("Overnight", result.Message);

            // waiting for a window is not a failure and nothing was attempted, so none of the item's recorded state moves
            Assert.AreEqual(RequestState.Success, item.LastRenewalStatus, "The wait must not be recorded as a warning against the item");
            Assert.IsNull(item.RenewalFailureMessage, "The wait is not a failure and must not populate the failure message");
            Assert.AreEqual(0, item.RenewalFailureCount);
            Assert.AreEqual(ManagedCertificateHealth.OK, item.Health, "An item waiting for its window has nothing wrong with it");

            // the last binding deployment status is what the deployment retry pass reads to find items which failed to
            // deploy, so a deferral overwriting it would hide a real failure from that pass
            Assert.AreEqual(RequestState.Success, item.LastBindingDeployment?.Status, "The deferral must not overwrite the status of the deployment which actually took place");
            Assert.AreEqual("External certificate deployment completed successfully.", item.LastBindingDeployment?.Message);

            // the pending update must survive so it is applied once the window opens
            Assert.AreEqual("v2", item.ExternalSource?.PendingSourceVersion);
        }

        [TestMethod, Description("A recorded source error is not cleared by waiting for a maintenance window")]
        public void DeferringForWindowDoesNotClearARecordedSourceError()
        {
            var manager = new CertifyManager();
            var item = CreateSubscriptionWithPendingUpdate();

            item.ExternalSource.LastError = "ManagementHub source returned 403";

            manager.DeferSubscriptionForMaintenanceWindow(item, "Limited to Maintenance Window 'Overnight'.");

            Assert.AreEqual("ManagementHub source returned 403", item.ExternalSource.LastError,
                "The last attempt did fail, and deferring the next one does not resolve it");
        }

        [TestMethod, Description("The start of a maintenance window wait is tracked so it is only logged once")]
        public void RepeatedDeferralsAreTrackedAsOneWait()
        {
            var manager = new CertifyManager();
            var item = CreateSubscriptionWithPendingUpdate();

            Assert.IsFalse(manager.IsSubscriptionAwaitingMaintenanceWindow(item.Id));

            manager.DeferSubscriptionForMaintenanceWindow(item, "Limited to Maintenance Window 'Overnight'.");

            Assert.IsTrue(manager.IsSubscriptionAwaitingMaintenanceWindow(item.Id),
                "The wait is recorded, so the passes which follow it do not each add a log entry");

            var repeated = manager.DeferSubscriptionForMaintenanceWindow(item, "Limited to Maintenance Window 'Overnight'.");

            Assert.AreEqual(CertifyManager.SubscriptionRequestOutcome.Deferred, repeated.Outcome, "Every pass still defers while the window is closed");
        }

        [TestMethod, Description("A deferred subscription request reports the item's existing state rather than a new problem")]
        public void DeferredRequestReportsExistingState()
        {
            Assert.AreEqual(RequestState.Success,
                CertifyManager.ResolveSubscriptionRequestState(CertifyManager.SubscriptionRequestOutcome.Deferred, storedRenewalStatus: RequestState.Success),
                "A healthy item which is only waiting for its window stays healthy");

            Assert.AreEqual(RequestState.Error,
                CertifyManager.ResolveSubscriptionRequestState(CertifyManager.SubscriptionRequestOutcome.Deferred, storedRenewalStatus: RequestState.Error),
                "A deferral must not clear a failure recorded by an earlier attempt");
        }

        [TestMethod, Description("A deferred subscription request performs no deployment tasks")]
        public void DeferredRequestPerformsNoDeploymentTasks()
        {
            var item = CreateSubscriptionWithPendingUpdate();
            item.PostRequestTasks = new System.Collections.ObjectModel.ObservableCollection<Certify.Config.DeploymentTaskConfig>
            {
                new Certify.Config.DeploymentTaskConfig { TaskName = "Upload" }
            };

            var deferredResult = new CertificateRequestResult(item) { IsSubscriptionUpdateDeferred = true };

            Assert.IsFalse(CertifyManager.ShouldPerformPostRequestTasks(item, deferredResult, skipTasks: false),
                "Nothing was deployed, so the deployment tasks have nothing to react to");
        }

        [TestMethod, Description("A deferred subscription request is not broadcast as request progress")]
        public void DeferredAutomaticRequestIsNotBroadcast()
        {
            Assert.IsFalse(CertifyManager.ShouldReportSubscriptionRequestProgress(
                CertifyManager.SubscriptionRequestMode.Automatic,
                CertifyManager.SubscriptionRequestOutcome.Deferred),
                "A wait re-evaluated every pass must not report progress on every pass");
        }
    }
}
