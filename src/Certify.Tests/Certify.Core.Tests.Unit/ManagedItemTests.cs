using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Certify.Datastore.Postgres;
using Certify.Datastore.SQLServer;
using Certify.Management;
using Certify.Models;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ManagedItemTests
    {
        private string _storeType = "sqlite";

        private const string TEST_PATH = "Tests";

        public ManagedItemTests()
        {

        }
        private IManagedItemStore GetManagedItemStore()
        {
            if (_storeType == "sqlite")
            {
                return new SQLiteItemManager(TEST_PATH);
            }
            else if (_storeType == "postgres")
            {
                return new PostgresItemManager(Environment.GetEnvironmentVariable("CERTIFY_TEST_POSTGRES"));
            }
            else if (_storeType == "sqlserver")
            {
                return new SQLServerItemManager(Environment.GetEnvironmentVariable("CERTIFY_TEST_SQLSERVER"));
            }
            else
            {
                throw new ArgumentOutOfRangeException("_storeType", "Unsupport store type " + _storeType);
            }
        }

        private static ManagedCertificate BuildTestManagedCertificate()
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
            var managedCertificateSettings = GetManagedItemStore();
            var testCert = ManagedItemTests.BuildTestManagedCertificate();
            try
            {
                var managedCertificate = await managedCertificateSettings.Update(testCert);

                var managedCertificates = await managedCertificateSettings.Find(new ManagedCertificateFilter { MaxResults = 10 });
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
            var itemManager = GetManagedItemStore();

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
        [Ignore]
        public async Task TestCreateDeleteManyManagedCertificates()
        {
            var itemManager = GetManagedItemStore();

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

            var numItems = 100000; // 100,000 items takes about 40 mins to generate for SQLite, 43 secs in Postgres, 66 secs in SQL Server
            var batchSize = 50;
            // now attempt async creation of bindings
            var taskSet = new Task[batchSize];

            var timer = Stopwatch.StartNew();

            // create a large number of managed items, to see if we encounter issues saving/loading from DB async       
            try
            {
                var runParallell = true;
                var numInBatch = 0;
                for (var i = 0; i < numItems; i++)
                {
                    var newTestItem = testItem.CopyAsTemplate();
                    newTestItem.Name = "MultiTest_" + i;
                    newTestItem.Id = Guid.NewGuid().ToString();
                    newTestItem.RequestConfig.PrimaryDomain = i + "_" + testItem.RequestConfig.PrimaryDomain;

                    if (runParallell)
                    {
                        taskSet[numInBatch] = itemManager.Update(newTestItem);

                        numInBatch++;
                        if (numInBatch >= batchSize)
                        {
                            // perform batch and start new batch
                            numInBatch = 0;

                            await Task.WhenAll(taskSet);
                            taskSet = new Task[batchSize];
                        }
                    }
                    else
                    {
                        await itemManager.Update(newTestItem).ConfigureAwait(false);

                    }
                }

                if (numInBatch > 0 && runParallell)
                {
                    // perform last few tasks
                    await Task.WhenAll(taskSet);
                }

                timer.Stop();

                System.Diagnostics.Debug.WriteLine($"Created {numItems} in {timer.ElapsedMilliseconds}ms avg:{timer.ElapsedMilliseconds / numItems}ms");

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
