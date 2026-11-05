using System;
using System.Reflection;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class DiagnosticReportLimitTests
    {
        private bool _statusReportingSetting;

        [TestInitialize]
        public void Setup()
        {
            _statusReportingSetting = CoreAppSettings.Current.EnableStatusReporting;
            CoreAppSettings.Current.EnableStatusReporting = true;
        }

        [TestCleanup]
        public void Cleanup() => CoreAppSettings.Current.EnableStatusReporting = _statusReportingSetting;

        [TestMethod]
        public async Task ReportsStopAtTheLimitForAProblem()
        {
            var client = new Mock<IDashboardClient>();
            client.Setup(c => c.ReportUserActionRequiredAsync(It.IsAny<ItemActionRequired>())).ReturnsAsync(true);
            var manager = CreateManager(client);

            for (var i = 0; i < CertifyManager.MaxDiagnosticReportsPerProblem + 5; i++)
            {
                await manager.ReportDiagnosticFailure(Failure("lowdiskspace"), null);
            }

            client.Verify(c => c.ReportUserActionRequiredAsync(It.IsAny<ItemActionRequired>()), Times.Exactly(10));
        }

        [TestMethod]
        public async Task EachProblemHasItsOwnLimit()
        {
            var client = new Mock<IDashboardClient>();
            client.Setup(c => c.ReportUserActionRequiredAsync(It.IsAny<ItemActionRequired>())).ReturnsAsync(true);
            var manager = CreateManager(client);

            for (var i = 0; i < CertifyManager.MaxDiagnosticReportsPerProblem; i++)
            {
                await manager.ReportDiagnosticFailure(Failure("lowdiskspace"), null);
            }

            await manager.ReportDiagnosticFailure(Failure("lowdiskspace"), null);
            await manager.ReportDiagnosticFailure(Failure("tempfail"), null);

            client.Verify(c => c.ReportUserActionRequiredAsync(It.Is<ItemActionRequired>(r => r.ActionType == "diagnostic:lowdiskspace")), Times.Exactly(10));
            client.Verify(c => c.ReportUserActionRequiredAsync(It.Is<ItemActionRequired>(r => r.ActionType == "diagnostic:tempfail")), Times.Once);
        }

        [TestMethod]
        public async Task FailedSendsDoNotCountTowardsTheLimit()
        {
            var client = new Mock<IDashboardClient>();
            var manager = CreateManager(client);

            client.Setup(c => c.ReportUserActionRequiredAsync(It.IsAny<ItemActionRequired>())).ReturnsAsync(false);
            await manager.ReportDiagnosticFailure(Failure("lowdiskspace"), null);

            client.Setup(c => c.ReportUserActionRequiredAsync(It.IsAny<ItemActionRequired>())).ThrowsAsync(new InvalidOperationException());
            await manager.ReportDiagnosticFailure(Failure("lowdiskspace"), null);

            client.Invocations.Clear();
            client.Setup(c => c.ReportUserActionRequiredAsync(It.IsAny<ItemActionRequired>())).ReturnsAsync(true);

            for (var i = 0; i < CertifyManager.MaxDiagnosticReportsPerProblem + 5; i++)
            {
                await manager.ReportDiagnosticFailure(Failure("lowdiskspace"), null);
            }

            client.Verify(c => c.ReportUserActionRequiredAsync(It.IsAny<ItemActionRequired>()), Times.Exactly(10));
        }

        [TestMethod]
        public async Task NewServiceInstanceReportsAgain()
        {
            var client = new Mock<IDashboardClient>();
            client.Setup(c => c.ReportUserActionRequiredAsync(It.IsAny<ItemActionRequired>())).ReturnsAsync(true);
            var manager = CreateManager(client);

            for (var i = 0; i < CertifyManager.MaxDiagnosticReportsPerProblem; i++)
            {
                await manager.ReportDiagnosticFailure(Failure("lowdiskspace"), null);
            }

            client.Invocations.Clear();

            await CreateManager(client).ReportDiagnosticFailure(Failure("lowdiskspace"), null);

            client.Verify(c => c.ReportUserActionRequiredAsync(It.IsAny<ItemActionRequired>()), Times.Once);
        }

        private static ActionResult Failure(string problem) => new() { Result = problem, IsSuccess = false, Message = "Problem detected" };

        private static CertifyManager CreateManager(Mock<IDashboardClient> client)
        {
            var manager = new CertifyManager();
            typeof(CertifyManager).GetField("_dashboardClient", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, client.Object);
            return manager;
        }
    }
}
