using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    [TestClass]
    /// <summary>
    /// Integration tests for CertifyManager 
    /// </summary>
    public class CertRequestTests : IntegrationTestBase, IDisposable
    {
        private IISManager iisManager;
        private CertifyManager certifyManager;
        private string testSiteName = "Test1CertRequest";
        private string testSiteDomain = "integration1." + PrimaryTestDomain;
        private string testSitePath = PrimaryIISRoot;
        private int testSiteHttpPort = 81;

        public CertRequestTests()
        {
            certifyManager = new CertifyManager();
            iisManager = new IISManager();
            //perform setup for IIS
            SetupIIS();
        }

        /// <summary>
        /// Perform teardown for IIS 
        /// </summary>
        public void Dispose()
        {
            TeardownIIS();
        }

        public void SetupIIS()
        {
            if (iisManager.SiteExists(testSiteName))
            {
                iisManager.DeleteSite(testSiteName);
            }

            iisManager.CreateSite(testSiteName, testSiteDomain, PrimaryIISRoot, "DefaultAppPool", port: testSiteHttpPort);
            Assert.IsTrue(iisManager.SiteExists(testSiteName));
        }

        public void TeardownIIS()
        {
            iisManager.DeleteSite(testSiteName);
            Assert.IsFalse(iisManager.SiteExists(testSiteName));
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequest()
        {
            var site = iisManager.GetSiteByDomain(testSiteDomain);
            Assert.AreEqual(site.Name, testSiteName);

            var dummyManagedSite = new ManagedSite
            {
                Id = Guid.NewGuid().ToString(),
                Name = testSiteName,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = testSiteDomain,
                    ChallengeType = "http-01",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = testSitePath
                },
                ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS
            };

            var result = await certifyManager.PerformCertificateRequest(dummyManagedSite);

            //ensure cert request was successful
            Assert.IsTrue(result.IsSuccess);

            //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
            var managedSites = certifyManager.GetManagedSites();
            var managedSite = managedSites.FirstOrDefault(m => m.Id == dummyManagedSite.Id);

            //emsure we have a new managed site
            Assert.IsNotNull(managedSite);

            //have cert file details
            Assert.IsNotNull(managedSite.CertificatePath);

            var fileExists = System.IO.File.Exists(managedSite.CertificatePath);
            Assert.IsTrue(fileExists);

            //check cert is correct
            var certInfo = CertificateManager.LoadCertificate(managedSite.CertificatePath);
            Assert.IsNotNull(certInfo);

            bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
            Assert.IsTrue(isRecentlyCreated);

            bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
            Assert.IsTrue(expiresInFuture);

            // remove managed site
            certifyManager.DeleteManagedSite(managedSite.Id);
        }
    }
}