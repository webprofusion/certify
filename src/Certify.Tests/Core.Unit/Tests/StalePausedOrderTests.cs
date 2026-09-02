using System;
using System.Collections.ObjectModel;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for releasing orders left paused waiting for an external service. A paused item is excluded from every
    /// renewal batch and accrues no failure count, which is right while a person is expected to act, but leaves an
    /// order waiting on a service stuck silently if the call which would resume it never arrives
    /// </summary>
    [TestClass]
    public class StalePausedOrderTests
    {
        private static ManagedCertificate CreatePausedItem(string challengeProvider, double pausedHoursAgo)
        {
            return new ManagedCertificate
            {
                Id = "paused-item",
                Name = "Paused Item",
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ACME,
                LastRenewalStatus = RequestState.Paused,
                DateLastRenewalAttempt = DateTimeOffset.UtcNow.AddHours(-pausedHoursAgo),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "test.example.com",
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>
                    {
                        new CertRequestChallengeConfig { ChallengeType = "dns-01", ChallengeProvider = challengeProvider }
                    }
                }
            };
        }

        [TestMethod, Description("An order paused for an external finalize which never came is released")]
        public void StaleAutomatedPauseIsReleased()
        {
            var item = CreatePausedItem("ManagedAcme", pausedHoursAgo: 25);

            Assert.AreEqual(ManagedCertificateHealth.AwaitingUser, item.Health, "A paused item is excluded from every renewal batch");
            Assert.IsTrue(CertifyManager.IsStalePausedOrder(item));
        }

        [TestMethod, Description("An order recently paused for an external finalize is left alone")]
        public void RecentAutomatedPauseIsNotReleased()
        {
            var item = CreatePausedItem("ManagedAcme", pausedHoursAgo: 1);

            Assert.IsFalse(CertifyManager.IsStalePausedOrder(item), "The service may still be about to finalize it");
        }

        [TestMethod, Description("An order paused waiting for a person is never released automatically")]
        public void ManualDnsPauseIsNeverReleased()
        {
            var item = CreatePausedItem("DNS01.Manual", pausedHoursAgo: 24 * 30);

            Assert.IsFalse(CertifyManager.IsStalePausedOrder(item),
                "Resetting an order a person is part way through would discard the DNS records they have created");
        }

        [TestMethod, Description("An order which has already been finalized is not released")]
        public void PauseWithCustomCsrSuppliedIsNotReleased()
        {
            var item = CreatePausedItem("ManagedAcme", pausedHoursAgo: 25);
            item.RequestConfig.CustomCSR = "-----BEGIN CERTIFICATE REQUEST-----";

            Assert.IsFalse(CertifyManager.IsStalePausedOrder(item), "The CSR arrived, so the order is no longer waiting on the service");
        }

        [TestMethod, Description("An item which is not paused is not affected")]
        public void UnpausedItemIsNotReleased()
        {
            var item = CreatePausedItem("ManagedAcme", pausedHoursAgo: 25);
            item.LastRenewalStatus = RequestState.Success;

            Assert.IsFalse(CertifyManager.IsStalePausedOrder(item));
        }

        [TestMethod, Description("An item paused with no recorded attempt date is left alone")]
        public void PauseWithNoAttemptDateIsNotReleased()
        {
            var item = CreatePausedItem("ManagedAcme", pausedHoursAgo: 25);
            item.DateLastRenewalAttempt = null;

            Assert.IsFalse(CertifyManager.IsStalePausedOrder(item), "There is no way to tell how long it has been waiting");
        }

        [TestMethod, Description("Releasing a stale pause makes the item due for renewal again")]
        public void ReleasedOrderBecomesDueForRenewal()
        {
            var item = CreatePausedItem("ManagedAcme", pausedHoursAgo: 25);

            // this is what ReleaseStalePausedOrders records against the item
            item.LastRenewalStatus = RequestState.Error;
            item.RenewalFailureCount = 1;

            Assert.AreNotEqual(ManagedCertificateHealth.AwaitingUser, item.Health, "The item is no longer excluded from renewal batches");

            var renewalCheck = ManagedCertificate.CalculateNextRenewalAttempt(item, 75, RenewalIntervalModes.PercentageLifetime);

            Assert.IsTrue(renewalCheck.IsRenewalDue, "A never-issued certificate is due as soon as it is no longer paused");
        }
    }
}
