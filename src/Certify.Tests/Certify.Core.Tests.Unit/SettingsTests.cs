using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class SettingsTest
    {
        [TestMethod, Description("Ensure a site which should be renewed correctly requires renewa, where failure has previously occurred")]
        public void TestCheckAutoRenewalPeriodRequiredWithFailures()
        {
            // setup
            var renewalPeriodDays = 14;
            var managedSite = new ManagedSite
            {
                IncludeInAutoRenew = true,
                DateRenewed = DateTime.Now.AddDays(-15),
                DateExpiry = DateTime.Now.AddDays(60),
                DateLastRenewalAttempt = DateTime.Now.AddHours(-1),
                LastRenewalStatus = RequestState.Error,
                RenewalFailureCount = 2
            };

            // perform check
            var isRenewalRequired = Management.CertifyManager.IsRenewalRequired(managedSite, renewalPeriodDays, true);

            // assert result
            Assert.IsTrue(isRenewalRequired, "Renewal should be required");
        }

        [TestMethod, Description("Ensure a site which should be renewed correctly requires renewal")]
        public void TestCheckAutoRenewalPeriodRequired()
        {
            // setup
            var renewalPeriodDays = 14;
            var managedSite = new ManagedSite { IncludeInAutoRenew = true, DateRenewed = DateTime.Now.AddDays(-15), DateExpiry = DateTime.Now.AddDays(60) };

            // perform check
            var isRenewalRequired = Management.CertifyManager.IsRenewalRequired(managedSite, renewalPeriodDays);

            // assert result
            Assert.IsTrue(isRenewalRequired, "Renewal should be required");
        }

        [TestMethod, Description("Ensure a site which should not be renewed correctly does not require renewal")]
        public void TestCheckAutoRenewalPeriodNotRequired()
        {
            // setup : set renewal period to 30 days, last renewal 15 days ago. Renewal should not be
            // required yet.
            var renewalPeriodDays = 30;
            var managedSite = new ManagedSite { IncludeInAutoRenew = true, DateRenewed = DateTime.Now.AddDays(-15), DateExpiry = DateTime.Now.AddDays(60) };

            // perform check
            var isRenewalRequired = Management.CertifyManager.IsRenewalRequired(managedSite, renewalPeriodDays);

            // assert result
            Assert.IsFalse(isRenewalRequired, "Renewal should not be required");
        }

        [TestMethod, Description("Ensure a site with unknown date for last renewal should require renewal")]
        public void TestCheckAutoRenewalPeriodUnknownLastRenewal()
        {
            // setup : set renewal period to 14 days, last renewal unknown.

            var renewalPeriodDays = 14;
            var managedSite = new ManagedSite { IncludeInAutoRenew = true, DateExpiry = DateTime.Now.AddDays(60) };

            // perform check
            var isRenewalRequired = Management.CertifyManager.IsRenewalRequired(managedSite, renewalPeriodDays);

            // assert result
            Assert.IsTrue(isRenewalRequired, "Renewal should be required");
        }
    }
}