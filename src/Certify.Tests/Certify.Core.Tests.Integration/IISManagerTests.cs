using Certify.Management;
using Certify.Management.Servers;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    [TestClass]
    /// <summary>
    /// Integration tests for IIS Manager 
    /// </summary>
    public class IISManagerTests : IntegrationTestBase, IDisposable
    {
        private ServerProviderIIS iisManager;
        private string testSiteName = "Test2CertRequest";
        private string testSiteDomain = "test.com";
        private int testSiteHttpPort = 81;

        private string testSitePath = "c:\\inetpub\\wwwroot";

        public IISManagerTests()
        {
            iisManager = new ServerProviderIIS();

            // see integration test base for env variable
            testSiteDomain = "integration2." + PrimaryTestDomain;

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
            iisManager.CreateSite(testSiteName, testSiteDomain, PrimaryIISRoot, "DefaultAppPool");
            Assert.IsTrue(iisManager.SiteExists(testSiteName));
        }

        public void TeardownIIS()
        {
            iisManager.DeleteSite(testSiteName);
            Assert.IsFalse(iisManager.SiteExists(testSiteName));
        }

        [TestMethod]
        public void TestSiteExists()
        {
            //site exists and matches required domain
            var site = iisManager.GetSiteByDomain(testSiteDomain);
            Assert.AreEqual(site.Name, testSiteName);
        }

        [TestMethod]
        public void TestIISVersionCheck()
        {
            var version = iisManager.GetServerVersion();
            Assert.IsTrue(version.Major >= 7);
        }

        [TestMethod]
        public void TestIISSiteRunning()
        {
            var site = iisManager.GetSiteByDomain(testSiteDomain);

            //this site should be running
            bool isRunning = iisManager.IsSiteRunning(site.Id.ToString());
            Assert.IsTrue(isRunning);

            //this site should not be running
            isRunning = iisManager.IsSiteRunning("MadeUpSiteName");
            Assert.IsFalse(isRunning);
        }

        [TestMethod]
        public void TestGetBinding()
        {
            var b = iisManager.GetSiteBindingByDomain(testSiteDomain);
            Assert.AreEqual(b.Host, testSiteDomain);

            b = iisManager.GetSiteBindingByDomain("randomdomain.com");
            Assert.IsNull(b);
        }

        [TestMethod]
        public void TestCreateUnusalBindings()
        {
            //delete test if it exists
            iisManager.DeleteSite("MSMQTest");

            // create net.msmq://localhost binding, no port or ip
            iisManager.CreateSite("MSMQTest", "localhost", PrimaryIISRoot, null, protocol: "net.msmq", ipAddress: null, port: null);

            var sites = iisManager.GetSiteBindingList(false);
        }

        [TestMethod]
        public void TestCreateFixedIPBindings()
        {
            var testName = testSiteName + "FixedIP";
            var testDomainName = "FixedIPtest.com";
            if (iisManager.SiteExists(testName))
            {
                iisManager.DeleteSite(testName);
            }

            var ipAddress =
            Dns.GetHostEntry(Dns.GetHostName()).AddressList[0].ToString();
            iisManager.CreateSite(testName, testDomainName, PrimaryIISRoot, "DefaultAppPool", "http", ipAddress);

            Assert.IsTrue(iisManager.SiteExists(testSiteName));
            var site = iisManager.GetSiteByDomain(testDomainName);
            Assert.IsTrue(site.Bindings.Any(b => b.Host == testDomainName && b.BindingInformation.Contains(ipAddress)));
        }

        [TestMethod]
        public void TestTooManyBindings()
        {
            //delete test if it exists
            if (iisManager.SiteExists("ManyBindings"))
            {
                iisManager.DeleteSite("ManyBindings");
            }

            // create net.msmq://localhost binding, no port or ip
            iisManager.CreateSite("ManyBindings", "toomany.com", PrimaryIISRoot, null, protocol: "http");
            var site = iisManager.GetSiteBindingByDomain("toomany.com");
            List<string> domains = new List<string>();
            for (var i = 0; i < 10000; i++)
            {
                domains.Add(Guid.NewGuid().ToString() + ".toomany.com");
            }
            iisManager.AddSiteBindings(site.SiteId, domains);
        }

        [TestMethod]
        public void TestLongBinding()
        {
            var testName = testSiteName + "LongBinding";
            var testDomainName = "86098fca1cae7442046562057b1ea940.f3368e3a3240d27430a814c46f7b2c5d.acme.invalid";
            if (iisManager.SiteExists(testName))
            {
                iisManager.DeleteSite(testName);
            }
            iisManager.CreateSite(testName, testDomainName, PrimaryIISRoot, null);
            var site = iisManager.GetSiteByDomain(testDomainName);
            var certStoreName = "MY";
            var cert = CertificateManager.GetCertificatesFromStore().First();
            iisManager.InstallCertificateforBinding(certStoreName, cert.GetCertHash(), site, testDomainName);

            Assert.IsTrue(iisManager.SiteExists(testName));
        }

        [TestMethod]
        public void TestPrimarySites()
        {
            //get all sites
            var sites = iisManager.GetPrimarySites(includeOnlyStartedSites: false);
            Assert.IsTrue(sites.Any());

            //get all sites excluding stopped sites
            sites = iisManager.GetPrimarySites(includeOnlyStartedSites: true);
            Assert.IsTrue(sites.Any());
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestBindingMatch()
        {
            // create test site with mix of hostname and IP only bindings
            var testStr = Guid.NewGuid().ToString().Substring(0, 6);
            PrimaryTestDomain = $"test-{testStr}." + PrimaryTestDomain;

            string testBindingSiteName = "TestAllBinding_" + testStr;

            var testSiteDomain = "test" + testStr + "." + PrimaryTestDomain;

            if (iisManager.SiteExists(testBindingSiteName))
            {
                iisManager.DeleteSite(testBindingSiteName);
            }

            // create site with IP all unassigned, no hostname
            var site = iisManager.CreateSite(testBindingSiteName, "", PrimaryIISRoot, "DefaultAppPool", port: testSiteHttpPort);

            // add another hostname binding (matching cert and not matching cert)
            List<string> testDomains = new List<string> { testSiteDomain, "label1." + testSiteDomain, "nested.label." + testSiteDomain };
            iisManager.AddSiteBindings(site.Id.ToString(), testDomains, testSiteHttpPort);

            // get fresh instance of site since updates
            site = iisManager.GetSiteById(site.Id.ToString());

            Assert.AreEqual(site.Name, testBindingSiteName);
            var dummyCertPath = Environment.CurrentDirectory + "\\Assets\\dummycert.pfx";
            var managedSite = new ManagedSite
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
                    WebsiteRootPath = testSitePath,
                    DeploymentSiteOption = DeploymentOption.SingleSite
                },
                ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS,
                CertificatePath = dummyCertPath
            };

            await iisManager.InstallCertForRequest(managedSite, dummyCertPath, false, false);

            // get cert info to compare hash
            var certInfo = CertificateManager.LoadCertificate(managedSite.CertificatePath);

            // check IIS site bindings
            site = iisManager.GetSiteById(site.Id.ToString());
            var finalBindings = site.Bindings.ToList();

            try
            {
                foreach (var b in finalBindings)
                {
                    if (b.Protocol == "https")
                    {
                        // check this item is one we should have included (is matching domain or has
                        // no hostname)
                        bool shouldBeIncluded = false;

                        if (!String.IsNullOrEmpty(b.Host))
                        {
                            if (testDomains.Contains(b.Host))
                            {
                                shouldBeIncluded = true;
                            }
                        }
                        else
                        {
                            shouldBeIncluded = true;
                        }

                        bool isCertMatch = StructuralComparisons.StructuralEqualityComparer.Equals(b.CertificateHash, certInfo.GetCertHash());

                        if (shouldBeIncluded)
                        {
                            Assert.IsTrue(isCertMatch, "Binding should have been updated with cert hash but was not.");
                        }
                        else
                        {
                            Assert.IsFalse(isCertMatch, "Binding should not have been updated with cert hash but was.");
                        }
                    }
                }
            }
            finally
            {
                // clean up IIS either way
                iisManager.DeleteSite(testBindingSiteName);
            }
        }
    }
}