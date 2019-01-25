using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ManagedItemTests
    {
        private ManagedCertificate BuildTestManagedCertificate()
        {
            var testSite = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",

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
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
            };
            return testSite;
        }

        [TestMethod, Description("Ensure managed sites list loads")]
        public async Task TestLoadManagedCertificates()
        {
            var managedCertificateSettings = new ItemManager("Tests");
            var testCert = BuildTestManagedCertificate();
            try
            {
                var managedCertificate = await managedCertificateSettings.UpdatedManagedCertificate(testCert);

                var managedCertificates = await managedCertificateSettings.GetManagedCertificates();
                Assert.IsTrue(managedCertificates.Count > 0);
            }
            finally
            {
                await managedCertificateSettings.DeleteManagedCertificate(testCert);
            }
        }

        [TestMethod, Description("Ensure managed site can be created, retrieved and deleted")]
        public async Task TestCreateDeleteManagedCertificate()
        {
            var itemManager = new ItemManager("Tests");


            var testSite = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",

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

        [TestMethod, Description("Ensure managed site can be created, retrieved and deleted")]
        public async Task TestCreateManyManagedCertificates()
        {
            var itemManager = new ItemManager("Tests");

            var testItem = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",

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
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
            };

            // create competing sets of tasks to create managed items

            var numItems = 1000;

            // now attempt async creation of bindings
            var taskSet = new Task[numItems];

            var timer = Stopwatch.StartNew();

            for (var i = 0; i < numItems; i++)
            {
                taskSet[i] = new Task(() =>
               {
                   testItem.Name = "MultiTest_" + i;
                   testItem.Id = Guid.NewGuid().ToString();
                   var result = itemManager.UpdatedManagedCertificate(testItem).Result;

               });

                taskSet[i].Start();
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
                var allManagedItems = await itemManager.GetManagedCertificates();
                foreach (var item in allManagedItems)
                {
                    if (item.Name.StartsWith("MultiTest_"))
                    {
                        await itemManager.DeleteManagedCertificate(item);
                    }
                }
            }
        }
    }
}
