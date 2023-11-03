using System;
using System.Threading.Tasks;
using Certify.Config.Migration;
using Certify.Management;
using Certify.Models;
using Certify.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CertifyManagerTests
    {
        private readonly CertifyManager _certifyManager;

        public CertifyManagerTests()
        {
            _certifyManager = new CertifyManager();
            _certifyManager.Init().Wait();
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetACMEProvider()")]
        public async Task TestCertifyManagerGetACMEProvider()
        {
            // Setup test data
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert);

            // Get results from CertifyManager.GetACMEProvider()
            var acmeClientProvider = await _certifyManager.GetACMEProvider(dummyManagedCert, caAccount);

            // Validate return from CertifyManager.GetACMEProvider()
            Assert.IsNotNull(acmeClientProvider, "Expected response from CertifyManager.GetACMEProvider() to not be null");
            Assert.AreEqual("https://acme-staging-v02.api.letsencrypt.org/directory", acmeClientProvider.GetAcmeBaseURI());
            Assert.AreEqual("Anvil", acmeClientProvider.GetProviderName());
        }

        [TestMethod, Description("Test for using CertifyManager.GetACMEProvider() with a null ca account")]
        public async Task TestCertifyManagerGetACMEProviderNullCaAccount()
        {
            // Setup test data
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });

            // Get results from CertifyManager.GetACMEProvider()
            var acmeClientProvider = await _certifyManager.GetACMEProvider(dummyManagedCert, null);

            // Validate return from CertifyManager.GetACMEProvider() with null ca account
            Assert.IsNull(acmeClientProvider, "Expected response from CertifyManager.GetACMEProvider() to be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetACMEProvider() with an invalid ca account")]
        public async Task TestCertifyManagerGetACMEProviderBadCaAccount()
        {
            // Setup test data
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var account = new AccountDetails
            {
                AccountKey = "",
                AccountURI = "",
                Title = "Dev",
                Email = "test@certifytheweb.com",
                CertificateAuthorityId = "badca.com",
                StorageKey = "dev",
                IsStagingAccount = true,
            };

            // Get results from CertifyManager.GetACMEProvider()
            var acmeClientProvider = await _certifyManager.GetACMEProvider(dummyManagedCert, account);

            // Validate return from CertifyManager.GetACMEProvider() with invalid ca account
            Assert.IsNull(acmeClientProvider, "Expected response from CertifyManager.GetACMEProvider() to be null");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.ReportProgress()")]
        public async Task TestCertifyManagerReportProgress()
        {
            // Setup test data
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });

            var progressState = new RequestProgressState(RequestState.Running, "Starting..", dummyManagedCert);
            var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);
            _certifyManager.SetStatusReporting(new StatusHubReporting());

            // Set event handler for when Progress changes
            var progressChanged = false;
            var progressNewState = RequestState.Running;
            progressIndicator.ProgressChanged += (obj, e) =>
            {
                progressChanged = true;
                progressNewState = e.CurrentState;
            };

            // Execute CertifyManager.ReportProgress() with new Warning state
            _certifyManager.ReportProgress(progressIndicator, new RequestProgressState(RequestState.Warning, "Warning message", dummyManagedCert), logThisEvent: true);
            await Task.Delay(100);
            
            // Validate events from CertifyManager.ReportProgress()
            Assert.IsTrue(progressChanged, "Expected progressChanged to be true after CertifyManager.ReportProgress() completed");
            Assert.AreEqual(RequestState.Warning, progressNewState, "Expected progressNewState to be changed to RequestState.Warning");

            // Execute CertifyManager.ReportProgress() with new Error state
            progressChanged = false;
            _certifyManager.ReportProgress(progressIndicator, new RequestProgressState(RequestState.Error, "Error message", dummyManagedCert), logThisEvent: true);
            await Task.Delay(100);

            // Validate events from CertifyManager.ReportProgress()
            Assert.IsTrue(progressChanged, "Expected progressChanged to be true after CertifyManager.ReportProgress() completed");
            Assert.AreEqual(RequestState.Error, progressNewState, "Expected progressNewState to be changed to RequestState.Error");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.PerformRenewalTasks()")]
        public async Task TestCertifyManagerPerformRenewalTasks()
        {
            // Get results from CertifyManager.PerformRenewalTasks()
            var renewalPerformed = await _certifyManager.PerformRenewalTasks();

            // Validate return from CertifyManager.PerformRenewalTasks()
            Assert.IsTrue(renewalPerformed, "Expected response from CertifyManager.PerformRenewalTasks() to be true");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.PerformExport() and CertifyManager.PerformImport()")]
        public async Task TestCertifyManagerPerformExportAndImport()
        {
            // Setup export test data
            var exportReq = new ExportRequest
            {
                Filter = new ManagedCertificateFilter { }, 
                Settings = new ExportSettings { ExportAllStoredCredentials = true, EncryptionSecret = "secret" },
                IsPreviewMode = false,
            };

            // Get results from CertifyManager.PerformExport()
            var performExportRes = await _certifyManager.PerformExport(exportReq);

            // Validate return from CertifyManager.PerformExport()
            Assert.IsNotNull(performExportRes, "Expected response from CertifyManager.PerformExport() to not be null");
            Assert.AreEqual(1, performExportRes.FormatVersion, "Expected FormatVersion of response from CertifyManager.PerformExport() to equal 1 by default");
            Assert.AreEqual("Certify The Web - Exported App Settings", performExportRes.Description, "Unexpected default Description in response from CertifyManager.PerformExport()");
            Assert.AreEqual(0, performExportRes.Errors.Count, "Unexpected Errors in response from CertifyManager.PerformExport()");
            Assert.AreEqual(Certify.Management.Util.GetAppVersion(), performExportRes.SystemVersion?.ToVersion(), "Unexpected SystemVersion in response from CertifyManager.PerformExport()");
            Assert.AreEqual(Environment.MachineName, performExportRes.SourceName, "Unexpected SourceName in response from CertifyManager.PerformExport()");
            Assert.AreEqual(DateTime.Now.Year, performExportRes.ExportDate.Year, "Unexpected ExportDate.Year in response from CertifyManager.PerformExport()");
            Assert.AreEqual(DateTime.Now.Day, performExportRes.ExportDate.Day, "Unexpected ExportDate.Year in response from CertifyManager.PerformExport()");
            Assert.AreEqual(DateTime.Now.Month, performExportRes.ExportDate.Month, "Unexpected ExportDate.Year in response from CertifyManager.PerformExport()");

            // Setup import test data
            var importReq = new ImportRequest
            {
                Package = performExportRes,
                Settings = new ImportSettings { EncryptionSecret = "secret" },
                IsPreviewMode = false,
            };

            // Get results from CertifyManager.PerformImport()
            var performImportRes = await _certifyManager.PerformImport(importReq);

            // Validate return from CertifyManager.PerformImport()
            Assert.IsNotNull(performImportRes, "Expected response from CertifyManager.PerformImport() to not be null");
            Assert.IsTrue(0 < performImportRes.Count, "Expected response from CertifyManager.PerformImport() to not be an empty list");
            foreach (var step in performImportRes)
            {
                Assert.AreEqual("Import", step.Category, $"Unexpected Category value in step '{step.Title}' from response of CertifyManager.PerformImport()");
                Assert.IsTrue(!string.IsNullOrEmpty(step.Title), $"Unexpected Title value in step '{step.Title}' from response of CertifyManager.PerformImport()");
                Assert.IsTrue(!string.IsNullOrEmpty(step.Key), $"Unexpected Key value in step '{step.Title}' from response of CertifyManager.PerformImport()");
                Assert.IsFalse(step.HasError, $"Unexpected HasError value in step '{step.Title}' from response of CertifyManager.PerformImport()");
                Assert.IsFalse(step.HasWarning, $"Unexpected HasWarning value in step '{step.Title}' from response of CertifyManager.PerformImport()");
            }
        }
    }
}
