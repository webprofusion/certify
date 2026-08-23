using System;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class ManagedCertificateHealthTests
    {
        /// <summary>
        /// Create a managed certificate with a lifetime positioned relative to now so the elapsed
        /// lifetime percentage is deterministic (based on a 100 day lifetime).
        /// </summary>
        private static ManagedCertificate CreateTestCert(RequestState? lastRenewalStatus, int renewalFailureCount = 0, double daysElapsed = 10, double lifetimeDays = 100, bool revoked = false)
        {
            var now = DateTimeOffset.UtcNow;

            return new ManagedCertificate
            {
                Id = "health-test",
                Name = "HealthTest",
                LastRenewalStatus = lastRenewalStatus,
                RenewalFailureCount = renewalFailureCount,
                CertificateRevoked = revoked,
                DateStart = now.AddDays(-daysElapsed),
                DateRenewed = now.AddDays(-daysElapsed),
                DateExpiry = now.AddDays(lifetimeDays - daysElapsed)
            };
        }

        [TestMethod, Description("Item with no last renewal status has Unknown health")]
        public void TestHealthUnknownWhenNoStatus()
        {
            var cert = CreateTestCert(lastRenewalStatus: null);

            Assert.AreEqual(ManagedCertificateHealth.Unknown, cert.Health);
        }

        [TestMethod, Description("Error status with few failures and plenty of lifetime remaining is Warning health")]
        public void TestHealthErrorStatusWithFewFailuresIsWarning()
        {
            var cert = CreateTestCert(RequestState.Error, renewalFailureCount: 1, daysElapsed: 10);

            Assert.AreEqual(ManagedCertificateHealth.Warning, cert.Health);
        }

        [TestMethod, Description("Error status at the failure danger threshold boundary remains Warning health")]
        public void TestHealthErrorStatusAtFailureDangerBoundaryIsWarning()
        {
            var cert = CreateTestCert(RequestState.Error, renewalFailureCount: LifetimeHealthThresholds.FailureDanger, daysElapsed: 10);

            Assert.AreEqual(ManagedCertificateHealth.Warning, cert.Health);
        }

        [TestMethod, Description("Error status with failures above the danger threshold is Error health")]
        public void TestHealthErrorStatusWithManyFailuresIsError()
        {
            var cert = CreateTestCert(RequestState.Error, renewalFailureCount: LifetimeHealthThresholds.FailureDanger + 1, daysElapsed: 10);

            Assert.AreEqual(ManagedCertificateHealth.Error, cert.Health);
        }

        [TestMethod, Description("Error status with lifetime nearly elapsed is Error health even with few failures")]
        public void TestHealthErrorStatusNearExpiryIsError()
        {
            var cert = CreateTestCert(RequestState.Error, renewalFailureCount: 1, daysElapsed: 97);

            Assert.AreEqual(ManagedCertificateHealth.Error, cert.Health);
        }

        [TestMethod, Description("Error status with no known lifetime is Warning health")]
        public void TestHealthErrorStatusWithUnknownLifetimeIsWarning()
        {
            var cert = CreateTestCert(RequestState.Error, renewalFailureCount: 1);
            cert.DateExpiry = null;

            Assert.AreEqual(ManagedCertificateHealth.Warning, cert.Health);
        }

        [TestMethod, Description("Paused status is AwaitingUser health")]
        public void TestHealthPausedStatusIsAwaitingUser()
        {
            var cert = CreateTestCert(RequestState.Paused, daysElapsed: 10);

            Assert.AreEqual(ManagedCertificateHealth.AwaitingUser, cert.Health);
        }

        [TestMethod, Description("Successful item with revoked certificate is Error health")]
        public void TestHealthSuccessStatusRevokedIsError()
        {
            var cert = CreateTestCert(RequestState.Success, daysElapsed: 10, revoked: true);

            Assert.AreEqual(ManagedCertificateHealth.Error, cert.Health);
        }

        [TestMethod, Description("Successful item with plenty of lifetime remaining is OK health")]
        public void TestHealthSuccessStatusIsOK()
        {
            var cert = CreateTestCert(RequestState.Success, daysElapsed: 10);

            Assert.AreEqual(ManagedCertificateHealth.OK, cert.Health);
        }

        [TestMethod, Description("Successful item past the lifetime warning threshold is Warning health")]
        public void TestHealthSuccessStatusPastWarningThresholdIsWarning()
        {
            var cert = CreateTestCert(RequestState.Success, daysElapsed: 80);

            Assert.AreEqual(ManagedCertificateHealth.Warning, cert.Health);
        }

        [TestMethod, Description("Successful item past the lifetime danger threshold is Error health")]
        public void TestHealthSuccessStatusPastDangerThresholdIsError()
        {
            var cert = CreateTestCert(RequestState.Success, daysElapsed: 97);

            Assert.AreEqual(ManagedCertificateHealth.Error, cert.Health);
        }

        [TestMethod, Description("Warning status with plenty of lifetime remaining is Warning health")]
        public void TestHealthWarningStatusIsWarning()
        {
            var cert = CreateTestCert(RequestState.Warning, daysElapsed: 10);

            Assert.AreEqual(ManagedCertificateHealth.Warning, cert.Health);
        }

        [TestMethod, Description("Warning status past the lifetime danger threshold is Error health")]
        public void TestHealthWarningStatusPastDangerThresholdIsError()
        {
            var cert = CreateTestCert(RequestState.Warning, daysElapsed: 97);

            Assert.AreEqual(ManagedCertificateHealth.Error, cert.Health);
        }
    }
}
