using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ManagedSiteTests
    {
        [TestMethod, Description("Ensure a large number of managed sites can be create, saved and loaded")]
        public void TestCheckLargeManagedSiteSettingSave()
        {
            var managedSiteSettings = new Management.ItemManager();
            managedSiteSettings.StorageSubfolder = "Tests";

            managedSiteSettings.LoadSettings();
            managedSiteSettings.DeleteAllManagedSites();
            managedSiteSettings.StoreSettings();

            var numTestManagedSites = 100000;
            var numSANsPerSite = 2;

            for (var i = 0; i < numTestManagedSites; i++)
            {
                var testname = Guid.NewGuid().ToString();
                var site = new Models.ManagedSite
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = testname,
                    GroupId = "test",

                    RequestConfig = new Models.CertRequestConfig
                    {
                        PrimaryDomain = testname + ".com",
                        ChallengeType = "http-01",
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = true,
                        WebsiteRootPath = "c:\\inetpub\\wwwroot"
                    },
                    ItemType = Models.ManagedItemType.SSL_LetsEncrypt_LocalIIS
                };

                site.DomainOptions.Add(new Models.DomainOption { Domain = testname + ".com", IsPrimaryDomain = true, IsSelected = true });

                for (var d = 0; d < numSANsPerSite; d++)
                {
                    site.DomainOptions.Add(new Models.DomainOption { Domain = d + "." + testname + ".com", IsPrimaryDomain = false, IsSelected = true });
                }

                //add new site
                managedSiteSettings.UpdatedManagedSite(site, false, false);
            }

            // save new items
            managedSiteSettings.StoreSettings();

            // reload settings
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            managedSiteSettings.LoadSettings();
            stopwatch.Stop();

            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 20 * numTestManagedSites, "Should load quickly : (ms) " + stopwatch.ElapsedMilliseconds);

            var managedSites = managedSiteSettings.GetManagedSites();

            // assert result
            Assert.IsTrue(managedSites.Count == numTestManagedSites, "Should have loaded required number of sites " + managedSites.Count);
        }
    }
}