using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how consecutive failures are counted. The count drives the health thresholds and the retry back off,
    /// so a single failed request must advance it by exactly one however many times that request records its outcome
    /// </summary>
    [TestClass]
    public class RenewalFailureCountTests
    {
        private static ManagedCertificate CreateItem(int renewalFailureCount)
        {
            return new ManagedCertificate
            {
                Id = "test-item",
                Name = "Test Item",
                RenewalFailureCount = renewalFailureCount
            };
        }

        [TestMethod, Description("A failure recorded without a preserved count increments the current count")]
        public void FailureWithNoPreservedCountIncrements()
        {
            var item = CreateItem(2);

            CertifyManager.IncrementManagedCertificateRenewalFailureCount(item);

            Assert.AreEqual(3, item.RenewalFailureCount);
        }

        [TestMethod, Description("A failure recorded with a preserved count advances that count by one")]
        public void FailureWithPreservedCountAdvancesIt()
        {
            var item = CreateItem(2);

            CertifyManager.IncrementManagedCertificateRenewalFailureCount(item, failureCount: 2);

            Assert.AreEqual(3, item.RenewalFailureCount);
        }

        [TestMethod, Description("An item failing for the first time reaches a count of one")]
        public void FirstFailureReachesOne()
        {
            var item = CreateItem(0);

            CertifyManager.IncrementManagedCertificateRenewalFailureCount(item, failureCount: 0);

            Assert.AreEqual(1, item.RenewalFailureCount);
        }

        [TestMethod, Description("One request which records its failure twice only advances the count once")]
        public void RepeatedRecordingOfTheSameFailureIsIdempotent()
        {
            // this is the shape of a request whose deployment task fails: the task failure is recorded, then the
            // overall request status is resolved and recorded, both using the count preserved from before the request
            var item = CreateItem(2);
            var currentFailureCount = item.RenewalFailureCount;

            CertifyManager.IncrementManagedCertificateRenewalFailureCount(item, currentFailureCount);
            CertifyManager.IncrementManagedCertificateRenewalFailureCount(item, currentFailureCount);

            Assert.AreEqual(3, item.RenewalFailureCount, "A single failed request must only count as one failure");
        }

        [TestMethod, Description("A count preserved from before the request wins over an interim reset by a successful stage")]
        public void PreservedCountSurvivesAnInterimReset()
        {
            // a deployment which succeeds records success and resets the count to zero, before a deployment task then
            // fails. The back off must continue from where it was rather than restarting
            var item = CreateItem(4);
            var currentFailureCount = item.RenewalFailureCount;

            item.RenewalFailureCount = 0;

            CertifyManager.IncrementManagedCertificateRenewalFailureCount(item, currentFailureCount);

            Assert.AreEqual(5, item.RenewalFailureCount);
        }

        [TestMethod, Description("Repeated failures still reach the failure thresholds at the intended rate")]
        public void ConsecutiveRequestsReachThresholdsAtTheIntendedRate()
        {
            var item = CreateItem(0);

            // five consecutive failed requests, each recording its outcome twice the way a failed deployment task does
            for (var attempt = 0; attempt < LifetimeHealthThresholds.FailureDanger; attempt++)
            {
                var currentFailureCount = item.RenewalFailureCount;

                CertifyManager.IncrementManagedCertificateRenewalFailureCount(item, currentFailureCount);
                CertifyManager.IncrementManagedCertificateRenewalFailureCount(item, currentFailureCount);
            }

            Assert.AreEqual(LifetimeHealthThresholds.FailureDanger, item.RenewalFailureCount, "Five failed requests should count as five failures, not ten");
        }
    }
}
