using System;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for the shared renewal schedule calculation used by the renewal process and reported to the UIs as the
    /// renewal plan for an item
    /// </summary>
    [TestClass]
    public class RenewalScheduleCalculatorTests
    {
        private static ManagedCertificate GetItemNotYetDue()
        {
            return new ManagedCertificate
            {
                Id = "test-item",
                IncludeInAutoRenew = true,
                DateStart = DateTimeOffset.UtcNow.AddDays(-10),
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-10),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(80)
            };
        }

        private static RenewalPrefs GetPrefs(params MaintenanceWindow[] windows)
        {
            return new RenewalPrefs
            {
                RenewalIntervalDays = 30,
                RenewalIntervalMode = RenewalIntervalModes.PercentageLifetime,
                MaintenanceWindows = [.. windows],
                DefaultMaintenanceWindowId = windows.Length > 0 ? windows[0].Id : null
            };
        }

        [TestMethod, Description("A renewal date derived from the renewal interval is not reported as a scheduled renewal")]
        public void IntervalBasedRenewal_IsNotReportedAsScheduled()
        {
            var item = GetItemNotYetDue();

            var plan = RenewalScheduleCalculator.CalculateNextRenewalAttempt(item, GetPrefs());

            Assert.IsNotNull(plan);
            Assert.IsFalse(plan.IsRenewalDue, "Item should not yet be due for renewal.");
            Assert.IsFalse(plan.IsRenewalScheduled, "An interval based renewal date is an estimate, not a scheduled renewal.");
        }

        [TestMethod, Description("A CA suggested (ARI) renewal window ahead of the normal interval is reported as a scheduled renewal")]
        public void ScheduledRenewalAheadOfInterval_IsReportedAsScheduled()
        {
            var item = GetItemNotYetDue();
            var scheduledDate = DateTimeOffset.UtcNow.AddDays(5);
            item.DateNextScheduledRenewalAttempt = scheduledDate;

            var plan = RenewalScheduleCalculator.CalculateNextRenewalAttempt(item, GetPrefs());

            Assert.IsNotNull(plan);
            Assert.IsTrue(plan.IsRenewalScheduled, "A renewal set to happen ahead of the normal interval is a scheduled renewal.");
            Assert.AreEqual(scheduledDate, plan.DateNextRenewalAttempt);
        }

        [TestMethod, Description("A scheduled renewal date later than the normal interval does not become the planned renewal")]
        public void ScheduledRenewalAfterInterval_IsNotReportedAsScheduled()
        {
            var item = GetItemNotYetDue();

            // scheduled well beyond expiry, so the normal interval still decides when renewal happens
            item.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow.AddDays(200);

            var plan = RenewalScheduleCalculator.CalculateNextRenewalAttempt(item, GetPrefs());

            Assert.IsNotNull(plan);
            Assert.IsFalse(plan.IsRenewalScheduled, "The scheduled date is not what drives renewal, so it must not be emphasised as scheduled.");
            Assert.AreNotEqual(item.DateNextScheduledRenewalAttempt, plan.DateNextRenewalAttempt);
        }

        [TestMethod, Description("A renewal which is due outside its maintenance window is deferred until the window next opens")]
        public void RenewalDueOutsideMaintenanceWindow_IsDeferredToNextWindow()
        {
            var window = new MaintenanceWindow
            {
                Id = "window-1",
                Name = "Sundays",
                IsEnabled = true,
                Days = MaintenanceDays.Sunday,
                StartTime = TimeSpan.FromHours(1),
                EndTime = TimeSpan.FromHours(2)
            };

            var item = new ManagedCertificate
            {
                Id = "test-item",
                IncludeInAutoRenew = true,
                MaintenanceWindowId = window.Id,
                DateStart = DateTimeOffset.UtcNow.AddDays(-80),
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-80),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(5)
            };

            // a Monday, outside the configured Sunday window
            var checkDate = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

            var plan = RenewalScheduleCalculator.CalculateNextRenewalAttempt(item, GetPrefs(window), checkDate);

            Assert.IsNotNull(plan);
            Assert.IsTrue(plan.IsRenewalDue, "Item should be due for renewal based on its lifetime.");
            Assert.IsTrue(plan.IsDeferredByMaintenanceWindow, "A renewal which is due outside the window must be deferred.");
            Assert.IsTrue(plan.DateNextRenewalAttempt > checkDate, "The next attempt should be the next window occurrence.");
            StringAssert.Contains(plan.Reason, "Limited to Maintenance Window");
        }

        [TestMethod, Description("A future planned renewal is moved to the maintenance window it will actually be attempted in")]
        public void PlannedRenewalWithMaintenanceWindow_MovesToNextWindowOccurrence()
        {
            var window = new MaintenanceWindow
            {
                Id = "window-1",
                Name = "Sundays",
                IsEnabled = true,
                Days = MaintenanceDays.Sunday,
                StartTime = TimeSpan.FromHours(1),
                EndTime = TimeSpan.FromHours(2)
            };

            var item = GetItemNotYetDue();
            item.MaintenanceWindowId = window.Id;

            var planWithoutWindow = RenewalScheduleCalculator.CalculateNextRenewalAttempt(item, GetPrefs());
            var planWithWindow = RenewalScheduleCalculator.CalculateNextRenewalAttempt(item, GetPrefs(window));

            Assert.IsNotNull(planWithoutWindow?.DateNextRenewalAttempt);
            Assert.IsNotNull(planWithWindow?.DateNextRenewalAttempt);

            Assert.IsTrue(planWithWindow.DateNextRenewalAttempt >= planWithoutWindow.DateNextRenewalAttempt,
                "The planned renewal cannot happen before the maintenance window which follows it.");
            Assert.AreEqual(DayOfWeek.Sunday, planWithWindow.DateNextRenewalAttempt.Value.DayOfWeek);
        }

        [TestMethod, Description("An item with no maintenance window configured is not reported as window deferred")]
        public void NoMaintenanceWindow_IsNotDeferred()
        {
            var item = GetItemNotYetDue();

            var plan = RenewalScheduleCalculator.CalculateNextRenewalAttempt(item, GetPrefs());

            Assert.IsNotNull(plan);
            Assert.IsFalse(plan.IsDeferredByMaintenanceWindow);
        }

        [TestMethod, Description("The renewal plan survives the serializers used between an instance, the hub and the UI")]
        public void RenewalPlan_RoundTripsViaInstanceAndClientSerializers()
        {
            var item = GetItemNotYetDue();
            item.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow.AddDays(5);
            item.RenewalPlan = RenewalScheduleCalculator.CalculateNextRenewalAttempt(item, GetPrefs());

            Assert.IsNotNull(item.RenewalPlan);

            // instance to hub uses System.Text.Json
            var systemTextJson = System.Text.Json.JsonSerializer.Serialize(item, Certify.Shared.JsonOptions.DefaultJsonSerializerOptions);
            var viaSystemTextJson = System.Text.Json.JsonSerializer.Deserialize<ManagedCertificate>(systemTextJson, Certify.Shared.JsonOptions.DefaultJsonSerializerOptions);

            Assert.IsNotNull(viaSystemTextJson?.RenewalPlan, "The renewal plan must reach the hub from the instance.");
            Assert.AreEqual(item.RenewalPlan.DateNextRenewalAttempt, viaSystemTextJson.RenewalPlan.DateNextRenewalAttempt);
            Assert.AreEqual(item.RenewalPlan.IsRenewalScheduled, viaSystemTextJson.RenewalPlan.IsRenewalScheduled);
            Assert.AreEqual(item.RenewalPlan.Reason, viaSystemTextJson.RenewalPlan.Reason);

            // hub to UI uses the generated api client, which serializes with Newtonsoft
            var newtonsoftJson = Newtonsoft.Json.JsonConvert.SerializeObject(item);
            var viaNewtonsoft = Newtonsoft.Json.JsonConvert.DeserializeObject<ManagedCertificate>(newtonsoftJson);

            Assert.IsNotNull(viaNewtonsoft?.RenewalPlan, "The renewal plan must reach the UI from the hub.");
            Assert.AreEqual(item.RenewalPlan.DateNextRenewalAttempt, viaNewtonsoft.RenewalPlan.DateNextRenewalAttempt);
            Assert.AreEqual(item.RenewalPlan.IsRenewalScheduled, viaNewtonsoft.RenewalPlan.IsRenewalScheduled);
            Assert.AreEqual(item.RenewalPlan.Reason, viaNewtonsoft.RenewalPlan.Reason);
        }
    }
}
