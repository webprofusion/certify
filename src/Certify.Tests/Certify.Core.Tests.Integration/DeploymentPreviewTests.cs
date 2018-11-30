using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
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
    public class DeploymentPreviewTests : IntegrationTestBase, IDisposable
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

        public DeploymentPreviewTests()
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

        [TestMethod]
        public async Task TestPreviewWildcard()
        {
            var testStr = "abc7363";
            var hostname = $"test-{testStr}.test." + PrimaryTestDomain;
            var wildcardDomain = "*.test." + PrimaryTestDomain;
            string testPreviewSiteName = "TestPreview_" + testStr;

            if (await iisManager.SiteExists(testPreviewSiteName))
            {
                await iisManager.DeleteSite(testPreviewSiteName);
            }

            var site = await iisManager.CreateSite(testPreviewSiteName, hostname, PrimaryIISRoot, "DefaultAppPool", port: testSiteHttpPort);

            ManagedCertificate managedCertificate = null;
            X509Certificate2 certInfo = null;

            try
            {
                var dummyManagedCertificate = new ManagedCertificate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = testPreviewSiteName,
                    GroupId = site.Id.ToString(),
                    RequestConfig = new CertRequestConfig
                    {
                        PrimaryDomain = wildcardDomain,
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = true,
                        Challenges = new ObservableCollection<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType= SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                ChallengeProvider = "DNS01.API.Route53",
                                ChallengeCredentialKey = _awsCredStorageKey
                            }
                        }
                    },
                    ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
                };

                var preview = await certifyManager.GeneratePreview(dummyManagedCertificate);
                string previewSummary = GetPreviewSummary(preview);
                System.Diagnostics.Debug.WriteLine(previewSummary);

                var deployStep = preview.Find(a => a.Category == "Deployment");
                Assert.IsTrue(deployStep.Substeps.Count == 1, "Only 1 binding deployment expected");
                Assert.IsTrue(deployStep.Substeps[0].Description == $"Add https binding | {testPreviewSiteName} | ***:443:{hostname} SNI**");
            }
            finally
            {
                // remove IIS site
                await iisManager.DeleteSite(testPreviewSiteName);

                // remove managed site
                if (managedCertificate != null) await certifyManager.DeleteManagedCertificate(managedCertificate.Id);

                // cleanup certificate
                if (certInfo != null) CertificateManager.RemoveCertificate(certInfo);
            }
        }

        private string GetTestStaticIP()
        {
            var ipAddresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList.ToList();
            var ipAddress = ipAddresses.FirstOrDefault(i => i.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToString();
            return ipAddress;
        }

        [TestMethod]
        public async Task TestPreviewStaticIPBindings()
        {
            var testStr = "static1";
            var hostname = $"test-{testStr}.test." + PrimaryTestDomain;
            var wildcardDomain = "*.test." + PrimaryTestDomain;
            string testPreviewSiteName = "StaticTestPreview_" + testStr;

            if (await iisManager.SiteExists(testPreviewSiteName))
            {
                await iisManager.DeleteSite(testPreviewSiteName);
            }

            var ipAddress = GetTestStaticIP();

            var site = await iisManager.CreateSite(testPreviewSiteName, hostname, PrimaryIISRoot, "DefaultAppPool", "http", ipAddress, testSiteHttpPort);

            ManagedCertificate managedCertificate = null;
            X509Certificate2 certInfo = null;

            try
            {
                var testManagedCert = new ManagedCertificate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = testPreviewSiteName,
                    GroupId = site.Id.ToString(),
                    RequestConfig = new CertRequestConfig
                    {
                        PrimaryDomain = wildcardDomain,            
                        PerformAutomatedCertBinding = true,
                        DeploymentSiteOption = DeploymentOption.Auto,
                        Challenges = new ObservableCollection<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType= SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                ChallengeProvider = "DNS01.API.Route53",
                                ChallengeCredentialKey = _awsCredStorageKey
                            }
                        }
                    },
                    ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
                };

                // Deployment Mode = Auto

                var preview = await certifyManager.GeneratePreview(testManagedCert);
                string previewSummary = GetPreviewSummary(preview);
                System.Diagnostics.Debug.WriteLine(previewSummary);

                var deployStep = preview.Find(a => a.Category == "Deployment");
                Assert.IsTrue(deployStep.Substeps.Count == 1, "Only 1 binding deployment expected");
                Assert.IsTrue(deployStep.Substeps[0].Description == $"Add https binding | {testPreviewSiteName} | ***:443:{hostname} SNI**");

                // Deployment Mode = Single Site, Non SNI, Static IP
                testManagedCert.RequestConfig.DeploymentSiteOption = DeploymentOption.SingleSite;
                testManagedCert.RequestConfig.PerformAutomatedCertBinding = false;
                testManagedCert.RequestConfig.BindingIPAddress = ipAddress;

                previewSummary = GetPreviewSummary(preview);
                deployStep = preview.Find(a => a.Category == "Deployment");
                Assert.IsTrue(deployStep.Substeps.Count == 1, "Only 1 binding deployment expected");
                Assert.IsTrue(deployStep.Substeps[0].Description == $"Add https binding | {testPreviewSiteName} | ***:443:{hostname} SNI**");


            }
            finally
            {
                // remove IIS site
                await iisManager.DeleteSite(testPreviewSiteName);

                // remove managed site
                if (managedCertificate != null) await certifyManager.DeleteManagedCertificate(managedCertificate.Id);

                // cleanup certificate
                if (certInfo != null) CertificateManager.RemoveCertificate(certInfo);
            }
        }
        private string GetPreviewSummary(List<ActionStep> steps)
        {
            string output = "";
            foreach (var s in steps)
            {
                output += $"{s.Title} : {s.Description}\r\n";
                if (s.Substeps != null)
                {
                    foreach (var sub in s.Substeps)
                    {
                        output += $"\t{s.Title} : {s.Description}\r\n";
                    }
                }
            }
            return output;
        }
    }
}
