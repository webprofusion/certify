using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Management.Servers;
using Certify.Models;
using Certify.Models.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

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

        private ILog _log;
        private string _siteId = "";

        public CertRequestTests()
        {
            var log = new LoggerConfiguration()
                     .WriteTo.Debug()
                     .CreateLogger();

            _log = new Loggy(log);
            certifyManager = new CertifyManager();
            iisManager = new ServerProviderIIS();

            // see integrationtestbase for environment variable replacement
            PrimaryTestDomain = ConfigSettings["AWS_TestDomain"];

            testSiteDomain = "integration1." + PrimaryTestDomain;
            testSitePath = PrimaryIISRoot;

            _awsCredStorageKey = ConfigSettings["TestCredentialsKey_Route53"];

            if (ConfigSettings["HttpPort"] != null)
            {
                testSiteHttpPort = int.Parse(ConfigSettings["HttpPort"]);
            }

            //perform setup for IIS
            SetupIIS().Wait();
        }

        /// <summary>
        /// Perform teardown for IIS
        /// </summary>
        public void Dispose()
        {
            TeardownIIS().Wait();
        }

        public async Task SetupIIS()
        {
            if (await iisManager.SiteExists(testSiteName))
            {
                await iisManager.DeleteSite(testSiteName);
            }

            var site = await iisManager.CreateSite(testSiteName, testSiteDomain, PrimaryIISRoot, "DefaultAppPool", port: testSiteHttpPort);
            Assert.IsTrue(await iisManager.SiteExists(testSiteName));
            _siteId = site.Id.ToString();
        }

        public async Task TeardownIIS()
        {
            await iisManager.DeleteSite(testSiteName);
            Assert.IsFalse(await iisManager.SiteExists(testSiteName));
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestHttp01()
        {
            var site = await iisManager.GetIISSiteById(_siteId);
            Assert.AreEqual(site.Name, testSiteName);

            var dummyManagedCertificate = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = testSiteName,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = testSiteDomain,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                        new List<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType="http-01"
                            }
                        }),
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = testSitePath
                },
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
            };

            var result = await certifyManager.PerformCertificateRequest(null, dummyManagedCertificate);

            //ensure cert request was successful
            Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

            //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
            var managedCertificates = await certifyManager.GetManagedCertificates();
            var managedCertificate = managedCertificates.FirstOrDefault(m => m.Id == dummyManagedCertificate.Id);

            //emsure we have a new managed site
            Assert.IsNotNull(managedCertificate);

            //have cert file details
            Assert.IsNotNull(managedCertificate.CertificatePath);

            var fileExists = System.IO.File.Exists(managedCertificate.CertificatePath);
            Assert.IsTrue(fileExists);

            //check cert is correct
            var certInfo = CertificateManager.LoadCertificate(managedCertificate.CertificatePath);
            Assert.IsNotNull(certInfo);

            bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
            Assert.IsTrue(isRecentlyCreated);

            bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
            Assert.IsTrue(expiresInFuture);

            // remove managed site
            await certifyManager.DeleteManagedCertificate(managedCertificate.Id);
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestHttp01IDN()
        {
            var testIDNDomain = "å🤔." + PrimaryTestDomain;

            var testSANList = new string[]
            {
                "xyå."+ PrimaryTestDomain,
                "xyå.xyå" + PrimaryTestDomain
            };

            if (await iisManager.SiteExists(testIDNDomain))
            {
                await iisManager.DeleteSite(testIDNDomain);
            }

            try
            {
                var site = await iisManager.CreateSite(testIDNDomain, testIDNDomain, testSitePath, "DefaultAppPool", port: testSiteHttpPort);

                Assert.AreEqual(site.Name, testIDNDomain);

                var dummyManagedCertificate = new ManagedCertificate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = testIDNDomain,
                    GroupId = site.Id.ToString(),
                    DomainOptions = new ObservableCollection<DomainOption> {
                    new DomainOption{ Domain= testIDNDomain, IsManualEntry=true, IsPrimaryDomain=true, IsSelected=true}
                },
                    RequestConfig = new CertRequestConfig
                    {
                        PrimaryDomain = testIDNDomain,
                        Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                            new List<CertRequestChallengeConfig>
                            {
                            new CertRequestChallengeConfig{
                                ChallengeType="http-01"
                            }
                            }),
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = true,
                        WebsiteRootPath = testSitePath
                    },
                    ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
                };

                var result = await certifyManager.PerformCertificateRequest(_log, dummyManagedCertificate);

                //ensure cert request was successful
                Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

                //have cert file details
                Assert.IsNotNull(dummyManagedCertificate.CertificatePath);

                var fileExists = System.IO.File.Exists(dummyManagedCertificate.CertificatePath);
                Assert.IsTrue(fileExists);

                //check cert is correct
                var certInfo = CertificateManager.LoadCertificate(dummyManagedCertificate.CertificatePath);
                Assert.IsNotNull(certInfo);

                bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
                Assert.IsTrue(isRecentlyCreated);

                bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
                Assert.IsTrue(expiresInFuture);
            }
            finally
            {
                await iisManager.DeleteSite(testIDNDomain);
            }
        }

        [TestMethod, TestCategory("MegaTest"), Ignore]
        public async Task TestChallengeRequestHttp01BazillionDomains()
        {
            // attempt to request a cert for many domains
            var siteName = "TestBazillionDomains";
            int numDomains = 100;

            List<string> domainList = new List<string>();
            for (var i = 0; i < numDomains; i++)
            {
                var testStr = Guid.NewGuid().ToString().Substring(0, 6);
                domainList.Add($"bazillion-1-{i}." + PrimaryTestDomain);
            }

            if (await iisManager.SiteExists(siteName))
            {
                await iisManager.DeleteSite(siteName);
            }

            var site = await iisManager.CreateSite(siteName, domainList[0], testSitePath, "DefaultAppPool", port: testSiteHttpPort);

            try
            {
                // add bindings
                await iisManager.AddSiteBindings(site.Id.ToString(), domainList, testSiteHttpPort);

                var dummyManagedCertificate = new ManagedCertificate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = testSiteName,
                    GroupId = site.Id.ToString(),
                    RequestConfig = new CertRequestConfig
                    {
                        PrimaryDomain = domainList[0],
                        SubjectAlternativeNames = domainList.ToArray(),
                        Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                            new List<CertRequestChallengeConfig>
                            {
                            new CertRequestChallengeConfig{
                                ChallengeType="http-01"
                            }
                            }),
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = false,
                        WebsiteRootPath = testSitePath
                    },
                    ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS,
                };

                //ensure cert request was successful

                var result = await certifyManager.PerformCertificateRequest(_log, dummyManagedCertificate);
                // check details of cert, subject alternative name should include domain and expiry
                // must be greater than 89 days in the future

                Assert.IsTrue(result.IsSuccess, $"Certificate Request Not Completed: {result.Message}");
            }
            finally
            {
                await iisManager.DeleteSite(siteName);
            }
        }

        [TestMethod, TestCategory("MegaTest"), Ignore]
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

            if (await iisManager.SiteExists("TestBazillionDomains"))
            {
                await iisManager.DeleteSite("TestBazillionDomains");
            }

            var site = iisManager.CreateSite("TestBazillionDomains", domainList[0], testSitePath, "DefaultAppPool", port: testSiteHttpPort);

            // add bindings
            await iisManager.AddSiteBindings(site.Id.ToString(), domainList, testSiteHttpPort);

            var dummyManagedCertificate = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = testSiteName,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = domainList[0],
                    SubjectAlternativeNames = domainList.ToArray(),
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                        new List<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType="http-01"
                            }
                        }),
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = false,
                    WebsiteRootPath = testSitePath
                },
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS,
            };

            //ensure cert request was successful
            try
            {
                var result = await certifyManager.PerformCertificateRequest(_log, dummyManagedCertificate);
                // request failed as expected

                Assert.IsFalse(result.IsSuccess, $"Certificate Request Should Not Complete: {result.Message}");
            }
            finally
            {
                await iisManager.DeleteSite("TestBazillionDomains");
            }
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestDNS()
        {
            var site = await iisManager.GetIISSiteById(_siteId);
            Assert.AreEqual(site.Name, testSiteName);

            var dummyManagedCertificate = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = testSiteName,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = testSiteDomain,
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = testSitePath,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig> {
                        new CertRequestChallengeConfig{
                            ChallengeType="dns-01",
                            ChallengeProvider= "DNS01.API.Route53",
                            ChallengeCredentialKey=_awsCredStorageKey,
                            ZoneId =  ConfigSettings["AWS_ZoneId"]
        }
                    },
                    DeploymentSiteOption = DeploymentOption.SingleSite
                },
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
            };

            var result = await certifyManager.PerformCertificateRequest(_log, dummyManagedCertificate);

            //ensure cert request was successful
            Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

            //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
            var managedCertificates = await certifyManager.GetManagedCertificates();
            var managedCertificate = managedCertificates.FirstOrDefault(m => m.Id == dummyManagedCertificate.Id);

            //emsure we have a new managed site
            Assert.IsNotNull(managedCertificate);

            //have cert file details
            Assert.IsNotNull(managedCertificate.CertificatePath);

            var fileExists = System.IO.File.Exists(managedCertificate.CertificatePath);
            Assert.IsTrue(fileExists);

            //check cert is correct
            var certInfo = CertificateManager.LoadCertificate(managedCertificate.CertificatePath);
            Assert.IsNotNull(certInfo);

            bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
            Assert.IsTrue(isRecentlyCreated);

            bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
            Assert.IsTrue(expiresInFuture);

            // remove managed site
            await certifyManager.DeleteManagedCertificate(managedCertificate.Id);

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

            if (await iisManager.SiteExists(testWildcardSiteName))
            {
                await iisManager.DeleteSite(testWildcardSiteName);
            }

            var site = iisManager.CreateSite(testWildcardSiteName, "test" + testStr + "." + PrimaryTestDomain, PrimaryIISRoot, "DefaultAppPool", port: testSiteHttpPort);

            ManagedCertificate managedCertificate = null;
            X509Certificate2 certInfo = null;

            try
            {
                var dummyManagedCertificate = new ManagedCertificate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = testWildcardSiteName,
                    GroupId = site.Id.ToString(),
                    RequestConfig = new CertRequestConfig
                    {
                        PrimaryDomain = wildcardDomain,
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = true,
                        WebsiteRootPath = testSitePath,
                        Challenges = new ObservableCollection<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType= SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                ChallengeProvider = "DNS01.API.Route53",
                                ChallengeCredentialKey = _awsCredStorageKey,
                                ZoneId = ConfigSettings["AWS_ZoneId"]
                            }
                        }
                    },
                    ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
                };

                var result = await certifyManager.PerformCertificateRequest(_log, dummyManagedCertificate);

                //ensure cert request was successful
                Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

                //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
                var managedCertificates = await certifyManager.GetManagedCertificates();
                managedCertificate = managedCertificates.FirstOrDefault(m => m.Id == dummyManagedCertificate.Id);

                //emsure we have a new managed site
                Assert.IsNotNull(managedCertificate);

                //have cert file details
                Assert.IsNotNull(managedCertificate.CertificatePath);

                var fileExists = System.IO.File.Exists(managedCertificate.CertificatePath);
                Assert.IsTrue(fileExists);

                //check cert is correct
                certInfo = CertificateManager.LoadCertificate(managedCertificate.CertificatePath);
                Assert.IsNotNull(certInfo);

                bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
                Assert.IsTrue(isRecentlyCreated);

                bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
                Assert.IsTrue(expiresInFuture);
            }
            finally
            {
                // remove IIS site
                await iisManager.DeleteSite(testWildcardSiteName);

                // remove managed site
                if (managedCertificate != null) await certifyManager.DeleteManagedCertificate(managedCertificate.Id);

                // cleanup certificate
                if (certInfo != null) CertificateManager.RemoveCertificate(certInfo);
            }
        }
    }
}
