using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Certify.Models;
using System.Threading.Tasks;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ManagedSiteTests
    {
        [TestMethod, Description("Ensure managed sites list loads")]
        public async Task TestLoadManagedSites()
        {
            var managedSiteSettings = new Management.ItemManager();
            managedSiteSettings.StorageSubfolder = "Tests";

            var managedSites = await managedSiteSettings.GetManagedSites();
            Assert.IsTrue(managedSites.Count > 0);
        }

        [TestMethod, Description("Ensure mamaged site can be created, retrieved and deleted")]
        public async Task TestCreateDeleteManagedSite()
        {
            var itemManager = new Management.ItemManager();
            itemManager.StorageSubfolder = "Tests";

            var testSite = new ManagedSite
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",

                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "testsite.com",
                    ChallengeType = "http-01",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = "c:\\inetpub\\wwwroot"
                },
                ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS
            };

            var managedSite = await itemManager.UpdatedManagedSite(testSite);

            Assert.IsNotNull(managedSite, "Create/store managed site");

            //check site now exists
            managedSite = await itemManager.GetManagedSite(testSite.Id);
            Assert.IsNotNull(managedSite, "Retrieve managed site");

            await itemManager.DeleteManagedSite(managedSite);
            managedSite = await itemManager.GetManagedSite(testSite.Id);

            // now check site has been delete
            Assert.IsNull(managedSite, "Managed site deleted");
        }

        [TestMethod, Description("Ensure a large number of managed sites can be create, saved and loaded")]
        public async Task TestCheckLargeManagedSiteSettingSave()
        {
            var managedSiteSettings = new Management.ItemManager();
            managedSiteSettings.StorageSubfolder = "Tests";

            await managedSiteSettings.LoadAllManagedItems();
            await managedSiteSettings.DeleteAllManagedSites();
            await managedSiteSettings.StoreSettings();

            var numTestManagedSites = 100000;
            var numSANsPerSite = 2;

            for (var i = 0; i < numTestManagedSites; i++)
            {
                var testname = Guid.NewGuid().ToString();
                var site = new ManagedSite
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = testname,
                    GroupId = "test",

                    RequestConfig = new CertRequestConfig
                    {
                        PrimaryDomain = testname + ".com",
                        ChallengeType = "http-01",
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = true,
                        WebsiteRootPath = "c:\\inetpub\\wwwroot"
                    },
                    ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS
                };

                site.DomainOptions.Add(new DomainOption { Domain = testname + ".com", IsPrimaryDomain = true, IsSelected = true });

                for (var d = 0; d < numSANsPerSite; d++)
                {
                    site.DomainOptions.Add(new DomainOption { Domain = d + "." + testname + ".com", IsPrimaryDomain = false, IsSelected = true });
                }

                //add new site
                await managedSiteSettings.UpdatedManagedSite(site);
            }

            // reload settings
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var managedSites = await managedSiteSettings.GetManagedSites(null, reloadAll: true);
            stopwatch.Stop();

            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 20 * numTestManagedSites, "Should load quickly : (ms) " + stopwatch.ElapsedMilliseconds);

            // assert result
            Assert.IsTrue(managedSites.Count == numTestManagedSites, "Should have loaded required number of sites " + managedSites.Count);
        }
    }
}