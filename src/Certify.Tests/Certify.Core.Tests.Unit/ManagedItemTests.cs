using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ManagedItemTests
    {
        private const string TEST_PATH = "Tests";

        public ManagedItemTests()
        {

            var itemManager = new ItemManager(TEST_PATH);

#if DEBUG
            Task.Run(async () =>
            {
                await itemManager.DeleteAll();
            });
#endif
        }

        private ManagedCertificate BuildTestManagedCertificate()
        {
            var testSite = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "testsite.com",
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
                    WebsiteRootPath = "c:\\inetpub\\wwwroot"
                },
                ItemType = ManagedCertificateType.SSL_ACME
            };
            return testSite;
        }

        [TestMethod, Description("Ensure managed sites list loads")]
        public async Task TestLoadManagedCertificates()
        {
            var managedCertificateSettings = new ItemManager(TEST_PATH);
            var testCert = BuildTestManagedCertificate();
            try
            {
                var managedCertificate = await managedCertificateSettings.Update(testCert);

                var managedCertificates = await managedCertificateSettings.GetAll();
                Assert.IsTrue(managedCertificates.Count > 0);
            }
            finally
            {
                await managedCertificateSettings.Delete(testCert);
            }
        }

        [TestMethod, Description("Ensure managed site can be created, retrieved and deleted")]
        public async Task TestCreateDeleteManagedCertificate()
        {
            var itemManager = new ItemManager(TEST_PATH);


            var testSite = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "testsite.com",
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
                    WebsiteRootPath = "c:\\inetpub\\wwwroot"
                },
                ItemType = ManagedCertificateType.SSL_ACME
            };

            var managedCertificate = await itemManager.Update(testSite);

            Assert.IsNotNull(managedCertificate, "Create/store managed site");

            //check site now exists
            managedCertificate = await itemManager.GetById(testSite.Id);
            Assert.IsNotNull(managedCertificate, "Retrieve managed site");

            await itemManager.Delete(managedCertificate);
            managedCertificate = await itemManager.GetById(testSite.Id);

            // now check site has been delete
            Assert.IsNull(managedCertificate, "Managed site deleted");
        }

        [TestMethod, Description("Ensure managed site can be created, retrieved and deleted")]
        public async Task TestCreateManyManagedCertificates()
        {
            var itemManager = new ItemManager(TEST_PATH);

            var testItem = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "testsite.com",
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
                    PerformExtensionlessConfigChecks = true
                },
                ItemType = ManagedCertificateType.SSL_ACME
            };

            // create competing sets of tasks to create managed items

            var numItems = 10000; // 100,000 items takes about 40 mins to generate

            // now attempt async creation of bindings
            var taskSet = new Task[numItems];

            var timer = Stopwatch.StartNew();

            for (var i = 0; i < numItems; i++)
            {
                testItem.Name = "MultiTest_" + i;
                testItem.Id = Guid.NewGuid().ToString();

                taskSet[i] =  itemManager.Update(testItem);
            }

            // create a large number of managed items, to see if we encounter isses saving/loading from DB async       
            try
            {
                await Task.WhenAll(taskSet);

                timer.Stop();

                System.Diagnostics.Debug.WriteLine($"Created {numItems} in { timer.ElapsedMilliseconds}ms avg:{ timer.ElapsedMilliseconds / numItems}ms");

            }
            catch (Exception)
            {
                throw;
            }
            finally
            {

                // now clean up
#if DEBUG
                await itemManager.DeleteByName("MultiTest_");
#endif

            }
        }
    }
}
