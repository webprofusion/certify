using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ManagedCertificateTests
    {
        [TestMethod, Description("Ensure managed sites list loads")]
        public async Task TestLoadManagedCertificates()
        {
            var managedCertificateSettings = new ItemManager();
            managedCertificateSettings.StorageSubfolder = "Tests";

            var managedCertificates = await managedCertificateSettings.GetManagedCertificates();
            Assert.IsTrue(managedCertificates.Count > 0);
        }

        [TestMethod, Description("Ensure mamaged site can be created, retrieved and deleted")]
        public async Task TestCreateDeleteManagedCertificate()
        {
            var itemManager = new ItemManager();
            itemManager.StorageSubfolder = "Tests";

            var testSite = new ManagedCertificate
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
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
            };

            var managedCertificate = await itemManager.UpdatedManagedCertificate(testSite);

            Assert.IsNotNull(managedCertificate, "Create/store managed site");

            //check site now exists
            managedCertificate = await itemManager.GetManagedCertificate(testSite.Id);
            Assert.IsNotNull(managedCertificate, "Retrieve managed site");

            await itemManager.DeleteManagedCertificate(managedCertificate);
            managedCertificate = await itemManager.GetManagedCertificate(testSite.Id);

            // now check site has been delete
            Assert.IsNull(managedCertificate, "Managed site deleted");
        }

        [Ignore, TestMethod, Description("Ensure a large number of managed sites can be create, saved and loaded")]
        public async Task TestCheckLargeManagedCertificateSettingSave()
        {
            var managedCertificateSettings = new ItemManager();
            managedCertificateSettings.StorageSubfolder = "Tests";

            await managedCertificateSettings.LoadAllManagedCertificates();
            await managedCertificateSettings.DeleteAllManagedCertificates();
            await managedCertificateSettings.StoreSettings();

            var numTestManagedCertificates = 100000;
            var numSANsPerSite = 2;

            for (var i = 0; i < numTestManagedCertificates; i++)
            {
                var testname = Guid.NewGuid().ToString();
                var site = new ManagedCertificate
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
                    ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
                };

                site.DomainOptions.Add(new DomainOption { Domain = testname + ".com", IsPrimaryDomain = true, IsSelected = true });

                for (var d = 0; d < numSANsPerSite; d++)
                {
                    site.DomainOptions.Add(new DomainOption { Domain = d + "." + testname + ".com", IsPrimaryDomain = false, IsSelected = true });
                }

                //add new site
                await managedCertificateSettings.UpdatedManagedCertificate(site);
            }

            // reload settings
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var managedCertificates = await managedCertificateSettings.GetManagedCertificates(null, reloadAll: true);
            stopwatch.Stop();

            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 20 * numTestManagedCertificates, "Should load quickly : (ms) " + stopwatch.ElapsedMilliseconds);

            // assert result
            Assert.IsTrue(managedCertificates.Count == numTestManagedCertificates, "Should have loaded required number of sites " + managedCertificates.Count);
        }
    }
}