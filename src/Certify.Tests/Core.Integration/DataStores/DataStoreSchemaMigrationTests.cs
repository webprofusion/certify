using System;
using System.Linq;
using System.Threading.Tasks;
using Certify.Datastore.Postgres;
using Certify.Datastore.SQLServer;
using Certify.Models;
using Certify.Models.Config;
using Certify.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Certify.Core.Tests.DataStores
{
    /// <summary>
    /// Covers applying the database schema as an explicit operation, so that the database user the service runs
    /// as can be restricted to reading and writing data with no schema modification rights.
    /// </summary>
    [TestClass]
    public class DataStoreSchemaMigrationTests
    {
        [ClassInitialize]
        public static async Task ClassInitialize(TestContext context)
        {
            await DataStoreTestContainers.InitializeAsync();
        }

        [ClassCleanup]
        public static async Task ClassCleanup()
        {
            await DataStoreTestContainers.DisposeAsync();
        }

        [TestMethod, Description("SQL Server: a read/write only user cannot apply the schema, an admin user can, and the runtime store then works")]
        public async Task TestSqlServerSchemaMigrationWithRestrictedRuntimeUser()
        {
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            var dbName = $"certify_migration_{suffix}";
            var loginName = $"certify_rt_{suffix}";
            var password = $"Rt#{suffix}Pw1";

            var adminConnectionString = await CreateSqlServerDatabase(dbName);
            var restrictedConnectionString = await CreateSqlServerRestrictedUser(adminConnectionString, dbName, loginName, password);

            try
            {
                IDataStoreSchemaProvider schemaProvider = new SQLServerManagedItemStore();

                // the restricted runtime user can see that the schema is missing but cannot create it
                var restrictedCheck = await schemaProvider.CheckSchema(restrictedConnectionString);
                Assert.AreEqual(DataStoreSchemaState.NotPresent, restrictedCheck.State, "Schema should be reported as not present");
                Assert.IsTrue(restrictedCheck.IsMigrationRequired, "Migration should be reported as required");
                Assert.IsFalse(restrictedCheck.CanApplySchemaChanges, "A read/write only user should not be able to apply schema changes");

                var restrictedApply = await schemaProvider.ApplySchemaMigrations(restrictedConnectionString);
                Assert.IsFalse(restrictedApply.IsSuccess, "A read/write only user should not be able to apply migrations");

                // the admin connection can
                var adminCheck = await schemaProvider.CheckSchema(adminConnectionString);
                Assert.AreEqual(DataStoreSchemaState.NotPresent, adminCheck.State, "Schema should still be not present");
                Assert.IsTrue(adminCheck.CanApplySchemaChanges, "An admin user should be able to apply schema changes");

                var adminApply = await schemaProvider.ApplySchemaMigrations(adminConnectionString);
                Assert.IsTrue(adminApply.IsSuccess, $"Migrations should apply successfully: {adminApply.Message}");
                Assert.IsTrue(adminApply.Result.Any(m => m.Id == "create-manageditem"), "The table should have been created");

                // the restricted user now sees a current schema and can use the store for normal operations
                var afterCheck = await schemaProvider.CheckSchema(restrictedConnectionString);
                Assert.AreEqual(DataStoreSchemaState.Current, afterCheck.State, $"Schema should now be current: {afterCheck.Message}");
                Assert.AreEqual(0, afterCheck.PendingMigrations.Count, "No migrations should remain outstanding");

                var store = new SQLServerManagedItemStore(restrictedConnectionString, instanceId: $"instance_{suffix}");
                Assert.IsTrue(await store.IsInitialised(), "The store should initialise using the restricted runtime user");

                var item = new ManagedCertificate { Id = Guid.NewGuid().ToString(), Name = "RestrictedUserTest" };
                await store.Update(item);

                var stored = await store.GetById(item.Id);
                Assert.IsNotNull(stored, "The restricted runtime user should be able to write and read items");
                Assert.AreEqual(item.Name, stored.Name);

                await store.Delete(item);
            }
            finally
            {
                await DropSqlServerDatabase(dbName, loginName);
            }
        }

        [TestMethod, Description("Postgres: a read/write only user cannot apply the schema, an admin user can, and the runtime store then works")]
        public async Task TestPostgresSchemaMigrationWithRestrictedRuntimeUser()
        {
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            var dbName = $"certify_migration_{suffix}";
            var roleName = $"certify_rt_{suffix}";
            var password = $"rt{suffix}pw";

            var adminConnectionString = await CreatePostgresDatabase(dbName);
            var restrictedConnectionString = await CreatePostgresRestrictedUser(adminConnectionString, roleName, password);

            try
            {
                IDataStoreSchemaProvider schemaProvider = new PostgresManagedItemStore();

                var restrictedCheck = await schemaProvider.CheckSchema(restrictedConnectionString);
                Assert.AreEqual(DataStoreSchemaState.NotPresent, restrictedCheck.State, "Schema should be reported as not present");
                Assert.IsTrue(restrictedCheck.IsMigrationRequired, "Migration should be reported as required");
                Assert.IsFalse(restrictedCheck.CanApplySchemaChanges, "A read/write only user should not be able to apply schema changes");

                var restrictedApply = await schemaProvider.ApplySchemaMigrations(restrictedConnectionString);
                Assert.IsFalse(restrictedApply.IsSuccess, "A read/write only user should not be able to apply migrations");

                var adminCheck = await schemaProvider.CheckSchema(adminConnectionString);
                Assert.AreEqual(DataStoreSchemaState.NotPresent, adminCheck.State, "Schema should still be not present");
                Assert.IsTrue(adminCheck.CanApplySchemaChanges, "An admin user should be able to apply schema changes");

                var adminApply = await schemaProvider.ApplySchemaMigrations(adminConnectionString);
                Assert.IsTrue(adminApply.IsSuccess, $"Migrations should apply successfully: {adminApply.Message}");
                Assert.IsTrue(adminApply.Result.Any(m => m.Id == "create-manageditem"), "The table should have been created");

                // the runtime user needs data rights on the newly created table
                await GrantPostgresTableRights(adminConnectionString, roleName);

                var afterCheck = await schemaProvider.CheckSchema(restrictedConnectionString);
                Assert.AreEqual(DataStoreSchemaState.Current, afterCheck.State, $"Schema should now be current: {afterCheck.Message}");
                Assert.AreEqual(0, afterCheck.PendingMigrations.Count, "No migrations should remain outstanding");

                var store = new PostgresManagedItemStore(restrictedConnectionString, instanceId: $"instance_{suffix}");
                Assert.IsTrue(await store.IsInitialised(), "The store should initialise using the restricted runtime user");

                var item = new ManagedCertificate { Id = Guid.NewGuid().ToString(), Name = "RestrictedUserTest" };
                await store.Update(item);

                var stored = await store.GetById(item.Id);
                Assert.IsNotNull(stored, "The restricted runtime user should be able to write and read items");
                Assert.AreEqual(item.Name, stored.Name);

                await store.Delete(item);
            }
            finally
            {
                await DropPostgresDatabase(dbName, roleName);
            }
        }

        [TestMethod, Description("Applying migrations twice is a no-op the second time")]
        public async Task TestSqlServerSchemaMigrationIsIdempotent()
        {
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            var dbName = $"certify_migration_{suffix}";
            var adminConnectionString = await CreateSqlServerDatabase(dbName);

            try
            {
                IDataStoreSchemaProvider schemaProvider = new SQLServerManagedItemStore();

                var first = await schemaProvider.ApplySchemaMigrations(adminConnectionString);
                Assert.IsTrue(first.IsSuccess, first.Message);
                Assert.IsTrue(first.Result.Count > 0, "The first run should apply at least one migration");

                var second = await schemaProvider.ApplySchemaMigrations(adminConnectionString);
                Assert.IsTrue(second.IsSuccess, second.Message);
                Assert.AreEqual(0, second.Result.Count, "The second run should apply nothing");

                var check = await schemaProvider.CheckSchema(adminConnectionString);
                Assert.AreEqual(DataStoreSchemaState.Current, check.State);
            }
            finally
            {
                await DropSqlServerDatabase(dbName, null);
            }
        }

        [TestMethod, Description("A legacy id-only schema is detected and migrated to the composite key")]
        public async Task TestSqlServerLegacySchemaIsMigrated()
        {
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            var dbName = $"certify_migration_{suffix}";
            var adminConnectionString = await CreateSqlServerDatabase(dbName);

            try
            {
                // the original shipped schema - id only, with the config column still called json
                await ExecuteSqlServer(adminConnectionString, @"
                    CREATE TABLE manageditem (
                        id NVARCHAR(255) NOT NULL,
                        json NVARCHAR(MAX) NOT NULL,
                        PRIMARY KEY (id)
                    );");

                IDataStoreSchemaProvider schemaProvider = new SQLServerManagedItemStore();

                var check = await schemaProvider.CheckSchema(adminConnectionString);
                Assert.AreEqual(DataStoreSchemaState.MigrationRequired, check.State, check.Message);

                var pendingIds = check.PendingMigrations.Select(m => m.Id).ToList();
                CollectionAssert.Contains(pendingIds, "rename-json-to-config", "The legacy json column should be detected");
                CollectionAssert.Contains(pendingIds, "add-itemtype");
                CollectionAssert.Contains(pendingIds, "add-instanceid");
                CollectionAssert.Contains(pendingIds, "composite-primary-key");

                var apply = await schemaProvider.ApplySchemaMigrations(adminConnectionString);
                Assert.IsTrue(apply.IsSuccess, apply.Message);

                var after = await schemaProvider.CheckSchema(adminConnectionString);
                Assert.AreEqual(DataStoreSchemaState.Current, after.State, after.Message);
            }
            finally
            {
                await DropSqlServerDatabase(dbName, null);
            }
        }

        [TestMethod, Description("SQL Server: an existing database keeps working without applying the optional composite key upgrade")]
        public async Task TestSqlServerExistingSchemaWorksWithoutOptionalUpgrade()
        {
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            var dbName = $"certify_migration_{suffix}";
            var instanceId = $"instance_{suffix}";
            var adminConnectionString = await CreateSqlServerDatabase(dbName);

            try
            {
                // the schema as shipped before the composite key change - every column present, primary key on id alone
                await ExecuteSqlServer(adminConnectionString, @"
                    CREATE TABLE manageditem (
                        id NVARCHAR(255) NOT NULL,
                        itemtype NVARCHAR(100) NOT NULL DEFAULT 'managedcertificate',
                        instanceid NVARCHAR(64) NOT NULL DEFAULT '',
                        config NVARCHAR(MAX) NOT NULL,
                        itemvalue NVARCHAR(MAX) NULL,
                        CONSTRAINT PK_manageditem_legacy PRIMARY KEY (id)
                    );
                    CREATE INDEX idx_manageditem_itemtype ON manageditem(itemtype);
                    CREATE INDEX idx_manageditem_instanceid ON manageditem(instanceid);");

                // a row from before instanceid was populated - it must not become invisible
                await ExecuteSqlServer(adminConnectionString,
                    "INSERT INTO manageditem (id, itemtype, instanceid, config) VALUES ('legacy-item', 'managedcertificate', '', '{\"Id\":\"legacy-item\",\"Name\":\"LegacyItem\"}');");

                IDataStoreSchemaProvider schemaProvider = new SQLServerManagedItemStore();

                var check = await schemaProvider.CheckSchema(adminConnectionString);

                Assert.AreEqual(DataStoreSchemaState.Current, check.State, $"An existing schema should be usable as it is: {check.Message}");
                Assert.IsFalse(check.IsMigrationRequired, "No migration should be required for an existing schema");
                Assert.IsTrue(check.HasOptionalMigrations, "The composite key upgrade should be offered as optional");
                Assert.AreEqual(0, check.RequiredMigrations.Count, "Nothing should be reported as required");
                CollectionAssert.Contains(check.OptionalMigrations.Select(m => m.Id).ToList(), "composite-primary-key");

                // connecting must not silently apply the optional change, even though these credentials could
                var store = new SQLServerManagedItemStore(adminConnectionString, instanceId: instanceId);

                Assert.AreEqual(1, await GetSqlServerPrimaryKeyColumnCount(adminConnectionString),
                    "Connecting must not rebuild the primary key - the upgrade is optional");

                // the pre-existing row is claimed for this instance rather than being left orphaned
                var legacyItem = await store.GetById("legacy-item");
                Assert.IsNotNull(legacyItem, "A row with an empty instanceid should be claimed and remain visible");

                // normal operations all work against the un-upgraded schema
                var item = new ManagedCertificate { Id = Guid.NewGuid().ToString(), Name = "NoUpgradeTest" };
                await store.Update(item);
                var stored = await store.GetById(item.Id);
                Assert.IsNotNull(stored, "Managed items should be readable without the optional upgrade");
                Assert.AreEqual(item.Name, stored.Name);
                await store.Delete(item);

                var credentialStore = new SQLServerCredentialStore(adminConnectionString, instanceId: instanceId);
                var credential = await credentialStore.Update(new StoredCredential
                {
                    StorageKey = Guid.NewGuid().ToString(),
                    ProviderType = "DNS01.API.Test",
                    Title = "NoUpgradeCredential",
                    Secret = "{\"key\":\"value\"}"
                });
                Assert.IsNotNull(credential, "Credentials should be storable without the optional upgrade");
                Assert.IsNotNull(await credentialStore.GetCredential(credential.StorageKey));

                var configStore = new SQLServerConfigurationStore(adminConnectionString, instanceId: instanceId);
                Assert.IsTrue(await configStore.IsInitialised(), "The configuration store should work without the optional upgrade");

                // and the upgrade is still available to apply later, on purpose
                var apply = await schemaProvider.ApplySchemaMigrations(adminConnectionString, includeOptional: true);
                Assert.IsTrue(apply.IsSuccess, apply.Message);
                Assert.IsTrue(apply.Result.Any(m => m.Id == "composite-primary-key"), "The optional step should be applied when asked for explicitly");

                Assert.AreEqual(3, await GetSqlServerPrimaryKeyColumnCount(adminConnectionString),
                    "The primary key should cover all three columns once the upgrade is applied");

                var after = await schemaProvider.CheckSchema(adminConnectionString);
                Assert.AreEqual(DataStoreSchemaState.Current, after.State);
                Assert.IsFalse(after.HasOptionalMigrations, "Nothing should remain outstanding");
            }
            finally
            {
                await DropSqlServerDatabase(dbName, null);
            }
        }

        [TestMethod, Description("SQL Server: connecting applies the additive migrations a legacy schema needs, but not the optional structural one")]
        public async Task TestSqlServerAutoMigrateSkipsOptionalSteps()
        {
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            var dbName = $"certify_migration_{suffix}";
            var adminConnectionString = await CreateSqlServerDatabase(dbName);

            try
            {
                // the original shipped schema - id only, with the config column still called json
                await ExecuteSqlServer(adminConnectionString, @"
                    CREATE TABLE manageditem (
                        id NVARCHAR(255) NOT NULL,
                        json NVARCHAR(MAX) NOT NULL,
                        PRIMARY KEY (id)
                    );");

                // connecting brings the schema up to a usable state on its own
                var store = new SQLServerManagedItemStore(adminConnectionString, instanceId: $"instance_{suffix}");

                var item = new ManagedCertificate { Id = Guid.NewGuid().ToString(), Name = "AutoMigrateTest" };
                await store.Update(item);
                Assert.IsNotNull(await store.GetById(item.Id), "The store should be usable after connecting to a legacy schema");

                Assert.AreEqual(1, await GetSqlServerPrimaryKeyColumnCount(adminConnectionString),
                    "The optional structural change must not be applied unattended");

                IDataStoreSchemaProvider schemaProvider = new SQLServerManagedItemStore();
                var check = await schemaProvider.CheckSchema(adminConnectionString);

                Assert.AreEqual(0, check.RequiredMigrations.Count, $"Required migrations should have been applied on connect: {check.Message}");
                Assert.IsTrue(check.HasOptionalMigrations, "The optional upgrade should still be pending");

                await store.Delete(item);
            }
            finally
            {
                await DropSqlServerDatabase(dbName, null);
            }
        }

        [TestMethod, Description("Postgres: an existing database keeps working without applying the optional composite key upgrade")]
        public async Task TestPostgresExistingSchemaWorksWithoutOptionalUpgrade()
        {
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            var dbName = $"certify_migration_{suffix}";
            var instanceId = $"instance_{suffix}";
            var adminConnectionString = await CreatePostgresDatabase(dbName);

            try
            {
                // the schema as shipped before the composite key change - every column present, primary key on id alone
                await ExecutePostgres(adminConnectionString, @"
                    CREATE TABLE manageditem (
                        id TEXT NOT NULL,
                        itemtype TEXT NOT NULL DEFAULT 'managedcertificate',
                        instanceid TEXT NOT NULL DEFAULT '',
                        config JSONB NOT NULL,
                        itemvalue TEXT NULL,
                        CONSTRAINT manageditem_pkey PRIMARY KEY (id)
                    );
                    CREATE INDEX idx_manageditem_itemtype ON manageditem(itemtype);
                    CREATE INDEX idx_manageditem_instanceid ON manageditem(instanceid);");

                await ExecutePostgres(adminConnectionString,
                    "INSERT INTO manageditem (id, itemtype, instanceid, config) VALUES ('legacy-item', 'managedcertificate', '', '{\"Id\":\"legacy-item\",\"Name\":\"LegacyItem\"}');");

                IDataStoreSchemaProvider schemaProvider = new PostgresManagedItemStore();

                var check = await schemaProvider.CheckSchema(adminConnectionString);

                Assert.AreEqual(DataStoreSchemaState.Current, check.State, $"An existing schema should be usable as it is: {check.Message}");
                Assert.IsFalse(check.IsMigrationRequired, "No migration should be required for an existing schema");
                Assert.IsTrue(check.HasOptionalMigrations, "The composite key upgrade should be offered as optional");
                Assert.AreEqual(0, check.RequiredMigrations.Count, "Nothing should be reported as required");
                CollectionAssert.Contains(check.OptionalMigrations.Select(m => m.Id).ToList(), "composite-primary-key");

                var store = new PostgresManagedItemStore(adminConnectionString, instanceId: instanceId);

                Assert.AreEqual(1, await GetPostgresPrimaryKeyColumnCount(adminConnectionString),
                    "Connecting must not rebuild the primary key - the upgrade is optional");

                var legacyItem = await store.GetById("legacy-item");
                Assert.IsNotNull(legacyItem, "A row with an empty instanceid should be claimed and remain visible");

                var item = new ManagedCertificate { Id = Guid.NewGuid().ToString(), Name = "NoUpgradeTest" };
                await store.Update(item);
                var stored = await store.GetById(item.Id);
                Assert.IsNotNull(stored, "Managed items should be readable without the optional upgrade");
                Assert.AreEqual(item.Name, stored.Name);
                await store.Delete(item);

                var credentialStore = new PostgresCredentialStore(adminConnectionString, instanceId: instanceId);
                var credential = await credentialStore.Update(new StoredCredential
                {
                    StorageKey = Guid.NewGuid().ToString(),
                    ProviderType = "DNS01.API.Test",
                    Title = "NoUpgradeCredential",
                    Secret = "{\"key\":\"value\"}"
                });
                Assert.IsNotNull(credential, "Credentials should be storable without the optional upgrade");
                Assert.IsNotNull(await credentialStore.GetCredential(credential.StorageKey));

                var configStore = new PostgresConfigurationStore(adminConnectionString, instanceId: instanceId);
                Assert.IsTrue(await configStore.IsInitialised(), "The configuration store should work without the optional upgrade");

                var apply = await schemaProvider.ApplySchemaMigrations(adminConnectionString, includeOptional: true);
                Assert.IsTrue(apply.IsSuccess, apply.Message);
                Assert.IsTrue(apply.Result.Any(m => m.Id == "composite-primary-key"), "The optional step should be applied when asked for explicitly");

                Assert.AreEqual(3, await GetPostgresPrimaryKeyColumnCount(adminConnectionString),
                    "The primary key should cover all three columns once the upgrade is applied");

                var after = await schemaProvider.CheckSchema(adminConnectionString);
                Assert.AreEqual(DataStoreSchemaState.Current, after.State);
                Assert.IsFalse(after.HasOptionalMigrations, "Nothing should remain outstanding");
            }
            finally
            {
                await DropPostgresDatabase(dbName, null);
            }
        }

        #region SQL Server helpers

        private static async Task<string> CreateSqlServerDatabase(string dbName)
        {
            await ExecuteSqlServer(DataStoreTestContainers.SqlServerConnectionString, $"CREATE DATABASE [{dbName}];");

            return new SqlConnectionStringBuilder(DataStoreTestContainers.SqlServerConnectionString)
            {
                InitialCatalog = dbName
            }.ConnectionString;
        }

        private static async Task<string> CreateSqlServerRestrictedUser(string adminConnectionString, string dbName, string loginName, string password)
        {
            // a login limited to reading and writing data, with no schema modification rights
            await ExecuteSqlServer(DataStoreTestContainers.SqlServerConnectionString,
                $"CREATE LOGIN [{loginName}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF;");

            await ExecuteSqlServer(adminConnectionString, $@"
                CREATE USER [{loginName}] FOR LOGIN [{loginName}];
                ALTER ROLE [db_datareader] ADD MEMBER [{loginName}];
                ALTER ROLE [db_datawriter] ADD MEMBER [{loginName}];");

            var builder = new SqlConnectionStringBuilder(adminConnectionString)
            {
                UserID = loginName,
                Password = password
            };

            builder.Remove("Integrated Security");

            return builder.ConnectionString;
        }

        private static async Task DropSqlServerDatabase(string dbName, string loginName)
        {
            try
            {
                await ExecuteSqlServer(DataStoreTestContainers.SqlServerConnectionString,
                    $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];");

                if (!string.IsNullOrEmpty(loginName))
                {
                    await ExecuteSqlServer(DataStoreTestContainers.SqlServerConnectionString, $"DROP LOGIN [{loginName}];");
                }
            }
            catch (SqlException)
            {
                // cleanup only
            }
        }

        private static async Task ExecuteSqlServer(string connectionString, string sql)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// The number of columns in the manageditem primary key - 1 for the original schema, 3 once the
        /// optional composite key upgrade has been applied
        /// </summary>
        private static async Task<int> GetSqlServerPrimaryKeyColumnCount(string connectionString)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                SELECT COUNT(*)
                FROM sys.key_constraints kc
                JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                WHERE kc.parent_object_id = OBJECT_ID('manageditem') AND kc.type = 'PK'", conn);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        #endregion

        #region Postgres helpers

        private static async Task<string> CreatePostgresDatabase(string dbName)
        {
            await ExecutePostgres(DataStoreTestContainers.PostgresConnectionString, $"CREATE DATABASE \"{dbName}\";");

            return new NpgsqlConnectionStringBuilder(DataStoreTestContainers.PostgresConnectionString)
            {
                Database = dbName
            }.ConnectionString;
        }

        private static async Task<string> CreatePostgresRestrictedUser(string adminConnectionString, string roleName, string password)
        {
            // a role which can connect and use the schema but cannot create or alter objects in it
            await ExecutePostgres(adminConnectionString, $"CREATE ROLE \"{roleName}\" LOGIN PASSWORD '{password}';");
            await ExecutePostgres(adminConnectionString, $"GRANT USAGE ON SCHEMA public TO \"{roleName}\";");

            return new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Username = roleName,
                Password = password
            }.ConnectionString;
        }

        private static async Task GrantPostgresTableRights(string adminConnectionString, string roleName)
        {
            await ExecutePostgres(adminConnectionString,
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON manageditem TO \"{roleName}\";");
        }

        private static async Task DropPostgresDatabase(string dbName, string roleName)
        {
            try
            {
                await ExecutePostgres(DataStoreTestContainers.PostgresConnectionString,
                    $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);");

                if (!string.IsNullOrEmpty(roleName))
                {
                    await ExecutePostgres(DataStoreTestContainers.PostgresConnectionString, $"DROP ROLE IF EXISTS \"{roleName}\";");
                }
            }
            catch (NpgsqlException)
            {
                // cleanup only
            }
        }

        private static async Task ExecutePostgres(string connectionString, string sql)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// The number of columns in the manageditem primary key - 1 for the original schema, 3 once the
        /// optional composite key upgrade has been applied
        /// </summary>
        private static async Task<int> GetPostgresPrimaryKeyColumnCount(string connectionString)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT COALESCE(array_length(con.conkey, 1), 0)
                FROM pg_constraint con
                WHERE con.conrelid = to_regclass('manageditem') AND con.contype = 'p'", conn);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        #endregion
    }
}
