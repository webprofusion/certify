using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Certify.Datastore.SQLite;
using Certify.Models;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Certify.Core.Tests.DataStores
{
    [TestClass]
    public class SQLiteManagedItemMigrationTests
    {
        private const string TEST_PATH = "Tests";

        [TestMethod, Description("Ensure sqlite legacy schema migration creates permanent backup before itemtype migration")]
        public async Task TestLegacySqliteSchemaMigrationCreatesPermanentBackup()
        {
            var testCert = BuildTestManagedCertificate();
            testCert.Name = "LegacySqliteSchemaBackup_" + Guid.NewGuid().ToString("N");
            var secondTestCert = BuildTestManagedCertificate();
            secondTestCert.Name = "LegacySqliteSchemaBackup_" + Guid.NewGuid().ToString("N");

            var storageSubfolder = Path.Combine(TEST_PATH, $"SQLiteMigrationBackup_{Guid.NewGuid():N}");
            var appDataPath = EnvironmentUtil.EnsuredAppDataPath(storageSubfolder);
            var dbPath = Path.Combine(appDataPath, $"{SQLiteStoreBase.ITEMMANAGERCONFIG}.db");
            var backupPath = $"{dbPath}.old";
            var legacyJson = JsonConvert.SerializeObject(testCert);
            var secondLegacyJson = JsonConvert.SerializeObject(secondTestCert);

            try
            {
                await InitializeLegacySqliteSchema(dbPath);
                await InsertLegacySqliteManagedItem(dbPath, testCert, legacyJson);
                await InsertLegacySqliteManagedItem(dbPath, secondTestCert, secondLegacyJson);

                var originalRowCount = await GetSqliteManagedItemRowCount(dbPath);

                var legacyColumns = await GetSqliteColumns(dbPath);
                Assert.IsTrue(legacyColumns.Contains("json"), "Legacy sqlite schema should start with json column only.");
                Assert.IsFalse(legacyColumns.Contains("itemtype"), "Legacy sqlite schema should not yet include itemtype.");

                var itemManager = new SQLiteManagedItemStore(storageSubfolder);
                var retrieved = await itemManager.GetById(testCert.Id);

                Assert.IsNotNull(retrieved, "Should retrieve managed certificate after sqlite migration");
                Assert.AreEqual(testCert.Name, retrieved.Name, "Retrieved certificate should preserve original data after migration");
                Assert.IsTrue(await itemManager.IsInitialised(), "SQLite store should be initialised after migration");

                Assert.IsTrue(File.Exists(backupPath), "Permanent sqlite backup should be created before itemtype migration");

                var migratedColumns = await GetSqliteColumns(dbPath);
                Assert.IsTrue(migratedColumns.Contains("config"), "Migrated sqlite schema should include config column.");
                Assert.IsTrue(migratedColumns.Contains("itemtype"), "Migrated sqlite schema should include itemtype column.");
                Assert.IsTrue(migratedColumns.Contains("itemvalue"), "Migrated sqlite schema should include itemvalue column.");
                Assert.IsFalse(migratedColumns.Contains("json"), "Migrated sqlite schema should no longer expose json column.");
                Assert.AreEqual(originalRowCount, await GetSqliteManagedItemRowCount(dbPath), "Migrated sqlite schema should preserve the original row count.");

                var backupColumns = await GetSqliteColumns(backupPath);
                Assert.IsTrue(backupColumns.Contains("json"), "Permanent backup should preserve the legacy json column.");
                Assert.IsFalse(backupColumns.Contains("itemtype"), "Permanent backup should preserve the pre-migration schema.");

                var backupJson = await GetSqliteLegacyJson(backupPath, testCert.Id);
                Assert.AreEqual(legacyJson, backupJson, "Permanent backup should preserve the original legacy row content.");

                var migratedItemType = await GetSqliteMigratedItemType(dbPath, testCert.Id);
                Assert.AreEqual("managedcertificate", migratedItemType, "Migrated sqlite item should be assigned the managedcertificate item type.");
            }
            finally
            {
                SqliteConnection.ClearAllPools();

                if (Directory.Exists(appDataPath))
                {
                    Directory.Delete(appDataPath, true);
                }
            }
        }

        private static ManagedCertificate BuildTestManagedCertificate()
        {
            return new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",
                UseStagingMode = true,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "testsite.com",
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                    [
                        new CertRequestChallengeConfig
                        {
                            ChallengeType = "http-01"
                        }
                    ]),
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = "c:\\inetpub\\wwwroot"
                },
                ItemType = ManagedCertificateType.SSL_ACME
            };
        }

        private static async Task InitializeLegacySqliteSchema(string dbPath)
        {
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var cmd = new SqliteCommand("CREATE TABLE manageditem (id TEXT NOT NULL PRIMARY KEY, json TEXT NOT NULL);", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertLegacySqliteManagedItem(string dbPath, ManagedCertificate cert, string config)
        {
            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var cmd = new SqliteCommand("INSERT INTO manageditem(id, json) VALUES(@id, @config);", conn);
            cmd.Parameters.Add(new SqliteParameter("@id", cert.Id));
            cmd.Parameters.Add(new SqliteParameter("@config", config));
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<List<string>> GetSqliteColumns(string dbPath)
        {
            var columns = new List<string>();

            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var cmd = new SqliteCommand("PRAGMA table_info(manageditem);", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                columns.Add((string)reader["name"]);
            }

            return columns;
        }

        private static async Task<string> GetSqliteLegacyJson(string dbPath, string id)
        {
            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var cmd = new SqliteCommand("SELECT json FROM manageditem WHERE id=@id", conn);
            cmd.Parameters.Add(new SqliteParameter("@id", id));

            return (string)await cmd.ExecuteScalarAsync();
        }

        private static async Task<string> GetSqliteMigratedItemType(string dbPath, string id)
        {
            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var cmd = new SqliteCommand("SELECT itemtype FROM manageditem WHERE id=@id", conn);
            cmd.Parameters.Add(new SqliteParameter("@id", id));

            return (string)await cmd.ExecuteScalarAsync();
        }

        private static async Task<long> GetSqliteManagedItemRowCount(string dbPath)
        {
            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var cmd = new SqliteCommand("SELECT COUNT(1) FROM manageditem", conn);

            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
    }
}
