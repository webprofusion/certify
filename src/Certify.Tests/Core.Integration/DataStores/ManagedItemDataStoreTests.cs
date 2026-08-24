using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Certify.Datastore.Postgres;
using Certify.Datastore.SQLite;
using Certify.Datastore.SQLServer;
using Certify.Models;
using Certify.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Npgsql;

namespace Certify.Core.Tests.DataStores
{
    [TestClass]
    public class ManagedItemDataStoreTests
    {
        private string _storeType = "postgres";

        private const string TEST_PATH = "Tests";

        private const string PostgresLegacySchemaSql = "DROP TABLE IF EXISTS manageditem; CREATE TABLE manageditem (id TEXT NOT NULL PRIMARY KEY, json JSONB NOT NULL);";
        private const string SqlServerSchemaSql = @"IF OBJECT_ID('manageditem', 'U') IS NULL
BEGIN
    CREATE TABLE manageditem (
        id NVARCHAR(64) NOT NULL PRIMARY KEY,
        itemtype NVARCHAR(100) NOT NULL,
        config NVARCHAR(MAX) NOT NULL,
        itemvalue NVARCHAR(MAX) NULL
    );
END";
        private const string SqlServerLegacySchemaSql = @"IF OBJECT_ID('manageditem', 'U') IS NOT NULL
BEGIN
    DROP TABLE manageditem;
END
CREATE TABLE manageditem (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    json NVARCHAR(MAX) NOT NULL
);";

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext context)
        {
            await DataStoreTestContainers.InitializeAsync();
        }

        [TestMethod, Description("Ensure multi-tenant isolation for managed items")]
        [DynamicData(nameof(ExternalTestDataStores))]
        public async Task TestManagedItemMultiTenancy(string storeType = null)
        {
            var tenantA = $"tenantA_{Guid.NewGuid():N}";
            var tenantB = $"tenantB_{Guid.NewGuid():N}";

            var storeA = GetManagedItemStore(storeType ?? _storeType, tenantA);
            var storeB = GetManagedItemStore(storeType ?? _storeType, tenantB);

            var itemA = BuildTestManagedCertificate();
            itemA.Name = $"TenantA_{Guid.NewGuid():N}";

            var itemB = BuildTestManagedCertificate();
            itemB.Name = $"TenantB_{Guid.NewGuid():N}";

            try
            {
                await storeA.Update(itemA);
                await storeB.Update(itemB);

                var listA = await storeA.Find(new ManagedCertificateFilter { Keyword = "Tenant" });
                var listB = await storeB.Find(new ManagedCertificateFilter { Keyword = "Tenant" });

                Assert.IsTrue(listA.Any(i => i.Id == itemA.Id), $"[{storeType}] Tenant A should see its own item");
                Assert.IsFalse(listA.Any(i => i.Id == itemB.Id), $"[{storeType}] Tenant A should not see tenant B item");

                Assert.IsTrue(listB.Any(i => i.Id == itemB.Id), $"[{storeType}] Tenant B should see its own item");
                Assert.IsFalse(listB.Any(i => i.Id == itemA.Id), $"[{storeType}] Tenant B should not see tenant A item");
            }
            finally
            {
                await storeA.Delete(itemA);
                await storeB.Delete(itemB);
            }
        }

        [ClassCleanup]
        public static async Task ClassCleanup()
        {
            await DataStoreTestContainers.DisposeAsync();
        }

        [TestMethod, Description("Ensure the same item id can be stored by more than one instance")]
        [DynamicData(nameof(ExternalTestDataStores))]
        public async Task TestManagedItemSharedIdAcrossInstances(string storeType)
        {
            // the logical key of a stored row is (id, itemtype, instanceid). Schemas which declared the primary
            // key on id alone failed here with "Violation of PRIMARY KEY constraint .. Cannot insert duplicate key",
            // which also broke data store migration into a database already holding the same item for another instance.
            var sharedId = Guid.NewGuid().ToString();

            var storeA = GetManagedItemStore(storeType, $"instanceA_{Guid.NewGuid():N}");
            var storeB = GetManagedItemStore(storeType, $"instanceB_{Guid.NewGuid():N}");

            var itemA = BuildTestManagedCertificate();
            itemA.Id = sharedId;
            itemA.Name = $"InstanceA_{Guid.NewGuid():N}";

            var itemB = BuildTestManagedCertificate();
            itemB.Id = sharedId;
            itemB.Name = $"InstanceB_{Guid.NewGuid():N}";

            try
            {
                await storeA.Update(itemA);
                await storeB.Update(itemB);

                var storedA = await storeA.GetById(sharedId);
                var storedB = await storeB.GetById(sharedId);

                Assert.IsNotNull(storedA, $"[{storeType}] Instance A should have its own copy of the item");
                Assert.IsNotNull(storedB, $"[{storeType}] Instance B should have its own copy of the item");
                Assert.AreEqual(itemA.Name, storedA.Name, $"[{storeType}] Instance A should read back its own version");
                Assert.AreEqual(itemB.Name, storedB.Name, $"[{storeType}] Instance B should read back its own version");
            }
            finally
            {
                await storeA.Delete(itemA);
                await storeB.Delete(itemB);
            }
        }

        public static IEnumerable<object[]> LegacyTestDataStores
        {
            get
            {
                return new[]
                {
                    new object[] { "postgres" },
                    new object[] { "sqlserver" }
                };
            }
        }

