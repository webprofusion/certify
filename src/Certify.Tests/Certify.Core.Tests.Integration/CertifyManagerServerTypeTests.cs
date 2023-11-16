using System;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Management.Servers;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class CertifyManagerServerTypeTests : IntegrationTestBase, IDisposable
    {
        private readonly CertifyManager _certifyManager;
        private readonly ServerProviderIIS _iisManager;
        private readonly string _testSiteName = "Test1ServerTypes";
        private readonly string _testSiteDomain = "integration1.anothertest.com";
        private readonly string _testSiteIp = "192.168.68.20";
        private readonly int _testSiteHttpPort = 80;
        private string _testSiteId = "";

        public CertifyManagerServerTypeTests()
        {
            // Must set IncludeExternalPlugins to true in C:\ProgramData\certify\appsettings.json and run copy-plugins.bat from certify-internal
            _certifyManager = new CertifyManager();
            _certifyManager.Init().Wait();

            _iisManager = new ServerProviderIIS();
            SetupIIS().Wait();
        }

        public void Dispose() => TeardownIIS().Wait();

        public async Task SetupIIS()
        {
            if (await _iisManager.SiteExists(_testSiteName))
            {
                await _iisManager.DeleteSite(_testSiteName);
            }

            var site = await _iisManager.CreateSite(_testSiteName, _testSiteDomain, _primaryWebRoot, "DefaultAppPool", ipAddress: _testSiteIp, port: _testSiteHttpPort);
            Assert.IsTrue(await _iisManager.SiteExists(_testSiteName));
            _testSiteId = site.Id.ToString();
        }

        public async Task TeardownIIS()
        {
            await _iisManager.DeleteSite(_testSiteName);
            Assert.IsFalse(await _iisManager.SiteExists(_testSiteName));
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetPrimaryWebSites() for IIS")]
        public async Task TestCertifyManagerGetPrimaryWebSitesIIS()
        {
            // Request websites from CertifyManager.GetPrimaryWebSites() for IIS
            var primaryWebsites = await _certifyManager.GetPrimaryWebSites(StandardServerTypes.IIS, true);

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for IIS
            Assert.IsNotNull(primaryWebsites, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to not be null");
            Assert.IsTrue(primaryWebsites.Count > 0, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to not be empty");
            Assert.IsTrue(primaryWebsites.Exists(s => s.IsEnabled), "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to have at least one enabled site");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetPrimaryWebSites() for Apache")]
        [Ignore]
        public async Task TestCertifyManagerGetPrimaryWebSitesApache()
        {
            // TODO: Support for Apache via plugin must be added
            // This test requires at least one website in Apache to be active
            var primaryWebsites = await _certifyManager.GetPrimaryWebSites(StandardServerTypes.Apache, true);

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for Apache
            Assert.IsNotNull(primaryWebsites, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for Apache sites to not be null");
            Assert.IsTrue(primaryWebsites.Count > 0, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for Apache sites to not be empty");
            Assert.IsTrue(primaryWebsites.Exists(s => s.IsEnabled), "Expected website list returned by CertifyManager.GetPrimaryWebSites() for Apache sites to have at least one enabled site");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetPrimaryWebSites() for Nginx")]
        public async Task TestCertifyManagerGetPrimaryWebSitesNginx()
        {
            // This test requires at least one website in Nginx conf to be defined
            var primaryWebsites = await _certifyManager.GetPrimaryWebSites(StandardServerTypes.Nginx, true);

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for Nginx
            Assert.IsNotNull(primaryWebsites, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for Nginx sites to not be null");
            Assert.IsTrue(primaryWebsites.Count > 0, "Expected website list returned by CertifyManager.GetPrimaryWebSites() to not be empty");
            Assert.IsTrue(primaryWebsites.Exists(s => s.IsEnabled), "Expected website list returned by CertifyManager.GetPrimaryWebSites() for Nginx sites to have at least one enabled site");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetPrimaryWebSites() for IIS using an item id")]
        public async Task TestCertifyManagerGetPrimaryWebSitesItemId()
        {
            // Request website info from CertifyManager.GetPrimaryWebSites() for IIS using Item ID
            var itemIdWebsite = await _certifyManager.GetPrimaryWebSites(StandardServerTypes.IIS, true, _testSiteId);

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for IIS using item id
            Assert.IsNotNull(itemIdWebsite, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to not be null");
            Assert.AreEqual(1, itemIdWebsite.Count, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to not be empty");
            Assert.AreEqual(_testSiteId, itemIdWebsite[0].Id, "Expected the same Item Id for SiteInfo objects returned by CertifyManager.GetPrimaryWebSites() for IIS sites");
            Assert.AreEqual(_testSiteName, itemIdWebsite[0].Name, "Expected the same Name for SiteInfo objects returned by CertifyManager.GetPrimaryWebSites() for IIS sites");
        }

        [TestMethod, Description("Test for using CertifyManager.GetPrimaryWebSites() for IIS using a bad item id")]
        public async Task TestCertifyManagerGetPrimaryWebSitesBadItemId()
        {
            // Request website from CertifyManager.GetPrimaryWebSites() using a non-existent Item ID
            var itemIdWebsite = await _certifyManager.GetPrimaryWebSites(StandardServerTypes.IIS, true, "bad_id");

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for IIS using a non-existent Item ID
            Assert.IsNotNull(itemIdWebsite, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to not be null");
            Assert.AreEqual(1, itemIdWebsite.Count, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to not be empty");
            Assert.IsNull(itemIdWebsite[0], "Expected website list object returned by CertifyManager.GetPrimaryWebSites() for IIS with a bad itemId to be null");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetPrimaryWebSites() for IIS including stopped sites")]
        public async Task TestCertifyManagerGetPrimaryWebSitesIncludeStoppedSites()
        {
            // This test requires at least one website in IIS that is stopped
            var primaryWebsites = await _certifyManager.GetPrimaryWebSites(StandardServerTypes.IIS, false);

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for IIS
            Assert.IsNotNull(primaryWebsites, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to not be null");
            Assert.IsTrue(primaryWebsites.Count > 0, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to not be empty");
            Assert.IsTrue(primaryWebsites.Exists(s => s.IsEnabled), "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to have at least one enabled site");
            Assert.IsTrue(primaryWebsites.Exists(s => s.IsEnabled == false), "Expected website list returned by CertifyManager.GetPrimaryWebSites() for IIS sites to have at least one disabled site");
        }

        [TestMethod, Description("Test for using CertifyManager.GetPrimaryWebSites() when server type is not found")]
        public async Task TestCertifyManagerGetPrimaryWebSitesServerTypeNotFound()
        {
            // Request websites from CertifyManager.GetPrimaryWebSites() using StandardServerTypes.Other
            var primaryWebsites = await _certifyManager.GetPrimaryWebSites(StandardServerTypes.Other, true);

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for StandardServerTypes.Other
            Assert.IsNotNull(primaryWebsites, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for StandardServerTypes.Other to not be null");
            Assert.AreEqual(0, primaryWebsites.Count, "Expected website list returned by CertifyManager.GetPrimaryWebSites() for StandardServerTypes.Other to be empty");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetDomainOptionsFromSite() for IIS")]
        public async Task TestCertifyManagerGetDomainOptionsFromSite()
        {
            // Request website Domain Options using Item ID
            var siteDomainOptions = await _certifyManager.GetDomainOptionsFromSite(StandardServerTypes.IIS, _testSiteId);

            // Evaluate return from CertifyManager.GetDomainOptionsFromSite() for IIS
            Assert.IsNotNull(siteDomainOptions, "Expected domain options list returned by CertifyManager.GetDomainOptionsFromSite() for IIS to not be null");
            Assert.AreEqual(1, siteDomainOptions.Count, "Expected domain options list returned by CertifyManager.GetDomainOptionsFromSite() for IIS to not be empty");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetDomainOptionsFromSite() for IIS site with no defined domain")]
        public async Task TestCertifyManagerGetDomainOptionsFromSiteNoDomain()
        {
            // Verify no domain site does not exist from previous test run
            var noDomainSiteName = "NoDomainSite";
            if (await _iisManager.SiteExists(noDomainSiteName))
            {
                await _iisManager.DeleteSite(noDomainSiteName);
            }

            // Add no domain site
            var noDomainSite = await _iisManager.CreateSite(noDomainSiteName, "", _primaryWebRoot, "DefaultAppPool", port: 81);
            Assert.IsTrue(await _iisManager.SiteExists(_testSiteName), "Expected no domain site to be created");
            var noDomainSiteId = noDomainSite.Id.ToString();

            // Request website Domain Options using Item ID
            var siteDomainOptions = await _certifyManager.GetDomainOptionsFromSite(StandardServerTypes.IIS, noDomainSiteId);

            // Evaluate return from CertifyManager.GetDomainOptionsFromSite() for IIS
            Assert.IsNotNull(siteDomainOptions, "Expected domain options list returned by CertifyManager.GetDomainOptionsFromSite() for IIS to not be null");
            Assert.AreEqual(0, siteDomainOptions.Count, "Expected domain options list returned by CertifyManager.GetDomainOptionsFromSite() for IIS to be empty");

            // Remove no domain site
            await _iisManager.DeleteSite(noDomainSiteName);
            Assert.IsFalse(await _iisManager.SiteExists(noDomainSiteName), "Expected no domain site to be deleted");
        }

        [TestMethod, Description("Test for using CertifyManager.GetDomainOptionsFromSite() when server type is not found")]
        public async Task TestCertifyManagerGetDomainOptionsFromSiteServerTypeNotFound()
        {
            // Request website Domain Options for a non-initialized server type (StandardServerTypes.Other)
            var siteDomainOptions = await _certifyManager.GetDomainOptionsFromSite(StandardServerTypes.Other, "1");

            // Evaluate return from CertifyManager.GetDomainOptionsFromSite() for StandardServerTypes.Other
            Assert.IsNotNull(siteDomainOptions, "Expected domain options list returned by CertifyManager.GetDomainOptionsFromSite() for StandardServerTypes.Other to not be null");
            Assert.AreEqual(0, siteDomainOptions.Count, "Expected domain options list returned by CertifyManager.GetDomainOptionsFromSite() for StandardServerTypes.Other to be empty");
        }

        [TestMethod, Description("Test for using CertifyManager.GetDomainOptionsFromSite() for IIS using a bad item id")]
        public async Task TestCertifyManagerGetDomainOptionsFromSiteBadItemId()
        {
            // Request website Domain Options using a non-existent Item ID for IIS
            var siteDomainOptions = await _certifyManager.GetDomainOptionsFromSite(StandardServerTypes.IIS, "bad_id");

            // Evaluate return from CertifyManager.GetDomainOptionsFromSite() using a non-existent Item ID
            Assert.IsNotNull(siteDomainOptions, "Expected domain options list returned by CertifyManager.GetDomainOptionsFromSite() to not be null");
            Assert.AreEqual(0, siteDomainOptions.Count, "Expected domain options list returned by CertifyManager.GetDomainOptionsFromSite() for a non-existent Item ID to be empty");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.IsServerTypeAvailable()")]
        public async Task TestCertifyManagerIsServerTypeAvailable()
        {
            // This test requires at least one website in Nginx conf to be defined
            var isIisAvailable = await _certifyManager.IsServerTypeAvailable(StandardServerTypes.IIS);
            var isNginxAvailable = await _certifyManager.IsServerTypeAvailable(StandardServerTypes.Nginx);
            var isApacheAvailable = await _certifyManager.IsServerTypeAvailable(StandardServerTypes.Apache);
            var isOtherAvailable = await _certifyManager.IsServerTypeAvailable(StandardServerTypes.Other);

            // Evaluate returns from CertifyManager.IsServerTypeAvailable()
            Assert.IsTrue(isIisAvailable, "Expected return from CertifyManager.IsServerTypeAvailable() to be true when at least one IIS site is active");

            Assert.IsTrue(isNginxAvailable, "Expected return from CertifyManager.IsServerTypeAvailable() to be true when at least one Nginx site is active");

            Assert.IsFalse(isApacheAvailable, "Expected return from CertifyManager.IsServerTypeAvailable() to be false when Apache plugin does not exist");
            // TODO: Support for Apache via plugin must be added to enable the next assert
            //Assert.IsTrue(isApacheAvailable, "Expected return from CertifyManager.IsServerTypeAvailable() to be true when at least one Apache site is active");

            Assert.IsFalse(isOtherAvailable, "Expected return from CertifyManager.IsServerTypeAvailable() to be false for StandardServerTypes.Other");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetServerTypeVersion()")]
        public async Task TestCertifyManagerGetServerTypeVersion()
        {
            // This test requires at least one website in Nginx conf to be defined
            var iisServerVersion = await _certifyManager.GetServerTypeVersion(StandardServerTypes.IIS);
            var nginxServerVersion = await _certifyManager.GetServerTypeVersion(StandardServerTypes.Nginx);
            var apacheServerVersion = await _certifyManager.GetServerTypeVersion(StandardServerTypes.Apache);
            var otherServerVersion = await _certifyManager.GetServerTypeVersion(StandardServerTypes.Other);

            var unknownVersion = new Version(0, 0);

            // Evaluate returns from CertifyManager.GetServerTypeVersion()
            Assert.AreNotEqual(unknownVersion, iisServerVersion, "Expected return from CertifyManager.GetServerTypeVersion() to be known when at least one IIS site is active");
            Assert.IsTrue(iisServerVersion.Major > 0);

            Assert.AreNotEqual(unknownVersion, nginxServerVersion, "Expected return from CertifyManager.GetServerTypeVersion() to be known when at least one Nginx site is active");
            Assert.IsTrue(nginxServerVersion.Major > 0);

            Assert.AreEqual(unknownVersion, apacheServerVersion, "Expected return from CertifyManager.GetServerTypeVersion() to be unknown when Apache plugin does not exist");
            // TODO: Support for Apache via plugin must be added to enable the next assert
            //Assert.AreNotEqual(unknownVersion, isApacheAvailable, "Expected return from CertifyManager.GetServerTypeVersion() to be known when at least one Apache site is active");

            Assert.AreEqual(unknownVersion, otherServerVersion, "Expected return from CertifyManager.GetServerTypeVersion() to be unknown for StandardServerTypes.Other");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.RunServerDiagnostics() for IIS")]
        public async Task TestCertifyManagerRunServerDiagnostics()
        {
            // Run diagnostics on the IIS site using Item ID
            var siteDiagnostics = await _certifyManager.RunServerDiagnostics(StandardServerTypes.IIS, _testSiteId);

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for IIS
            Assert.IsNotNull(siteDiagnostics, "Expected diagnostics list returned by CertifyManager.RunServerDiagnostics() for IIS site to not be null");
            Assert.AreEqual(1, siteDiagnostics.Count, "Expected diagnostics list returned by CertifyManager.RunServerDiagnostics() for IIS site to not be empty");
        }

        [TestMethod, Description("Test for using CertifyManager.RunServerDiagnostics() when server type is not found")]
        public async Task TestCertifyManagerRunServerDiagnosticsServerTypeNotFound()
        {
            // Run diagnostics for a non-initialized server type (StandardServerTypes.Other)
            var siteDiagnostics = await _certifyManager.RunServerDiagnostics(StandardServerTypes.Other, _testSiteId);

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for StandardServerTypes.Other
            Assert.IsNotNull(siteDiagnostics, "Expected diagnostics list returned by CertifyManager.RunServerDiagnostics() for StandardServerTypes.Other to not be null");
            Assert.AreEqual(0, siteDiagnostics.Count, "Expected diagnostics list returned by CertifyManager.RunServerDiagnostics() for StandardServerTypes.Other to be empty");
        }

        [TestMethod, Description("Test for using CertifyManager.RunServerDiagnostics() using a bad item id")]
        public async Task TestCertifyManagerRunServerDiagnosticsBadItemId()
        {
            // Run diagnostics on the IIS site using bad Item ID
            var siteDiagnostics = await _certifyManager.RunServerDiagnostics(StandardServerTypes.IIS, "bad_id");

            // Evaluate return from CertifyManager.GetPrimaryWebSites() for IIS with bad Item ID
            Assert.IsNotNull(siteDiagnostics, "Expected diagnostics list returned by CertifyManager.RunServerDiagnostics() for IIS site to not be null");

            // Note: There seems to be no difference at the moment as to whether the Item ID passed in is valid or not,
            // as RunServerDiagnostics() for IIS never uses the passed siteId string (is this intentional?)
            Assert.AreEqual(1, siteDiagnostics.Count, "Expected diagnostics list returned by CertifyManager.RunServerDiagnostics() for IIS site to be empty");
        }
    }
}
