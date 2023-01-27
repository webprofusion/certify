using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation.Language;
using System.Threading.Tasks;
using Certify.Datastore.Postgres;
using Certify.Datastore.SQLServer;
using Certify.Management;
using Certify.Models;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

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
                return new SQLiteItemManager(TEST_PATH, highPerformanceMode: true);
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
            var testCert = BuildTestManagedCertificate();
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

                Debug.WriteLine($"Created {numItems} in {timer.ElapsedMilliseconds}ms avg:{timer.ElapsedMilliseconds / numItems}ms");

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

        [TestMethod, Description("Create many managed items, then test filter behaviour on result sets")]
        public async Task TestManagedCertificateFilters()
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

            var numItems = 100;
            var batchSize = 50;
            // now attempt async creation of bindings
            var taskSet = new Task[batchSize];

            var timer = Stopwatch.StartNew();
            var inMemoryList = new List<ManagedCertificate>();

            // create a large number of managed items, to see if we encounter issues saving/loading from DB async       
            try
            {
                Debug.WriteLine($"Checking no previous test data exists");
                var check = await itemManager.Find(new ManagedCertificateFilter { Keyword = "FilterMultiTest" });
                Assert.IsTrue(check.Count() == 0, "There should be no previous test data present");

                for (var i = 0; i < numItems; i++)
                {
                    var newTestItem = testItem.CopyAsTemplate();
                    newTestItem.Name = "FilterMultiTest_" + i;
                    newTestItem.Id = Guid.NewGuid().ToString();
                    newTestItem.RequestConfig.PrimaryDomain = i + "_" + testItem.RequestConfig.PrimaryDomain;
                    newTestItem.DateExpiry = DateTime.Now.AddDays(new Random().Next(5, 90));
                    newTestItem.DateStart = DateTime.Now.AddDays(-new Random().Next(1, 30));
                    newTestItem.DateLastOcspCheck = DateTime.Now.AddMinutes(-new Random().Next(1, 60));
                    newTestItem.DateLastRenewalInfoCheck = DateTime.Now.AddMinutes(-new Random().Next(1, 30));
                    newTestItem.DateRenewed = DateTime.Now.AddDays(-new Random().Next(1, 30));

                    inMemoryList.Add(newTestItem);
                }

                // create some test data which should not be returned in our test filters
                var numExtraMultiTestData = 50;
                for (var i = 0; i < numExtraMultiTestData; i++)
                {
                    var newTestItem = testItem.CopyAsTemplate();
                    newTestItem.Name = "ExtraMultiTest_" + i;
                    newTestItem.Id = Guid.NewGuid().ToString();
                    newTestItem.RequestConfig.PrimaryDomain = i + "_" + testItem.RequestConfig.PrimaryDomain;
                    newTestItem.DateExpiry = DateTime.Now.AddDays(new Random().Next(5, 90));
                    newTestItem.DateStart = DateTime.Now.AddDays(-new Random().Next(1, 30));
                    newTestItem.DateLastOcspCheck = DateTime.Now.AddMinutes(-new Random().Next(1, 30));
                    newTestItem.DateLastRenewalInfoCheck = DateTime.Now.AddMinutes(-new Random().Next(1, 30));
                    newTestItem.DateRenewed = DateTime.Now.AddDays(-new Random().Next(1, 30));

                    inMemoryList.Add(newTestItem);
                }

                await itemManager.StoreAll(inMemoryList);

                timer.Stop();

                Debug.WriteLine($"Created {numItems} in {timer.ElapsedMilliseconds}ms avg:{timer.ElapsedMilliseconds / numItems}ms");

                // writes take a while to complete and are async check data set
                var stillWaiting = true;
                var waitCount = 0;

                await Task.Delay(1000);

                while (stillWaiting)
                {
                    var result = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"ExtraMultiTest_" });
                    if (result.Count == numExtraMultiTestData)
                    {
                        stillWaiting = false;
                    }
                    else
                    {
                        waitCount++;

                        Assert.IsTrue(waitCount < 10, "Waited too long for test data to commit");

                        Debug.WriteLine($"Wating for test data to be committed.. Got {result.Count} of {numExtraMultiTestData} ::  {waitCount}");
                        await Task.Delay(1000);
                    }
                }

                Debug.WriteLine($"Testing: Retrieve one result");
                var testResult1 = await itemManager.Find(new ManagedCertificateFilter { MaxResults = 1 });
                Assert.IsTrue(testResult1.Count() == 1);

                Debug.WriteLine($"Testing: Retrieve all results, check test data present.");
                var testResultAll = await itemManager.Find(new ManagedCertificateFilter { });
                var checkCount = testResultAll.Count(t => t.Name.IndexOf("FilterMultiTest") >= 0);
                Assert.IsTrue(checkCount == numItems, "Test data set should all be present");

                var testFilter = new List<ManagedCertificateFilter> {
                    new ManagedCertificateFilter { Id= inMemoryList.First().Id , FilterDescription="Test id match"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_" , FilterDescription="Test keyword filter by itself"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_" , Name="FilterMultiTest_1", FilterDescription="Test keyword filter and name"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", LastOCSPCheckMins = 10 , FilterDescription="Test LastOCSPCheckMins"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", LastRenewalInfoCheckMins = 5, FilterDescription="Test LastRenewalInfoCheckMins" },
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", MaxResults =10, FilterDescription="Test Max results" }
                };

                foreach (var filter in testFilter)
                {
                    Debug.WriteLine($"Testing: {filter.FilterDescription}");

                    var testResult = await itemManager.Find(filter);

                    var expectedResult = inMemoryList.Where(i =>
                           (filter.Id == null || i.Id.Equals(filter.Id, StringComparison.InvariantCultureIgnoreCase))
                           && (filter.Keyword == null || i.Name.IndexOf(filter.Keyword, StringComparison.InvariantCultureIgnoreCase) >= 0)
                           && (filter.Name == null || i.Name.Equals(filter.Name, StringComparison.InvariantCultureIgnoreCase))
                           && (filter.LastOCSPCheckMins == null || i.DateLastOcspCheck < DateTime.Now.AddMinutes(-(int)filter.LastOCSPCheckMins))
                           && (filter.LastRenewalInfoCheckMins == null || i.DateLastRenewalInfoCheck < DateTime.Now.AddMinutes(-(int)filter.LastRenewalInfoCheckMins))
                        );

                    if (filter.MaxResults > 0)
                    {
                        expectedResult = expectedResult.Take(filter.MaxResults);
                    }

                    Assert.IsTrue(expectedResult.Count() > 0, $"{filter.FilterDescription} Expected results should have more than zero results");
                    Assert.IsTrue(testResult.Count() > 0, $"{filter.FilterDescription} Test results should have more than zero results");
                    Assert.AreEqual(expectedResult.Count(), testResult.Count, filter.FilterDescription);
                }
            }
            finally
            {
                Debug.WriteLine($"Deleting test data set");

                await itemManager.DeleteByName("FilterMultiTest_");
                await itemManager.DeleteByName("ExtraMultiTest_");

                // allow time for deletes to finish
                await Task.Delay(5000);
            }
        }
    }
}