        public static IEnumerable<object[]> TestDataStores
        {
            get
            {
                return new[]
                {
                    new object[] { "postgres" },
                    new object[] { "sqlite" },
                    new object[] { "sqlserver" }
                };
            }
        }

        public static IEnumerable<object[]> ExternalTestDataStores
        {
            get
            {
                return new[]
                {
                    new object[] { "postgres" },
                    new object[] { "sqlserver" }
                };
            }
        }

        public ManagedItemDataStoreTests()
        {

        }
        private IManagedItemStore GetManagedItemStore(string storeType = null)
        {
            if (storeType == null)
            {
                storeType = _storeType;
            }

            if (storeType == "sqlite")
            {
                return new SQLiteManagedItemStore(TEST_PATH);
            }
            else if (storeType == "postgres")
            {
                return new PostgresManagedItemStore(DataStoreTestContainers.PostgresConnectionString);
            }
            else if (storeType == "sqlserver")
            {
                return new SQLServerManagedItemStore(DataStoreTestContainers.SqlServerConnectionString);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(storeType), "Unsupported store type " + storeType);
            }
        }

        private IManagedItemStore GetManagedItemStore(string storeType, string instanceId)
        {
            if (storeType == "sqlite")
            {
                return new SQLiteManagedItemStore(TEST_PATH);
            }
            else if (storeType == "postgres")
            {
                return new PostgresManagedItemStore(DataStoreTestContainers.PostgresConnectionString, instanceId: instanceId);
            }
            else if (storeType == "sqlserver")
            {
                return new SQLServerManagedItemStore(DataStoreTestContainers.SqlServerConnectionString, instanceId: instanceId);
            }

            throw new ArgumentOutOfRangeException(nameof(storeType), "Unsupported store type " + storeType);
        }

        private static async Task InitializeLegacyPostgresSchema(string connectionString)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(PostgresLegacySchemaSql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InitializeLegacySqlServerSchema(string connectionString)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(SqlServerLegacySchemaSql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertLegacyPostgresManagedItem(string connectionString, ManagedCertificate cert)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("INSERT INTO manageditem(id, json) VALUES(@id, CAST(@config as jsonb));", conn);
            cmd.Parameters.Add(new NpgsqlParameter("@id", cert.Id));
            cmd.Parameters.Add(new NpgsqlParameter("@config", JsonConvert.SerializeObject(cert)));
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertLegacySqlServerManagedItem(string connectionString, ManagedCertificate cert)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("INSERT INTO manageditem(id, json) VALUES(@id, @config);", conn);
            cmd.Parameters.Add(new SqlParameter("@id", cert.Id));
            cmd.Parameters.Add(new SqlParameter("@config", JsonConvert.SerializeObject(cert)));
            await cmd.ExecuteNonQueryAsync();
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
        [DynamicData(nameof(TestDataStores))]
        public async Task TestLoadManagedCertificates(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            var testCert = BuildTestManagedCertificate();
            try
            {
                var managedCertificate = await itemManager.Update(testCert);
                var filter = new ManagedCertificateFilter { MaxResults = 10 };
                var managedCertificates = await itemManager.Find(filter);

                Assert.IsNotEmpty(managedCertificates);

                var total = await itemManager.CountAll(filter);
                Assert.IsGreaterThan(0, total);
            }
            finally
            {
                await itemManager.Delete(testCert);
            }
        }

        [TestMethod, Description("Test migration from legacy schema to new schema")]
        [DynamicData(nameof(LegacyTestDataStores))]
        public async Task TestLegacySchemaMigration(string storeType = null)
        {
            var testCert = BuildTestManagedCertificate();
            testCert.Name = "LegacySchemaTest_" + Guid.NewGuid().ToString();

            if (storeType == "postgres")
            {
                await InitializeLegacyPostgresSchema(DataStoreTestContainers.PostgresConnectionString);
                await InsertLegacyPostgresManagedItem(DataStoreTestContainers.PostgresConnectionString, testCert);
            }
            else if (storeType == "sqlserver")
            {
                await InitializeLegacySqlServerSchema(DataStoreTestContainers.SqlServerConnectionString);
                await InsertLegacySqlServerManagedItem(DataStoreTestContainers.SqlServerConnectionString, testCert);
            }
            else
            {
                Assert.Fail("Legacy schema migration test only applies to postgres and sqlserver.");
            }

            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            try
            {
                var retrieved = await itemManager.GetById(testCert.Id);
                Assert.IsNotNull(retrieved, "Should retrieve managed certificate after migration");
                Assert.AreEqual(testCert.Name, retrieved.Name, "Retrieved certificate should have correct name after migration");
                Assert.IsTrue(await itemManager.IsInitialised(), "Store should be initialised after migration");

                if (storeType == "postgres")
                {
                    await using var conn = new NpgsqlConnection(DataStoreTestContainers.PostgresConnectionString);
                    await conn.OpenAsync();
                    await using var cmd = new NpgsqlCommand("SELECT itemtype FROM manageditem WHERE id=@id", conn);
                    cmd.Parameters.Add(new NpgsqlParameter("@id", testCert.Id));
                    var itemType = (string)await cmd.ExecuteScalarAsync();
                    Assert.AreEqual("managedcertificate", itemType, "Item type should be set after migration");
                }
                else if (storeType == "sqlserver")
                {
                    await using var conn = new SqlConnection(DataStoreTestContainers.SqlServerConnectionString);
                    await conn.OpenAsync();
                    await using var cmd = new SqlCommand("SELECT itemtype FROM manageditem WHERE id=@id", conn);
                    cmd.Parameters.Add(new SqlParameter("@id", testCert.Id));
                    var itemType = (string)await cmd.ExecuteScalarAsync();
                    Assert.AreEqual("managedcertificate", itemType, "Item type should be set after migration");
                }
            }
            finally
            {
                await itemManager.Delete(testCert);
            }
        }

        [TestMethod, Description("Ensure rapid update succeeds and increments version")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestRapidUpdateManagedCertificates(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            var testCert = BuildTestManagedCertificate();
            try
            {
                var managedCertificate = await itemManager.Update(testCert);

                for (var i = 0; i < 10; i++)
                {
                    await itemManager.Update(managedCertificate);
                }

                var managedCertificates = await itemManager.Find(new ManagedCertificateFilter { Id = managedCertificate.Id });
                Assert.HasCount(1, managedCertificates);
                Assert.AreEqual(10, managedCertificates[0].Version);
            }
            finally
            {
                await itemManager.Delete(testCert);
            }
        }

        [TestMethod, Description("Ensure managed site can be created, retrieved and deleted")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestCreateDeleteManagedCertificate(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

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

            //check item now exists
            managedCertificate = await itemManager.GetById(testSite.Id);
            Assert.IsNotNull(managedCertificate, "Retrieve managed site");

            // test update
            testSite.Name = "Test update";
            var result = await itemManager.Update(testSite);
            Assert.IsNotNull(result, "Update managed site");
            Assert.AreEqual(testSite.Name, result.Name);

            await itemManager.Delete(managedCertificate);
            managedCertificate = await itemManager.GetById(testSite.Id);

            // now check site has been delete
            Assert.IsNull(managedCertificate, "Managed site deleted");
        }

        [TestMethod, Description("Ensure managed site can be created, retrieved and deleted")]
        [DynamicData(nameof(TestDataStores))]
        [Ignore]
        public async Task TestCreateDeleteManyManagedCertificates(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

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
        [DynamicData(nameof(TestDataStores))]
        public async Task TestManagedCertificateFilters(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            Assert.IsTrue(await itemManager.IsInitialised(), "Database should be initialised ok");

            var testItem = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",
                UseStagingMode = true,
                IncludeInAutoRenew = true,
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
                Assert.IsEmpty(check, "There should be no previous test data present");

                var rnd = new Random();
                for (var i = 0; i < numItems; i++)
                {
                    var newTestItem = testItem.CopyAsTemplate();
                    newTestItem.Name = "FilterMultiTest_" + i;
                    newTestItem.Id = Guid.NewGuid().ToString();
                    newTestItem.RequestConfig.PrimaryDomain = i + "_" + testItem.RequestConfig.PrimaryDomain;
                    newTestItem.IncludeInAutoRenew = rnd.Next(1, 30) < 10 ? true : false;
                    newTestItem.DateExpiry = DateTimeOffset.UtcNow.AddDays(rnd.Next(5, 90));
                    newTestItem.DateStart = newTestItem.DateExpiry.Value.AddDays(-rnd.Next(1, 30));
                    newTestItem.DateLastOcspCheck = DateTimeOffset.UtcNow.AddMinutes(-rnd.Next(1, 60));
                    newTestItem.DateLastRenewalInfoCheck = DateTimeOffset.UtcNow.AddMinutes(-rnd.Next(1, 30));
                    newTestItem.DateRenewed = newTestItem.DateStart;
                    newTestItem.DateLastRenewalAttempt = newTestItem.DateRenewed;

                    if (rnd.Next(0, 10) >= 8)
                    {
                        // randomly make some items dns challenges
                        newTestItem.RequestConfig.Challenges.Add(new CertRequestChallengeConfig { ChallengeCredentialKey = "ABCD123", ChallengeProvider = "A.Test.Provider", ChallengeType = "dns-01" });
                    }

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
                    newTestItem.DateExpiry = DateTimeOffset.UtcNow.AddDays(rnd.Next(5, 90));
                    newTestItem.DateStart = DateTimeOffset.UtcNow.AddDays(-rnd.Next(1, 30));
                    newTestItem.DateLastOcspCheck = DateTimeOffset.UtcNow.AddMinutes(-rnd.Next(1, 30));
                    newTestItem.DateLastRenewalInfoCheck = DateTimeOffset.UtcNow.AddMinutes(-rnd.Next(1, 30));
                    newTestItem.DateRenewed = DateTimeOffset.UtcNow.AddDays(-rnd.Next(1, 30));
                    newTestItem.DateLastRenewalAttempt = newTestItem.DateRenewed;

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

                        Assert.IsLessThan(10, waitCount, "Waited too long for test data to commit");

                        Debug.WriteLine($"Wating for test data to be committed.. Got {result.Count} of {numExtraMultiTestData} ::  {waitCount}");
                        await Task.Delay(1000);
                    }
                }

                Debug.WriteLine($"Testing: Retrieve one result");
                var testResult1 = await itemManager.Find(new ManagedCertificateFilter { MaxResults = 1 });
                Assert.AreEqual(1, testResult1.Count());

                Debug.WriteLine($"Testing: Retrieve all results, check test data present.");
                var testResultAll = await itemManager.Find(new ManagedCertificateFilter { });
                var checkCount = testResultAll.Count(t => t.Name.IndexOf("FilterMultiTest") >= 0);
                Assert.AreEqual(numItems, checkCount, "Test data set should all be present");

                var testFilter = new List<ManagedCertificateFilter> {
                    new ManagedCertificateFilter { Id= inMemoryList.First().Id , FilterDescription="Test id match"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_" , FilterDescription="Test keyword filter by itself"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_" , Name="FilterMultiTest_1", FilterDescription="Test keyword filter and name"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", LastOCSPCheckMins = 10 , FilterDescription="Test LastOCSPCheckMins"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", LastRenewalInfoCheckMins = 5, FilterDescription="Test LastRenewalInfoCheckMins" },
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", MaxResults =10, FilterDescription="Test Max results" },
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", PageIndex=0, PageSize =5, FilterDescription="Paging test 0" },
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", PageIndex=1, PageSize =5, FilterDescription="Paging test 1" },
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", PageIndex=2, PageSize =5, FilterDescription="Paging test 3" },
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", PageIndex=2, PageSize =5, FilterDescription="Paging test 4 with sorting by renewal date", OrderBy= ManagedCertificateFilter.SortMode.RENEWAL_ASC },
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", ChallengeType ="http-01", FilterDescription="Challenge type filter"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", ChallengeProvider ="A.Test.Provider", FilterDescription="Challenge provider filter"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", StoredCredentialKey ="ABCD123", FilterDescription="Stored Credential filter"},
                    new ManagedCertificateFilter { Keyword = "FilterMultiTest_", IncludeOnlyNextAutoRenew =true, FilterDescription="Only Auto Renew filter"}
                };

                foreach (var filter in testFilter)
                {
                    Debug.WriteLine($"Testing: {filter.FilterDescription}");

                    var testResult = await itemManager.Find(filter);

                    var expectedResult = inMemoryList.Where(i =>
                           (filter.Id == null || i.Id.Equals(filter.Id, StringComparison.InvariantCultureIgnoreCase))
                           && (filter.Keyword == null || i.Name.IndexOf(filter.Keyword, StringComparison.InvariantCultureIgnoreCase) >= 0)
                           && (filter.Name == null || i.Name.Equals(filter.Name, StringComparison.InvariantCultureIgnoreCase))
                           && (filter.LastOCSPCheckMins == null || i.DateLastOcspCheck < DateTimeOffset.UtcNow.AddMinutes(-(int)filter.LastOCSPCheckMins))
                           && (filter.LastRenewalInfoCheckMins == null || i.DateLastRenewalInfoCheck < DateTimeOffset.UtcNow.AddMinutes(-(int)filter.LastRenewalInfoCheckMins))
                           && (filter.ChallengeType == null || i.RequestConfig.Challenges.Any(c => c.ChallengeType == filter.ChallengeType))
                           && (filter.ChallengeProvider == null || i.RequestConfig.Challenges.Any(c => c.ChallengeProvider == filter.ChallengeProvider))
                           && (filter.StoredCredentialKey == null || i.RequestConfig.Challenges.Any(c => c.ChallengeCredentialKey == filter.StoredCredentialKey))
                           && (filter.IncludeOnlyNextAutoRenew == false || i.IncludeInAutoRenew == true)
                        ).AsQueryable();

                    if (filter.OrderBy == ManagedCertificateFilter.SortMode.NAME_ASC)
                    {
                        expectedResult = expectedResult
                            .OrderBy(t => t.Name)
                            .AsQueryable();
                    }

                    if (filter.OrderBy == ManagedCertificateFilter.SortMode.RENEWAL_ASC)
                    {
                        expectedResult = expectedResult
                            .OrderBy(t => t.DateLastRenewalAttempt)
                            .AsQueryable();
                    }

                    if (filter.PageIndex != null && filter.PageSize != null)
                    {
                        expectedResult = expectedResult.Skip((int)filter.PageIndex * (int)filter.PageSize);
                    }

                    if (filter.PageSize != null)
                    {
                        expectedResult = expectedResult.Take((int)filter.PageSize);
                    }

                    if (filter.MaxResults > 0)
                    {
                        expectedResult = expectedResult.Take(filter.MaxResults);
                    }

                    Assert.IsGreaterThan(0, expectedResult.Count(), $"{filter.FilterDescription} Expected results should have more than zero results");
                    Assert.IsNotEmpty(testResult, $"{filter.FilterDescription} Test results should have more than zero results");

                    Assert.HasCount(expectedResult.Count(), testResult, filter.FilterDescription);

                    if (filter.OrderBy == ManagedCertificateFilter.SortMode.NAME_ASC)
                    {
                        Assert.AreEqual(testResult.First().Id, expectedResult.First().Id, $"{filter.FilterDescription} Test and expected should return same first items");
                        Assert.AreEqual(testResult.Last().Id, expectedResult.Last().Id, $"{filter.FilterDescription} Test and expected should return same last items");
                    }

                    if (filter.OrderBy == ManagedCertificateFilter.SortMode.RENEWAL_ASC)
                    {
                        Assert.AreEqual(testResult.First().Id, expectedResult.First().Id, $"{filter.FilterDescription} Test and expected should return same first items");
                        Assert.AreEqual(testResult.Last().Id, expectedResult.Last().Id, $"{filter.FilterDescription} Test and expected should return same last items");
                    }
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

        [TestMethod, Description("Test that schema upgrade handles existing data correctly")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestSchemaUpgradePreservesData(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            var testCert = BuildTestManagedCertificate();
            testCert.Name = "SchemaUpgradeTest_" + Guid.NewGuid().ToString();

            try
            {
                // Create a managed certificate
                var managedCertificate = await itemManager.Update(testCert);

                Assert.IsNotNull(managedCertificate, "Managed certificate should be created");
                Assert.IsNotNull(managedCertificate.Id, "Managed certificate should have an ID");

                // Retrieve it to verify it was stored correctly
                var retrieved = await itemManager.GetById(managedCertificate.Id);
                Assert.IsNotNull(retrieved, "Should be able to retrieve managed certificate");
                Assert.AreEqual(testCert.Name, retrieved.Name, "Retrieved certificate should have correct name");

                // Verify IsInitialised works
                var isInit = await itemManager.IsInitialised();
                Assert.IsTrue(isInit, "Store should be initialised");

                // Verify count works
                var count = await itemManager.CountAll(new ManagedCertificateFilter { Keyword = "SchemaUpgradeTest_" });
                Assert.IsGreaterThan(0, count, "Should find at least one test item");
            }
            finally
            {
                await itemManager.Delete(testCert);
            }
        }

        [TestMethod, Description("Test Health filter works correctly")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestHealthFilter(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            var testCertOk = BuildTestManagedCertificate();
            testCertOk.Name = "HealthFilterTest_OK_" + Guid.NewGuid().ToString();
            testCertOk.LastRenewalStatus = RequestState.Success;
            testCertOk.DateExpiry = DateTimeOffset.UtcNow.AddDays(30);

            var testCertError = BuildTestManagedCertificate();
            testCertError.Name = "HealthFilterTest_Error_" + Guid.NewGuid().ToString();
            testCertError.LastRenewalStatus = RequestState.Error;
            testCertError.DateExpiry = DateTimeOffset.UtcNow.AddDays(30);

            var testCertPaused = BuildTestManagedCertificate();
            testCertPaused.Name = "HealthFilterTest_Paused_" + Guid.NewGuid().ToString();
            testCertPaused.LastRenewalStatus = RequestState.Paused;
            testCertPaused.DateExpiry = DateTimeOffset.UtcNow.AddDays(30);

            var testCertNoCert = BuildTestManagedCertificate();
            testCertNoCert.Name = "HealthFilterTest_NoCert_" + Guid.NewGuid().ToString();
            testCertNoCert.LastRenewalStatus = null;
            testCertNoCert.DateExpiry = null;

            try
            {
                await itemManager.Update(testCertOk);
                await itemManager.Update(testCertError);
                await itemManager.Update(testCertPaused);
                await itemManager.Update(testCertNoCert);

                // Test OK filter
                var okResults = await itemManager.Find(new ManagedCertificateFilter { Keyword = "HealthFilterTest_", Health = "ok" });
                Assert.IsTrue(okResults.Any(r => r.Name == testCertOk.Name), "OK filter should include success items");

                // Test Error filter
                var errorResults = await itemManager.Find(new ManagedCertificateFilter { Keyword = "HealthFilterTest_", Health = "error" });
                Assert.IsTrue(errorResults.Any(r => r.Name == testCertError.Name), "Error filter should include error items");

                // Test Paused filter
                var pausedResults = await itemManager.Find(new ManagedCertificateFilter { Keyword = "HealthFilterTest_", Health = "paused" });
                Assert.IsTrue(pausedResults.Any(r => r.Name == testCertPaused.Name), "Paused filter should include paused items");

                // Test NoCertificate filter
                var noCertResults = await itemManager.Find(new ManagedCertificateFilter { Keyword = "HealthFilterTest_", Health = "nocertificate" });
                Assert.IsTrue(noCertResults.Any(r => r.Name == testCertNoCert.Name), "NoCertificate filter should include items with no expiry");
            }
            finally
            {
                await itemManager.Delete(testCertOk);
                await itemManager.Delete(testCertError);
                await itemManager.Delete(testCertPaused);
                await itemManager.Delete(testCertNoCert);
            }
        }

        [TestMethod, Description("Test IncludeOnlyNextAutoRenew filter works correctly")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestAutoRenewFilter(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            var testCertAutoRenew = BuildTestManagedCertificate();
            testCertAutoRenew.Name = "AutoRenewTest_Yes_" + Guid.NewGuid().ToString();
            testCertAutoRenew.IncludeInAutoRenew = true;

            var testCertNoAutoRenew = BuildTestManagedCertificate();
            testCertNoAutoRenew.Name = "AutoRenewTest_No_" + Guid.NewGuid().ToString();
            testCertNoAutoRenew.IncludeInAutoRenew = false;

            try
            {
                await itemManager.Update(testCertAutoRenew);
                await itemManager.Update(testCertNoAutoRenew);

                // Test IncludeOnlyNextAutoRenew filter
                var autoRenewResults = await itemManager.Find(new ManagedCertificateFilter { Keyword = "AutoRenewTest_", IncludeOnlyNextAutoRenew = true });

                Assert.IsTrue(autoRenewResults.Any(r => r.Name == testCertAutoRenew.Name), "Auto-renew filter should include items set to auto-renew");
                Assert.IsFalse(autoRenewResults.Any(r => r.Name == testCertNoAutoRenew.Name), "Auto-renew filter should exclude items not set to auto-renew");
            }
            finally
            {
                await itemManager.Delete(testCertAutoRenew);
                await itemManager.Delete(testCertNoAutoRenew);
            }
        }

        [TestMethod, Description("Test keyword search matches on Comments field")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestKeywordSearchOnComments(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            var uniqueTag = Guid.NewGuid().ToString("N")[..8];
            var commentSearchTerm = $"UniqueComment_{uniqueTag}";

            var testCertWithComment = BuildTestManagedCertificate();
            testCertWithComment.Name = $"CommentTest_NoMatch_{uniqueTag}";
            testCertWithComment.Comments = $"This item has a {commentSearchTerm} in the notes";

            var testCertWithoutComment = BuildTestManagedCertificate();
            testCertWithoutComment.Name = $"CommentTest_Plain_{uniqueTag}";
            testCertWithoutComment.Comments = null;

            var testCertNameMatch = BuildTestManagedCertificate();
            testCertNameMatch.Name = $"CommentTest_{commentSearchTerm}_InName";
            testCertNameMatch.Comments = null;

            try
            {
                await itemManager.Update(testCertWithComment);
                await itemManager.Update(testCertWithoutComment);
                await itemManager.Update(testCertNameMatch);

                // Search by the unique comment term - should match the item with the comment AND the item with it in the name
                var results = await itemManager.Find(new ManagedCertificateFilter { Keyword = commentSearchTerm });
                Assert.IsTrue(results.Any(r => r.Id == testCertWithComment.Id), $"[{storeType}] Keyword search should find item by Comments field");
                Assert.IsTrue(results.Any(r => r.Id == testCertNameMatch.Id), $"[{storeType}] Keyword search should find item by Name field");
                Assert.IsFalse(results.Any(r => r.Id == testCertWithoutComment.Id), $"[{storeType}] Keyword search should NOT find item without matching Comment or Name");

                // Verify CountAll matches Find for the same filter
                var filter = new ManagedCertificateFilter { Keyword = commentSearchTerm };
                var count = await itemManager.CountAll(filter);
                var findResults = await itemManager.Find(filter);
                Assert.AreEqual(findResults.Count, (int)count, $"[{storeType}] CountAll should match Find count for keyword filter");
            }
            finally
            {
                await itemManager.Delete(testCertWithComment);
                await itemManager.Delete(testCertWithoutComment);
                await itemManager.Delete(testCertNameMatch);
            }
        }

        [TestMethod, Description("Test Name filter works in isolation without Keyword")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestNameFilterAlone(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            var uniqueTag = Guid.NewGuid().ToString("N")[..8];
            var exactName = $"NameFilterTest_Exact_{uniqueTag}";

            var testCertExact = BuildTestManagedCertificate();
            testCertExact.Name = exactName;

            var testCertSimilar = BuildTestManagedCertificate();
            testCertSimilar.Name = $"NameFilterTest_Other_{uniqueTag}";

            try
            {
                await itemManager.Update(testCertExact);
                await itemManager.Update(testCertSimilar);

                // Name filter alone should match exactly
                var results = await itemManager.Find(new ManagedCertificateFilter { Name = exactName });
                Assert.HasCount(1, results, $"[{storeType}] Name filter should match exactly one item");
                Assert.AreEqual(testCertExact.Id, results.First().Id, $"[{storeType}] Name filter should return the exact match");

                // Name filter should not match partial names
                var noResults = await itemManager.Find(new ManagedCertificateFilter { Name = "NameFilterTest_NonExistent_" + uniqueTag });
                Assert.IsEmpty(noResults, $"[{storeType}] Name filter should return empty for non-matching name");
            }
            finally
            {
                await itemManager.Delete(testCertExact);
                await itemManager.Delete(testCertSimilar);
            }
        }

        [TestMethod, Description("Test CountAll matches Find for each filter type")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestCountAllMatchesFind(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            var uniqueTag = Guid.NewGuid().ToString("N")[..8];

            var testItems = new List<ManagedCertificate>();
            var rnd = new Random(42);

            for (var i = 0; i < 10; i++)
            {
                var item = BuildTestManagedCertificate();
                item.Name = $"CountAllTest_{uniqueTag}_{i}";
                item.IncludeInAutoRenew = i % 2 == 0;
                item.DateLastOcspCheck = DateTimeOffset.UtcNow.AddMinutes(-rnd.Next(1, 60));
                item.DateLastRenewalInfoCheck = DateTimeOffset.UtcNow.AddMinutes(-rnd.Next(1, 30));
                item.DateExpiry = i < 3 ? null : DateTimeOffset.UtcNow.AddDays(rnd.Next(5, 90));
                item.LastRenewalStatus = i switch
                {
                    0 => RequestState.Success,
                    1 => RequestState.Error,
                    2 => RequestState.Paused,
                    _ => RequestState.Success
                };
                item.Comments = i % 3 == 0 ? $"SomeComment_{uniqueTag}" : null;

                if (i % 4 == 0)
                {
                    item.RequestConfig.Challenges.Add(new CertRequestChallengeConfig
                    {
                        ChallengeType = "dns-01",
                        ChallengeProvider = "Test.DnsProvider",
                        ChallengeCredentialKey = "TestCredKey123"
                    });
                }

                testItems.Add(item);
            }

            try
            {
                foreach (var item in testItems)
                {
                    await itemManager.Update(item);
                }

                // Define filters to test CountAll consistency
                var filters = new List<ManagedCertificateFilter>
                {
                    new ManagedCertificateFilter { Id = testItems[0].Id, FilterDescription = "Id filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", FilterDescription = "Keyword filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", Name = testItems[0].Name, FilterDescription = "Name + Keyword filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", IncludeOnlyNextAutoRenew = true, FilterDescription = "AutoRenew filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", LastOCSPCheckMins = 10, FilterDescription = "LastOCSPCheckMins filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", LastRenewalInfoCheckMins = 5, FilterDescription = "LastRenewalInfoCheckMins filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", ChallengeType = "dns-01", FilterDescription = "ChallengeType filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", ChallengeProvider = "Test.DnsProvider", FilterDescription = "ChallengeProvider filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", StoredCredentialKey = "TestCredKey123", FilterDescription = "StoredCredentialKey filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", Health = "ok", FilterDescription = "Health=ok filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", Health = "error", FilterDescription = "Health=error filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", Health = "paused", FilterDescription = "Health=paused filter" },
                    new ManagedCertificateFilter { Keyword = $"CountAllTest_{uniqueTag}", Health = "nocertificate", FilterDescription = "Health=nocertificate filter" },
                };

                foreach (var filter in filters)
                {
                    var findResults = await itemManager.Find(filter);
                    var countResult = await itemManager.CountAll(filter);

                    Assert.AreEqual(findResults.Count, (int)countResult, $"[{storeType}] CountAll should match Find count for {filter.FilterDescription}");
                    Debug.WriteLine($"[{storeType}] {filter.FilterDescription}: Find={findResults.Count}, CountAll={countResult}");
                }
            }
            finally
            {
                foreach (var item in testItems)
                {
                    await itemManager.Delete(item);
                }
            }
        }

        [TestMethod, Description("Test each filter parameter returns correct results in isolation")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestIndividualFilterParameters(string storeType = null)
        {
            var itemManager = GetManagedItemStore(storeType ?? _storeType);

            var uniqueTag = Guid.NewGuid().ToString("N")[..8];

            // Create items with specific properties to test each filter in isolation
            var itemAutoRenewYes = BuildTestManagedCertificate();
            itemAutoRenewYes.Name = $"IndFilter_{uniqueTag}_AutoYes";
            itemAutoRenewYes.IncludeInAutoRenew = true;
            itemAutoRenewYes.LastRenewalStatus = RequestState.Success;
            itemAutoRenewYes.DateExpiry = DateTimeOffset.UtcNow.AddDays(30);
            itemAutoRenewYes.DateLastOcspCheck = DateTimeOffset.UtcNow.AddMinutes(-60);
            itemAutoRenewYes.DateLastRenewalInfoCheck = DateTimeOffset.UtcNow.AddMinutes(-60);

            var itemAutoRenewNo = BuildTestManagedCertificate();
            itemAutoRenewNo.Name = $"IndFilter_{uniqueTag}_AutoNo";
            itemAutoRenewNo.IncludeInAutoRenew = false;
            itemAutoRenewNo.LastRenewalStatus = RequestState.Error;
            itemAutoRenewNo.DateExpiry = DateTimeOffset.UtcNow.AddDays(30);
            itemAutoRenewNo.DateLastOcspCheck = DateTimeOffset.UtcNow; // recent check
            itemAutoRenewNo.DateLastRenewalInfoCheck = DateTimeOffset.UtcNow; // recent check

            var itemNoCert = BuildTestManagedCertificate();
            itemNoCert.Name = $"IndFilter_{uniqueTag}_NoCert";
            itemNoCert.LastRenewalStatus = null;
            itemNoCert.DateExpiry = null;
            itemNoCert.IncludeInAutoRenew = true;

            var itemPaused = BuildTestManagedCertificate();
            itemPaused.Name = $"IndFilter_{uniqueTag}_Paused";
            itemPaused.LastRenewalStatus = RequestState.Paused;
            itemPaused.DateExpiry = DateTimeOffset.UtcNow.AddDays(30);
            itemPaused.IncludeInAutoRenew = true;

            var itemDns = BuildTestManagedCertificate();
            itemDns.Name = $"IndFilter_{uniqueTag}_Dns";
            itemDns.LastRenewalStatus = RequestState.Success;
            itemDns.DateExpiry = DateTimeOffset.UtcNow.AddDays(30);
            itemDns.IncludeInAutoRenew = true;
            itemDns.RequestConfig.Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                new List<CertRequestChallengeConfig>
                {
                    new CertRequestChallengeConfig
                    {
                        ChallengeType = "dns-01",
                        ChallengeProvider = "Ind.TestProvider",
                        ChallengeCredentialKey = "IndCredKey_" + uniqueTag
                    }
                });

            var allItems = new List<ManagedCertificate> { itemAutoRenewYes, itemAutoRenewNo, itemNoCert, itemPaused, itemDns };

            try
            {
                foreach (var item in allItems)
                {
                    await itemManager.Update(item);
                }

                // Test 1: Id filter
                var idResult = await itemManager.Find(new ManagedCertificateFilter { Id = itemAutoRenewYes.Id });
                Assert.HasCount(1, idResult, $"[{storeType}] Id filter should return exactly one");
                Assert.AreEqual(itemAutoRenewYes.Id, idResult.First().Id);

                // Test 2: Name filter (exact match)
                var nameResult = await itemManager.Find(new ManagedCertificateFilter { Name = itemDns.Name });
                Assert.HasCount(1, nameResult, $"[{storeType}] Name filter should return exactly one");
                Assert.AreEqual(itemDns.Id, nameResult.First().Id);

                // Test 3: Keyword filter
                var keywordResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}" });
                Assert.HasCount(allItems.Count, keywordResult, $"[{storeType}] Keyword filter should match all test items");

                // Test 4: IncludeOnlyNextAutoRenew
                var autoRenewResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", IncludeOnlyNextAutoRenew = true });
                Assert.IsFalse(autoRenewResult.Any(r => r.Id == itemAutoRenewNo.Id), $"[{storeType}] AutoRenew filter should exclude non-auto-renew items");
                Assert.IsTrue(autoRenewResult.Any(r => r.Id == itemAutoRenewYes.Id), $"[{storeType}] AutoRenew filter should include auto-renew items");

                // Test 5: Health = ok (Success or null status, WITH expiry)
                var healthOkResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", Health = "ok" });
                Assert.IsTrue(healthOkResult.Any(r => r.Id == itemAutoRenewYes.Id), $"[{storeType}] Health=ok should include Success items");
                Assert.IsFalse(healthOkResult.Any(r => r.Id == itemAutoRenewNo.Id), $"[{storeType}] Health=ok should exclude Error items");

                // Test 6: Health = error
                var healthErrorResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", Health = "error" });
                Assert.IsTrue(healthErrorResult.Any(r => r.Id == itemAutoRenewNo.Id), $"[{storeType}] Health=error should include Error items");
                Assert.IsFalse(healthErrorResult.Any(r => r.Id == itemAutoRenewYes.Id), $"[{storeType}] Health=error should exclude Success items");

                // Test 7: Health = warning (maps to same SQL as error)
                var healthWarningResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", Health = "warning" });
                Assert.IsTrue(healthWarningResult.Any(r => r.Id == itemAutoRenewNo.Id), $"[{storeType}] Health=warning should include Error items");

                // Test 8: Health = paused
                var healthPausedResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", Health = "paused" });
                Assert.IsTrue(healthPausedResult.Any(r => r.Id == itemPaused.Id), $"[{storeType}] Health=paused should include Paused items");
                Assert.HasCount(1, healthPausedResult, $"[{storeType}] Health=paused should only return paused items");

                // Test 9: Health = nocertificate
                var healthNoCertResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", Health = "nocertificate" });
                Assert.IsTrue(healthNoCertResult.Any(r => r.Id == itemNoCert.Id), $"[{storeType}] Health=nocertificate should include items with no expiry");

                // Test 10: ChallengeType filter
                var challengeTypeResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", ChallengeType = "dns-01" });
                Assert.HasCount(1, challengeTypeResult, $"[{storeType}] ChallengeType filter should match one item");
                Assert.AreEqual(itemDns.Id, challengeTypeResult.First().Id);

                // Test 11: ChallengeProvider filter
                var challengeProviderResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", ChallengeProvider = "Ind.TestProvider" });
                Assert.HasCount(1, challengeProviderResult, $"[{storeType}] ChallengeProvider filter should match one item");
                Assert.AreEqual(itemDns.Id, challengeProviderResult.First().Id);

                // Test 12: StoredCredentialKey filter
                var credKeyResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", StoredCredentialKey = "IndCredKey_" + uniqueTag });
                Assert.HasCount(1, credKeyResult, $"[{storeType}] StoredCredentialKey filter should match one item");
                Assert.AreEqual(itemDns.Id, credKeyResult.First().Id);

                // Test 13: LastOCSPCheckMins (items checked more than 30 mins ago)
                var ocspResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", LastOCSPCheckMins = 30 });
                Assert.IsTrue(ocspResult.Any(r => r.Id == itemAutoRenewYes.Id), $"[{storeType}] OCSP filter should include items with old check date");
                Assert.IsFalse(ocspResult.Any(r => r.Id == itemAutoRenewNo.Id), $"[{storeType}] OCSP filter should exclude items with recent check date");

                // Test 14: LastRenewalInfoCheckMins (items checked more than 30 mins ago)
                var renewalInfoResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", LastRenewalInfoCheckMins = 30 });
                Assert.IsTrue(renewalInfoResult.Any(r => r.Id == itemAutoRenewYes.Id), $"[{storeType}] RenewalInfoCheck filter should include items with old check date");
                Assert.IsFalse(renewalInfoResult.Any(r => r.Id == itemAutoRenewNo.Id), $"[{storeType}] RenewalInfoCheck filter should exclude items with recent check date");

                // Test 15: MaxResults
                var maxResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", MaxResults = 2 });
                Assert.HasCount(2, maxResult, $"[{storeType}] MaxResults should limit results");

                // Test 16: Paging (PageIndex=0, PageSize=2)
                var page0Result = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", PageIndex = 0, PageSize = 2 });
                var page1Result = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", PageIndex = 1, PageSize = 2 });
                Assert.HasCount(2, page0Result, $"[{storeType}] Page 0 should return PageSize items");
                Assert.HasCount(2, page1Result, $"[{storeType}] Page 1 should return PageSize items");
                Assert.IsFalse(page0Result.Select(r => r.Id).Intersect(page1Result.Select(r => r.Id)).Any(), $"[{storeType}] Pages should not overlap");

                // Test 17: No results for non-matching filter
                var emptyResult = await itemManager.Find(new ManagedCertificateFilter { Keyword = $"IndFilter_{uniqueTag}", ChallengeType = "nonexistent-01" });
                Assert.IsEmpty(emptyResult, $"[{storeType}] Non-matching filter should return empty");
            }
            finally
            {
                foreach (var item in allItems)
                {
                    await itemManager.Delete(item);
                }
            }
        }
    }
}
