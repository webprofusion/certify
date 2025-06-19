using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Models.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CertifyManagerMaintenanceTests
    {
        [DataTestMethod]
        [DataRow(90, null, 1, 10, DisplayName = "No scheduled renewal, ARI window in future")]
        [DataRow(90, 60, 1, 10, DisplayName = "Scheduled renewal in future, ARI window in future")]
        [DataRow(10, null, -5, 5, DisplayName = "No scheduled renewal, ARI window started")]
        [DataRow(10, 2, -5, 5, DisplayName = "Scheduled renewal soon, ARI window started")]
        [DataRow(1, null, -10, -1, DisplayName = "No scheduled renewal, ARI window in past")]
        [DataRow(1, 0, -10, -1, DisplayName = "Scheduled renewal now, ARI window in past")]
        [DataRow(10, 2, -5, 5, true, DisplayName = "Revoked, Scheduled renewal soon, ARI window started")]
        public async Task PerformRenewalInfoCheck_VariousInputs(
            int expiryDays,
            int? scheduledRenewalDays,
            int renewalWindowStartOffset,
            int renewalWindowEndOffset, bool revoked = false, bool alreadyScheduled = false)
        {
            // Arrange
            var renewalWindow = new RenewalWindow
            {
                Start = DateTimeOffset.UtcNow.AddDays(renewalWindowStartOffset),
                End = DateTimeOffset.UtcNow.AddDays(renewalWindowEndOffset)
            };

            var managerMock = GetMockCertifyManager(renewalWindow);

            var completedRenewalInfoChecks = new List<string>();
            var itemsWhichRequireRenewal = new List<string>();
            var itemsViaARI = new Dictionary<string, DateTimeOffset>();
            var directoryInfoCache = new Dictionary<string, AcmeDirectoryInfo>();
            var managedCert = new ManagedCertificate
            {
                Id = "test-cert",
                Name = "Test Cert",
                CertificateThumbprintHash = "abc123",
                DateExpiry = DateTimeOffset.UtcNow.AddDays(expiryDays),
                CertificatePath = "dummy.pfx"
            };

            if (revoked)
            {
                managedCert.CertificateRevoked = true;
            }

            if (scheduledRenewalDays.HasValue)
            {
                managedCert.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow.AddDays(scheduledRenewalDays.Value);
            }

            var log = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<CertifyManagerMaintenanceTests>());

            // Act
            var newAriRenewalScheduled = await managerMock.Object.PerformRenewalInfoCheck(
                log,
                completedRenewalInfoChecks,
                itemsWhichRequireRenewal,
                itemsViaARI,
                directoryInfoCache,
                managedCert
            );

            // Assert
            Assert.IsTrue(completedRenewalInfoChecks.Contains("test-cert"), "Certificate ID should be added to completedRenewalInfoChecks");

            var nextRenewal = ManagedCertificate.CalculateNextRenewalAttempt(managedCert, CoreAppSettings.Current.RenewalIntervalDays, CoreAppSettings.Current.RenewalIntervalMode ?? RenewalIntervalModes.DaysAfterLastRenewal);

            if (newAriRenewalScheduled)
            {
                Assert.IsTrue(itemsViaARI.ContainsKey("test-cert"), "Certificate ID should be added to itemsViaARI");

                if (nextRenewal.DateNextRenewalAttempt < DateTimeOffset.UtcNow)
                {
                    Assert.IsTrue(itemsWhichRequireRenewal.Contains("test-cert"), "Certificate ID should be added to itemsWhichRequireRenewal");
                }
            }
            else
            {
                Assert.IsFalse(itemsViaARI.ContainsKey("test-cert"), "Certificate ID should not be added to itemsViaARI if no renewal is scheduled");
            }
        }

        private Mock<CertifyManager> GetMockCertifyManager(RenewalWindow mockRenewalWindow)
        {
            var mockAcmeProvider = new Mock<IACMEClientProvider>();

            mockAcmeProvider.Setup(p => p.GetRenewalInfo(It.IsAny<string>()))
                .ReturnsAsync(new RenewalInfo
                {
                    SuggestedWindow = mockRenewalWindow,
                    ExplanationURL = new Uri("https://example.com/renewalinfo")
                });
            mockAcmeProvider.Setup(p => p.GetAcmeDirectory()).ReturnsAsync(new AcmeDirectoryInfo { RenewalInfo = new Uri("https://example-acme.com/renewalinfo") });
            mockAcmeProvider.Setup(p => p.GetAcmeBaseURI()).Returns("https://example-acme.com/");

            var managerMock = new Mock<CertifyManager>();

            managerMock.Setup(m => m.GetAccountDetails(It.IsAny<ManagedCertificate>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new AccountDetails());

            managerMock.Setup(m => m.GetACMEProvider(It.IsAny<ManagedCertificate>(), It.IsAny<AccountDetails>())).ReturnsAsync(mockAcmeProvider.Object);

            managerMock.Setup(m => m.ComputeARICertificateId(It.IsAny<ManagedCertificate>())).ReturnsAsync("ARICertId12345");

            return managerMock;
        }
    }
}
