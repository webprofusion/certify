using System;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class RenewalRequiredTests
    {
        [TestMethod, Description("Ensure a site which should be renewed correctly requires renewal, where failure has previously occurred")]
        public void TestCheckAutoRenewalPeriodRequiredWithFailures()
        {
            // setup
            var renewalPeriodDays = 14;
            var renewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-15),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(60),
                DateLastRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-12),
                LastRenewalStatus = RequestState.Error,
                RenewalFailureCount = 2
            };

            // perform check
            var renewalDueCheck
                = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode, true);

            // assert result
            Assert.IsTrue(renewalDueCheck.IsRenewalDue, "Renewal should be required");

            managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-15),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(60),
                DateLastRenewalAttempt = null,
                LastRenewalStatus = null,
                RenewalFailureCount = 0
            };

            // perform check
            renewalDueCheck = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode, true);

            // assert result
            Assert.IsTrue(renewalDueCheck.IsRenewalDue, "Site with no previous status - Renewal should be required");
        }

        [TestMethod, Description("Ensure renewal hold when site requires immediate renewal but failure has previously occurred")]
        public void TestCheckAutoRenewalPeriodRequiredWithFailuresHold()
        {
            // setup
            var renewalPeriodDays = 14;
            var renewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-15),
                DateStart = DateTimeOffset.UtcNow.AddDays(-15),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(60),
                DateLastRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-12),
                LastRenewalStatus = RequestState.Error,
                RenewalFailureCount = 100, // high number of failures
                DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-0.1) // scheduled renewal set to become due
            };

            // perform check
            var renewalDueCheck
                = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode, true);

            // assert result
            Assert.IsTrue(renewalDueCheck.IsRenewalDue, "Renewal should be required");
            Assert.IsTrue(renewalDueCheck.IsRenewalOnHold, "Renewal should be on hold");
            Assert.AreEqual(renewalDueCheck.HoldHrs, 48, "Hold should be for 48 Hrs");

            managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-49);
            
            // perform check as if last attempt was over 48rs ago, item should require renewal and not be on hold
            renewalDueCheck = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode, true);

            // assert result
            Assert.IsTrue(renewalDueCheck.IsRenewalDue, "Renewal should be required");
            Assert.IsFalse(renewalDueCheck.IsRenewalOnHold, "Renewal should not be on hold");
        }

        [TestMethod, Description("Ensure renewal hold when item has failed more than 100 times")]
        public void TestCheckAutoRenewalWithTooManyFailuresHold()
        {
            // setup
            var renewalPeriodDays = 14;
            var renewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateRenewed = DateTimeOffset.UtcNow.AddDays(-15),
                DateStart = DateTimeOffset.UtcNow.AddDays(-15),
                DateExpiry = DateTimeOffset.UtcNow.AddDays(60),
                DateLastRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-12),
                LastRenewalStatus = RequestState.Error,
                RenewalFailureCount = 1001, // too many failures
                DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-0.1) // scheduled renewal set to become due
            };

            // perform check
            var renewalDueCheck
                = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode, true);

            // assert result
            Assert.IsTrue(renewalDueCheck.IsRenewalDue, "Renewal should be required");
            Assert.IsTrue(renewalDueCheck.IsRenewalOnHold, "Renewal should be on hold");
            Assert.AreEqual(renewalDueCheck.HoldHrs, 48, "Hold should be for 48 Hrs");

            managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-49);

            // perform check as if last attempt was over 48rs ago, item should require renewal and not be on hold
            renewalDueCheck = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode, true);

            // assert result
            Assert.IsTrue(renewalDueCheck.IsRenewalDue, "Renewal should be required");
            Assert.IsTrue(renewalDueCheck.IsRenewalOnHold, "Renewal should permanently be on hol, too many failures.");
        }

        [TestMethod, Description("Ensure a site which should be renewed correctly requires renewal")]
        public void TestCheckAutoRenewalPeriodRequired()
        {
            // setup
            var renewalPeriodDays = 14;
            var renewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var managedCertificate = new ManagedCertificate { IncludeInAutoRenew = true, DateRenewed = DateTimeOffset.UtcNow.AddDays(-15), DateExpiry = DateTimeOffset.UtcNow.AddDays(60) };

            // perform check
            var isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode);

            // assert result
            Assert.IsTrue(isRenewalRequired.IsRenewalDue, "Renewal should be required");
        }

        [TestMethod, Description("Ensure a site which should not be renewed correctly does not require renewal")]
        public void TestCheckAutoRenewalPeriodNotRequired()
        {
            // setup : set renewal period to 30 days, last renewal 15 days ago. Renewal should not be
            // required yet.
            var renewalPeriodDays = 30;
            var renewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var managedCertificate = new ManagedCertificate { IncludeInAutoRenew = true, DateRenewed = DateTimeOffset.UtcNow.AddDays(-15), DateExpiry = DateTimeOffset.UtcNow.AddDays(60) };

            // perform check
            var isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode);

            // assert result
            Assert.IsFalse(isRenewalRequired.IsRenewalDue, "Renewal should not be required");

            var expectedRenewal = managedCertificate.DateRenewed.Value.AddDays(renewalPeriodDays);
            Assert.IsTrue((expectedRenewal - isRenewalRequired.DateNextRenewalAttempt).Value.TotalMinutes < 1, "Planned renewal should be within a minute of the date last renewed plus renewal interval");
        }

        [TestMethod, Description("Ensure item which should not normally be renewed correctly requires renewal if DateNextScheduledRenewalAttempt is set and due")]
        public void TestDateNextScheduledRenewalAttempt()
        {
            // setup : set renewal period to 30 days, last renewal 15 days ago.

            var renewalPeriodDays = 30;
            var renewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var managedCertificate = new ManagedCertificate { IncludeInAutoRenew = true, DateRenewed = DateTimeOffset.UtcNow.AddDays(-15), DateExpiry = DateTimeOffset.UtcNow.AddDays(60) };

            // set scheduled renewal so it should become due
            managedCertificate.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow.AddDays(-0.1);

            // perform check
            var isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode);

            // assert result
            Assert.IsTrue(isRenewalRequired.IsRenewalDue, "Renewal should be required due to scheduled date");

            // set scheduled renewal so it should not become due
            managedCertificate.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow.AddDays(45);

            // perform check
            isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode);

            // assert result
            Assert.IsFalse(isRenewalRequired.IsRenewalDue, "Renewal should not be required due to scheduled date in future");
        }

        [TestMethod, Description("Ensure a site with unknown date for last renewal should require renewal")]
        public void TestCheckAutoRenewalPeriodUnknownLastRenewal()
        {
            // setup : set renewal period to 14 days, last renewal unknown.

            var renewalPeriodDays = 14;
            var renewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var managedCertificate = new ManagedCertificate { IncludeInAutoRenew = true, DateExpiry = DateTimeOffset.UtcNow.AddDays(60) };

            // perform check
            var isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode);

            // assert result
            Assert.IsTrue(isRenewalRequired.IsRenewalDue, "Renewal should be required");
        }

        [TestMethod, Description("Cert that has a short lifetime should renew if it's expiry falls before the normal renewal interval")]
        public void TestCheckAutoRenewalWithShortCertLifetime()
        {
            // setup : set renewal period to 14 days. Cert has an extra short 12hr lifetime and so needs to renew before 12hrs have elapsed regardless of default renewal mode.

            var renewalPeriodDays = 14;
            var renewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var startDate = DateTimeOffset.UtcNow.AddDays(-0.5);
            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateStart = startDate,
                DateRenewed = startDate,
                DateExpiry = startDate.AddDays(0.5)
            };

            // perform check
            var isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode);

            // assert result
            Assert.IsTrue(isRenewalRequired.IsRenewalDue, "Renewal should be required, certs lifetime is shorter than renewal interval");

            Assert.AreEqual(12, (int)isRenewalRequired.CertLifetime.Value.TotalHours, "Renewal should be required, certs lifetime is shorter than renewal interval");

            Assert.IsTrue(isRenewalRequired.DateNextRenewalAttempt.Value > managedCertificate.DateStart.Value.AddMinutes(30), "Cert should not try to instantly renew");

            Assert.IsTrue(isRenewalRequired.DateNextRenewalAttempt.Value < managedCertificate.DateStart.Value.AddHours(12), "Cert should renew before expiry time");
        }

        [TestMethod, Description("Cert with custom percentage lifetime")]
        [DataTestMethod]
        [DataRow(true, 0, 30, 50, 60, RenewalIntervalModes.PercentageLifetime, false, "30 day cert renewing at 50% lifetime, not due for renewal")]
        [DataRow(true, 15.5f, 30, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "30 day cert renewing at 50% lifetime, due for renewal")]
        [DataRow(true, 0.5f, 1, 75, 60, RenewalIntervalModes.PercentageLifetime, false, "1 day cert renewing at 75% lifetime, not due for renewal")]
        [DataRow(true, 0.76f, 1, 75, 60, RenewalIntervalModes.PercentageLifetime, true, "1 day cert renewing at 75% lifetime, due for renewal")]
        [DataRow(true, 180, 365, 90, 90, RenewalIntervalModes.PercentageLifetime, false, "365 day cert renewing at 90% lifetime, not due for renewal")]
        [DataRow(true, 180, 365, 90, 90, RenewalIntervalModes.PercentageLifetime, false, "365 day cert renewing at 90% lifetime, not due for renewal")]
        public void TestAutoRenewalWithPercentageCertLifetime(
            bool previouslyRenewed, float daysElapsed, float lifetimeDays, float customRenewalPercentage, int renewalInterval, string customIntervalMode,
            bool renewalExpected, string testDescription)
        {
            // setup 
            var renewalIntervalMode = RenewalIntervalModes.DaysAfterLastRenewal;

            var startDate = DateTimeOffset.UtcNow.AddDays(-daysElapsed);

            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateStart = startDate,
                DateExpiry = startDate.AddDays(lifetimeDays),
                CustomRenewalTarget = customRenewalPercentage,
                CustomRenewalIntervalMode = customIntervalMode,
                DateRenewed = previouslyRenewed ? (DateTimeOffset?)startDate : (DateTimeOffset?)null
            };

            // perform check
            var isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalInterval, renewalIntervalMode);

            // assert result
            Assert.AreEqual(isRenewalRequired.IsRenewalDue, renewalExpected, $"Renewal expected: {renewalExpected} : {testDescription}");

            Assert.AreEqual(lifetimeDays * 24, (int)isRenewalRequired.CertLifetime.Value.TotalHours, $"Expected cert lifetime : {testDescription}");

        }

        [TestMethod, Description("Cert with default percentage lifetime")]
        [DataTestMethod]
        [DataRow(true, 0, 30, 50, RenewalIntervalModes.PercentageLifetime, false, "30 day cert renewing at 50% lifetime, not due for renewal")]
        [DataRow(true, 15.5f, 30, 50, RenewalIntervalModes.PercentageLifetime, true, "30 day cert renewing at 50% lifetime, due for renewal")]
        [DataRow(true, 0.5f, 1, 75, RenewalIntervalModes.PercentageLifetime, false, "1 day cert renewing at 75% lifetime, not due for renewal")]
        [DataRow(true, 0.76f, 1, 75, RenewalIntervalModes.PercentageLifetime, true, "1 day cert renewing at 75% lifetime, due for renewal")]
        [DataRow(true, 180, 365, 90, RenewalIntervalModes.PercentageLifetime, false, "365 day cert renewing at 90% lifetime, not due for renewal")]
        public void TestAutoRenewalWithDefaultPercentageCertLifetime(
           bool previouslyRenewed, float daysElapsed, float lifetimeDays, int renewalInterval, string renewalIntervalMode,
           bool renewalExpected, string testDescription)
        {
            // setup 

            var startDate = DateTimeOffset.UtcNow.AddDays(-daysElapsed);

            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateStart = startDate,
                DateExpiry = startDate.AddDays(lifetimeDays),
                DateRenewed = previouslyRenewed ? (DateTimeOffset?)startDate : (DateTimeOffset?)null
            };

            // perform check
            var isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalInterval, renewalIntervalMode);

            // assert result
            Assert.AreEqual(isRenewalRequired.IsRenewalDue, renewalExpected, $"Renewal expected: {renewalExpected} : {testDescription}");

            Assert.AreEqual(lifetimeDays * 24, (int)isRenewalRequired.CertLifetime.Value.TotalHours, $"Expected cert lifetime : {testDescription}");

        }

        [TestMethod, Description("Cert with custom percentage lifetime")]
        [DataTestMethod]
        [DataRow(true, 45, 90, 30, RenewalIntervalModes.DaysBeforeExpiry, false, "90 day cert renewing at 30 days before expiry, not due for renewal")]
        [DataRow(true, 45, 90, 30, RenewalIntervalModes.DaysAfterLastRenewal, true, "90 day cert renewing at 30 days after last renewal, due for renewal")]
        [DataRow(true, 63, 90, 30, RenewalIntervalModes.DaysBeforeExpiry, true, "90 day cert renewing at 30 days before expiry, due for renewal")]
        [DataRow(true, 31, 90, 30, RenewalIntervalModes.DaysAfterLastRenewal, true, "90 day cert renewing at 30 days after last renewal, due for renewal")]
        [DataRow(true, 5, 90, 30, RenewalIntervalModes.DaysAfterLastRenewal, false, "90 day cert renewing at 30 days after last renewal, not for renewal")]
        [DataRow(true, 5, 7, 30, RenewalIntervalModes.DaysAfterLastRenewal, false, "7 day cert renewing at *30 days after last renewal*, due for renewal due to short lifetime")]
        [DataRow(true, 6, 7, 1, RenewalIntervalModes.DaysBeforeExpiry, true, "7 day cert renewing at *1 days before renewal*, due for renewal due to short lifetime")]
        [DataRow(true, 5, 7, 1, RenewalIntervalModes.DaysBeforeExpiry, false, "7 day cert renewing at *1 days before renewal*, not due for renewal")]
        public void TestAutoRenewalWithIntervalMode(
           bool previouslyRenewed, float daysElapsed, float lifetimeDays, int renewalInterval, string renewalIntervalMode,
           bool renewalExpected, string testDescription)
        {
            // setup 

            var startDate = DateTimeOffset.UtcNow.AddDays(-daysElapsed);

            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateStart = startDate,
                DateExpiry = startDate.AddDays(lifetimeDays),
                DateRenewed = previouslyRenewed ? (DateTimeOffset?)startDate : (DateTimeOffset?)null
            };

            // perform check
            var isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalInterval, renewalIntervalMode);

            // assert result
            Assert.AreEqual(isRenewalRequired.IsRenewalDue, renewalExpected, $"Renewal expected: {renewalExpected} : {testDescription}");

            Assert.AreEqual(lifetimeDays * 24, (int)isRenewalRequired.CertLifetime.Value.TotalHours, $"Expected cert lifetime : {testDescription}");

        }

        [TestMethod, Description("Cert with custom percentage lifetime, not yet successfully ordered")]
        [DataTestMethod]
        [DataRow(0, 0f, 1, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "1 day cert renewing at 50% lifetime, not yet created, due for first order")]
        [DataRow(1, 0f, 1, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "1 day cert renewing at 50% lifetime, not yet created, attempted once")]
        [DataRow(4, 1f, 0, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "Unknown lifetime cert renewing at 50% lifetime, not yet created, attempted 5 times")]
        [DataRow(5, 2.4f, 1, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "1 day cert renewing at 50% lifetime, not yet created, attempted 5 times")]
        [DataRow(10, 2.4f, 1, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "1 day cert renewing at 50% lifetime, not yet created, attempted 10 times")]
        [DataRow(15, 2.4f, 1, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "1 day cert renewing at 50% lifetime, not yet created, attempted 15 times")]
        [DataRow(0, 0f, 0.01f, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "0.01 day cert renewing at 50% lifetime, not yet created, due for first order")]
        [DataRow(25, 48f, 90f, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "90 day cert renewing at 50% lifetime, not yet created, due for first order")]
        [DataRow(5, 29f, 90f, 50, 60, RenewalIntervalModes.PercentageLifetime, true, "90 day cert renewing at 50% lifetime, not yet created, due for first order")]

        public void TestAutoStartNewCert(
            int previousAttempts, float holdHrsExpected, float lifetimeDays, float customRenewalPercentage, int renewalInterval, string customIntervalMode,
            bool renewalExpected, string testDescription)
        {
            // setup 
            var renewalIntervalMode = RenewalIntervalModes.PercentageLifetime;

            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                CustomRenewalTarget = customRenewalPercentage,
                CustomRenewalIntervalMode = customIntervalMode
            };

            if (lifetimeDays > 0)
            {
                managedCertificate.RequestConfig.PreferredExpiryDays = lifetimeDays;
            }

            if (previousAttempts > 0)
            {
                managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-0.01);
                managedCertificate.RenewalFailureCount = previousAttempts;

            }

            // perform check
            var renewalCheckResult = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalInterval, renewalIntervalMode);

            // assert result
            Assert.AreEqual(renewalExpected, renewalCheckResult.IsRenewalDue, $"Renewal expected: {renewalExpected} : {testDescription}");

            Assert.AreEqual(holdHrsExpected, renewalCheckResult.HoldHrs, $"Renewal hold expected: {holdHrsExpected} : {testDescription}");

            Assert.AreEqual(holdHrsExpected > 0, renewalCheckResult.IsRenewalOnHold, $"Renewal hold expected : {testDescription}");

        }

        [TestMethod, Description("Ensure a site with unknown date for last renewal should renew before expiry")]
        [DataTestMethod]
        [DataRow(14, 90, 13, "DaysBeforeExpiry")]
        [DataRow(14, 90, 29, "DaysBeforeExpiry")]
        [DataRow(60, 90, 30, "DaysBeforeExpiry")]
        [DataRow(30, 45, -1, "DaysAfterLastRenewal")]
        [DataRow(60, 90, 30, "DaysAfterLastRenewal")]
        [DataRow(1, 10, 180, "DaysAfterLastRenewal")]
        [DataRow(60, 14, 14, "DaysAfterLastRenewal")]
        public void TestCheckAutoRenewal30DaysBeforeExpiry(int renewalPeriodDays, int daysSinceRenewed, int daysUntilExpiry, string renewalIntervalMode)
        {
            // setup 

            var dateLastRenewed = DateTimeOffset.UtcNow.AddDays(-daysSinceRenewed);

            var managedCertificate = new ManagedCertificate
            {
                IncludeInAutoRenew = true,
                DateRenewed = dateLastRenewed,
                DateLastRenewalAttempt = dateLastRenewed,
                DateExpiry = DateTimeOffset.UtcNow.AddDays(daysUntilExpiry)
            };

            // perform check
            var isRenewalRequired = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalPeriodDays, renewalIntervalMode);

            // assert result
            if (renewalIntervalMode == RenewalIntervalModes.DaysAfterLastRenewal)
            {
                if (daysSinceRenewed >= renewalPeriodDays)
                {
                    Assert.IsTrue(isRenewalRequired.IsRenewalDue, $"Renewal should be required. Renewal mode: {renewalIntervalMode}, renewal interval: {renewalPeriodDays}, days since last renewed: {daysSinceRenewed}");
                }
                else
                {
                    Assert.IsFalse(isRenewalRequired.IsRenewalDue, $"Renewal should not be required.  Renewal mode: {renewalIntervalMode}, renewal interval: {renewalPeriodDays}, days since last renewed: {daysSinceRenewed}");
                }
            }
            else if (renewalIntervalMode == RenewalIntervalModes.DaysBeforeExpiry)
            {
                if (daysUntilExpiry <= renewalPeriodDays)
                {
                    Assert.IsTrue(isRenewalRequired.IsRenewalDue, $"Renewal should be required. Renewal mode: {renewalIntervalMode}, renewal interval: {renewalPeriodDays}, days until expiry: {daysUntilExpiry}");
                }
                else
                {
                    Assert.IsFalse(isRenewalRequired.IsRenewalDue, $"Renewal should not be required. Renewal mode: {renewalIntervalMode}, renewal interval: {renewalPeriodDays}, days until expiry: {daysUntilExpiry}");
                }
            }
        }

        [TestMethod, Description("Check Percentage Lifetime Elapsed calc, allowing for nulls etc")]
        [DataTestMethod]
        [DataRow(null, null, null)]

        [DataRow(14f, 90f, 15)]
        [DataRow(0.5f, 1f, 50)]
        [DataRow(0f, 1f, 0)]
        [DataRow(0.1f, 0.5f, 20)]
        [DataRow(-0.1f, 0.5f, 0)] // cert start date is in the future, no elapsed lifetime
        [DataRow(365f, 90f, 100)]
        public void TestCheckPercentageLifetimeElapsed(float? daysSinceRenewed, float? lifetimeDays, int? expectedPercentage)
        {
            var managedCertificate = new ManagedCertificate();

            var testDate = DateTimeOffset.UtcNow;

            if (daysSinceRenewed.HasValue && lifetimeDays.HasValue)
            {
                var dateLastRenewed = testDate.AddDays(-daysSinceRenewed.Value);

                managedCertificate = new ManagedCertificate
                {
                    DateRenewed = dateLastRenewed,
                    DateLastRenewalAttempt = dateLastRenewed,
                    DateStart = dateLastRenewed,
                    DateExpiry = dateLastRenewed.AddDays(lifetimeDays.Value)
                };
            }

            var percentageElapsed = managedCertificate.GetPercentageLifetimeElapsed(testDate);

            Assert.AreEqual(expectedPercentage, percentageElapsed);
        }
    }
}
