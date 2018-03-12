using Certify.Management;
using Certify.Management.Servers;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    [TestClass]
    /// <summary>
    /// Integration tests for CertifyManager 
    /// </summary>
    public class CertRequestTests : IntegrationTestBase, IDisposable
    {
        private ServerProviderIIS iisManager;
        private CertifyManager certifyManager;
        private string testSiteName = "Test1CertRequest";
        private string testSiteDomain = "";
        private string testSitePath = "c:\\inetpub\\wwwroot";
        private int testSiteHttpPort = 81;
        private string _awsCredStorageKey = "";

        public CertRequestTests()
        {
            certifyManager = new CertifyManager();
            iisManager = new ServerProviderIIS();

            // see integrationtestbase for environment variable replacement
            testSiteDomain = "integration1." + PrimaryTestDomain;
            testSitePath = PrimaryIISRoot;

            _awsCredStorageKey = ConfigurationManager.AppSettings["TestCredentialsKey_Route53"];

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
        public async Task TestChallengeRequestHttp01()
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
            Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

            //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
            var managedSites = await certifyManager.GetManagedSites();
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
            await certifyManager.DeleteManagedSite(managedSite.Id);
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestHttp01IDN()
        {
            var testIDNDomain = "å🤔." + PrimaryTestDomain;

            if (iisManager.SiteExists(testIDNDomain))
            {
                iisManager.DeleteSite(testIDNDomain);
            }

            var site = iisManager.CreateSite(testIDNDomain, testIDNDomain, testSitePath, "DefaultAppPool", port: testSiteHttpPort);

            Assert.AreEqual(site.Name, testIDNDomain);

            var dummyManagedSite = new ManagedSite
            {
                Id = Guid.NewGuid().ToString(),
                Name = testIDNDomain,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = testIDNDomain,
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
            Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

            //have cert file details
            Assert.IsNotNull(dummyManagedSite.CertificatePath);

            var fileExists = System.IO.File.Exists(dummyManagedSite.CertificatePath);
            Assert.IsTrue(fileExists);

            //check cert is correct
            var certInfo = CertificateManager.LoadCertificate(dummyManagedSite.CertificatePath);
            Assert.IsNotNull(certInfo);

            bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
            Assert.IsTrue(isRecentlyCreated);

            bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
            Assert.IsTrue(expiresInFuture);
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestHttp01BazillionDomains()
        {
            // attempt to request a cert for many domains

            int numDomains = 100;

            List<string> domainList = new List<string>();
            for (var i = 0; i < numDomains; i++)
            {
                var testStr = Guid.NewGuid().ToString().Substring(0, 6);
                domainList.Add($"bazillion-1-{i}." + PrimaryTestDomain);
            }

            if (iisManager.SiteExists("TestBazillionDomains"))
            {
                iisManager.DeleteSite("TestBazillionDomains");
            }

            var site = iisManager.CreateSite("TestBazillionDomains", domainList[0], testSitePath, "DefaultAppPool", port: testSiteHttpPort);

            // add bindings
            iisManager.AddSiteBindings(site.Id.ToString(), domainList, testSiteHttpPort);

            var dummyManagedSite = new ManagedSite
            {
                Id = Guid.NewGuid().ToString(),
                Name = testSiteName,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = domainList[0],
                    SubjectAlternativeNames = domainList.ToArray(),
                    ChallengeType = "http-01",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = false,
                    WebsiteRootPath = testSitePath
                },
                ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS,
            };

            //ensure cert request was successful
            try
            {
                var result = await certifyManager.PerformCertificateRequest(dummyManagedSite);
                // check details of cert, subject alternative name should include domain and expiry
                // must be greater than 89 days in the future

                Assert.IsTrue(result.IsSuccess, $"Certificate Request Not Completed: {result.Message}");
            }
            finally
            {
                iisManager.DeleteSite("TestBazillionDomains");
            }
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestHttp01BazillionAndOneDomains()
        {
            // attempt to request a cert for too many domains

            int numDomains = 101;

            List<string> domainList = new List<string>();
            for (var i = 0; i < numDomains; i++)
            {
                var testStr = Guid.NewGuid().ToString().Substring(0, 6);
                domainList.Add($"bazillion-2-{i}." + PrimaryTestDomain);
            }

            if (iisManager.SiteExists("TestBazillionDomains"))
            {
                iisManager.DeleteSite("TestBazillionDomains");
            }

            var site = iisManager.CreateSite("TestBazillionDomains", domainList[0], testSitePath, "DefaultAppPool", port: testSiteHttpPort);

            // add bindings
            iisManager.AddSiteBindings(site.Id.ToString(), domainList, testSiteHttpPort);

            var dummyManagedSite = new ManagedSite
            {
                Id = Guid.NewGuid().ToString(),
                Name = testSiteName,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = domainList[0],
                    SubjectAlternativeNames = domainList.ToArray(),
                    ChallengeType = "http-01",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = false,
                    WebsiteRootPath = testSitePath
                },
                ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS,
            };

            //ensure cert request was successful
            try
            {
                var result = await certifyManager.PerformCertificateRequest(dummyManagedSite);
                // request failed as expected

                Assert.IsFalse(result.IsSuccess, $"Certificate Request Should Not Complete: {result.Message}");
            }
            finally
            {
                iisManager.DeleteSite("TestBazillionDomains");
            }
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestDNS()
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
                    ChallengeType = "dns-01",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = testSitePath,
                    ChallengeProvider = "DNS01.API.Route53",
                    ChallengeCredentialKey = _awsCredStorageKey
                },
                ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS
            };

            var result = await certifyManager.PerformCertificateRequest(dummyManagedSite);

            //ensure cert request was successful
            Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

            //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
            var managedSites = await certifyManager.GetManagedSites();
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
            await certifyManager.DeleteManagedSite(managedSite.Id);

            // cleanup certificate
            CertificateManager.RemoveCertificate(certInfo);
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestDNSWildcard()
        {
            var testStr = Guid.NewGuid().ToString().Substring(0, 6);
            PrimaryTestDomain = $"test-{testStr}." + PrimaryTestDomain;
            var wildcardDomain = "*.test." + PrimaryTestDomain;
            string testWildcardSiteName = "TestWildcard_" + testStr;

            if (iisManager.SiteExists(testWildcardSiteName))
            {
                iisManager.DeleteSite(testWildcardSiteName);
            }

            iisManager.CreateSite(testWildcardSiteName, "test" + testStr + "." + PrimaryTestDomain, PrimaryIISRoot, "DefaultAppPool", port: testSiteHttpPort);
            var site = iisManager.GetSiteByDomain("test" + testStr + "." + PrimaryTestDomain);

            ManagedSite managedSite = null;
            X509Certificate2 certInfo = null;

            try
            {
                var dummyManagedSite = new ManagedSite
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = testWildcardSiteName,
                    GroupId = site.Id.ToString(),
                    RequestConfig = new CertRequestConfig
                    {
                        PrimaryDomain = wildcardDomain,
                        ChallengeType = "dns-01",
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = true,
                        WebsiteRootPath = testSitePath,
                        ChallengeProvider = "DNS01.API.Route53",
                        ChallengeCredentialKey = _awsCredStorageKey
                    },
                    ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS
                };

                var result = await certifyManager.PerformCertificateRequest(dummyManagedSite);

                //ensure cert request was successful
                Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

                //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
                var managedSites = await certifyManager.GetManagedSites();
                managedSite = managedSites.FirstOrDefault(m => m.Id == dummyManagedSite.Id);

                //emsure we have a new managed site
                Assert.IsNotNull(managedSite);

                //have cert file details
                Assert.IsNotNull(managedSite.CertificatePath);

                var fileExists = System.IO.File.Exists(managedSite.CertificatePath);
                Assert.IsTrue(fileExists);

                //check cert is correct
                certInfo = CertificateManager.LoadCertificate(managedSite.CertificatePath);
                Assert.IsNotNull(certInfo);

                bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
                Assert.IsTrue(isRecentlyCreated);

                bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
                Assert.IsTrue(expiresInFuture);
            }
            finally
            {
                // remove managed site
                if (managedSite != null) await certifyManager.DeleteManagedSite(managedSite.Id);

                // remove IIS site
                iisManager.DeleteSite(testWildcardSiteName);

                // cleanup certificate
                if (certInfo != null) CertificateManager.RemoveCertificate(certInfo);
            }
        }
    }
}