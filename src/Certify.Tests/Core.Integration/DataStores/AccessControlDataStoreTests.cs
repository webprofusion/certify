using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Datastore.Postgres;
using Certify.Datastore.SQLite;
using Certify.Datastore.SQLServer;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.DataStores
{
    [TestClass]
    public class AccessControlDataStoreTests
    {
        private string _storeType = "sqlite";
        private const string TEST_PATH = "Tests";
        private ILog _log = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<AccessControlDataStoreTests>());

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

        public static IEnumerable<object[]> TestDataStores
        {
            get
            {
                return new[]
                {
                    new object[] { "sqlite" },
                    new object[] { "postgres" },
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

        private IConfigurationStore GetStore(string storeType = null)
        {
            if (storeType == null)
            {
                storeType = _storeType;
            }

            if (storeType == "sqlite")
            {
                return new SQLiteConfigurationStore(storageSubfolder: TEST_PATH);
            }
            else if (storeType == "postgres")
            {
                return new PostgresConfigurationStore(DataStoreTestContainers.PostgresConnectionString);
            }
            else if (storeType == "sqlserver")
            {
                return new SQLServerConfigurationStore(DataStoreTestContainers.SqlServerConnectionString);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(storeType), "Unsupported store type " + storeType);
            }
        }

        private IConfigurationStore GetStore(string storeType, string instanceId)
        {
            if (storeType == "sqlite")
            {
                return new SQLiteConfigurationStore(storageSubfolder: TEST_PATH);
            }
            else if (storeType == "postgres")
            {
                return new PostgresConfigurationStore(DataStoreTestContainers.PostgresConnectionString, instanceId: instanceId);
            }
            else if (storeType == "sqlserver")
            {
                return new SQLServerConfigurationStore(DataStoreTestContainers.SqlServerConnectionString, instanceId: instanceId);
            }

            throw new ArgumentOutOfRangeException(nameof(storeType), "Unsupported store type " + storeType);
        }

        [TestMethod]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestStoreSecurityPrincipal(string storeType)
        {
            var store = GetStore(storeType ?? _storeType);

            var sp = new SecurityPrincipal
            {
                Email = "test@test.com",
                PrincipalType = SecurityPrincipalType.User,
                Username = "test",
                Provider = StandardIdentityProviders.INTERNAL
            };

            try
            {
                await store.Add(nameof(SecurityPrincipal), sp);

                var list = await store.GetItems<SecurityPrincipal>(nameof(SecurityPrincipal));

                Assert.IsTrue(list.Any(l => l.Id == sp.Id), "Security Principal retrieved");
            }
            finally
            {
                // cleanup
                await store.Delete<SecurityPrincipal>(nameof(SecurityPrincipal), sp.Id);
            }
        }

        [TestMethod, Description("Test multi-tenant isolation for configuration items")]
        [DynamicData(nameof(ExternalTestDataStores))]
        public async Task TestConfigurationStoreMultiTenancy(string storeType)
        {
            var tenantA = $"tenantA_{Guid.NewGuid():N}";
            var tenantB = $"tenantB_{Guid.NewGuid():N}";

            var storeA = GetStore(storeType, tenantA);
            var storeB = GetStore(storeType, tenantB);

            var spA = new SecurityPrincipal
            {
                Email = "tenantA@test.com",
                PrincipalType = SecurityPrincipalType.User,
                Username = "tenantA",
                Provider = StandardIdentityProviders.INTERNAL
            };

            var spB = new SecurityPrincipal
            {
                Email = "tenantB@test.com",
                PrincipalType = SecurityPrincipalType.User,
                Username = "tenantB",
                Provider = StandardIdentityProviders.INTERNAL
            };

            try
            {
                await storeA.Add(nameof(SecurityPrincipal), spA);
                await storeB.Add(nameof(SecurityPrincipal), spB);

                var listA = await storeA.GetItems<SecurityPrincipal>(nameof(SecurityPrincipal));
                var listB = await storeB.GetItems<SecurityPrincipal>(nameof(SecurityPrincipal));

                Assert.IsTrue(listA.Any(s => s.Id == spA.Id), $"[{storeType}] Tenant A should see its own item");
                Assert.IsFalse(listA.Any(s => s.Id == spB.Id), $"[{storeType}] Tenant A should not see tenant B item");

                Assert.IsTrue(listB.Any(s => s.Id == spB.Id), $"[{storeType}] Tenant B should see its own item");
                Assert.IsFalse(listB.Any(s => s.Id == spA.Id), $"[{storeType}] Tenant B should not see tenant A item");
            }
            finally
            {
                await storeA.Delete<SecurityPrincipal>(nameof(SecurityPrincipal), spA.Id);
                await storeB.Delete<SecurityPrincipal>(nameof(SecurityPrincipal), spB.Id);
            }
        }

        [TestMethod]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestStoreRole(string storeType)
        {
            var store = GetStore(storeType ?? _storeType);

            var role1 = new Role("test", "Test Role", "A test role");
            var role2 = new Role("test2", "Test Role 2", "A test role 2");

            try
            {
                await store.Add(nameof(Role), role1);
                await store.Add(nameof(Role), role2);

                var item = await store.Get<Role>(nameof(Role), role1.Id);

                Assert.AreEqual(role1.Id, item.Id, "Role retrieved");
            }
            finally
            {
                // cleanup
                await store.Delete<Role>(nameof(Role), role1.Id);
                await store.Delete<Role>(nameof(Role), role2.Id);
            }
        }

        [TestMethod]
        public void TestStorePasswordHashing()
        {
            var store = GetStore(_storeType);

            var access = new AccessControl(_log, store);

            var firstHash = access.HashPassword("secret");

            Assert.IsNotNull(firstHash);

            Assert.IsTrue(access.IsPasswordValid("secret", firstHash));
        }

        [TestMethod]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestStoreGeneralAccessControl(string storeType)
        {

            var store = GetStore(storeType ?? _storeType);

            var access = new AccessControl(_log, store);

            var adminSp = new SecurityPrincipal
            {
                Id = "admin_01",
                Email = "admin@test.com",
                Description = "Primary test admin",
                PrincipalType = SecurityPrincipalType.User,
                Username = "admin01",
                Provider = StandardIdentityProviders.INTERNAL
            };

            var consumerSp = new SecurityPrincipal
            {
                Id = "dev_01",
                Email = "dev_test01@test.com",
                Description = "Consumer test",
                PrincipalType = SecurityPrincipalType.User,
                Username = "dev01",
                Password = "oldpassword",
                Provider = StandardIdentityProviders.INTERNAL
            };

            try
            {
                var list = await access.GetSecurityPrincipals(adminSp.Id);

                // add first admin security principal, bypass role check as there is no user to check yet

                await access.AddSecurityPrincipal(adminSp.Id, adminSp, bypassIntegrityCheck: true);

                await access.AddAssignedRole(adminSp.Id, new AssignedRole { Id = new Guid().ToString(), SecurityPrincipalId = adminSp.Id, RoleId = StandardRoles.Administrator.Id }, bypassIntegrityCheck: true);

                // add second security principal, allow role check as admin user should now exist with required role
                var added = await access.AddSecurityPrincipal(adminSp.Id, consumerSp);

                Assert.IsTrue(added, "Should be able to add a security principal");

                list = await access.GetSecurityPrincipals(adminSp.Id);

                Assert.IsTrue(list.Any(), "Should have security principals in store");

                // get updated sp so that password is hashed for comparison check
                consumerSp = await access.GetSecurityPrincipal(adminSp.Id, consumerSp.Id, includePassword: true);

                Assert.IsTrue(access.IsPasswordValid("oldpassword", consumerSp.Password));
            }
            finally
            {
                await access.DeleteSecurityPrincipal(adminSp.Id, consumerSp.Id);
                await access.DeleteSecurityPrincipal(adminSp.Id, adminSp.Id, allowSelfDelete: true);
            }
        }

        [TestMethod, Description("Test update of an existing item")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestUpdateConfigurationItem(string storeType)
        {
            var store = GetStore(storeType ?? _storeType);

            var sp = new SecurityPrincipal
            {
                Email = "update_test@test.com",
                PrincipalType = SecurityPrincipalType.User,
                Username = "updatetest",
                Provider = StandardIdentityProviders.INTERNAL
            };

            try
            {
                await store.Add(nameof(SecurityPrincipal), sp);

                // Verify initial state
                var retrieved = await store.Get<SecurityPrincipal>(nameof(SecurityPrincipal), sp.Id);
                Assert.IsNotNull(retrieved, $"[{storeType}] Item should exist after add");
                Assert.AreEqual("update_test@test.com", retrieved.Email);

                // Update the item
                sp.Email = "updated@test.com";
                sp.Username = "updateduser";
                await store.Update(nameof(SecurityPrincipal), sp);

                // Verify update applied
                var updated = await store.Get<SecurityPrincipal>(nameof(SecurityPrincipal), sp.Id);
                Assert.IsNotNull(updated, $"[{storeType}] Item should exist after update");
                Assert.AreEqual("updated@test.com", updated.Email, $"[{storeType}] Email should be updated");
                Assert.AreEqual("updateduser", updated.Username, $"[{storeType}] Username should be updated");
            }
            finally
            {
                await store.Delete<SecurityPrincipal>(nameof(SecurityPrincipal), sp.Id);
            }
        }

        [TestMethod, Description("Test GetItems returns only items of the requested type")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestGetItemsFiltersByType(string storeType)
        {
            var store = GetStore(storeType ?? _storeType);

            var sp = new SecurityPrincipal
            {
                Email = "filter_test@test.com",
                PrincipalType = SecurityPrincipalType.User,
                Username = "filtertest",
                Provider = StandardIdentityProviders.INTERNAL
            };

            var role = new Role("filter_role_1", "Filter Test Role", "A filter test role");

            try
            {
                await store.Add(nameof(SecurityPrincipal), sp);
                await store.Add(nameof(Role), role);

                // GetItems for SecurityPrincipal should not include Roles
                var principals = await store.GetItems<SecurityPrincipal>(nameof(SecurityPrincipal));
                Assert.IsTrue(principals.Any(p => p.Id == sp.Id), $"[{storeType}] Should find the security principal");

                var roles = await store.GetItems<Role>(nameof(Role));
                Assert.IsTrue(roles.Any(r => r.Id == role.Id), $"[{storeType}] Should find the role");

                // Ensure no cross-contamination
                Assert.IsFalse(principals.Any(p => p.Id == role.Id), $"[{storeType}] Security principals should not include roles");
            }
            finally
            {
                await store.Delete<SecurityPrincipal>(nameof(SecurityPrincipal), sp.Id);
                await store.Delete<Role>(nameof(Role), role.Id);
            }
        }

        [TestMethod, Description("Test delete returns true for existing items and item is gone after deletion")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestDeleteBehavior(string storeType)
        {
            var store = GetStore(storeType ?? _storeType);

            var sp = new SecurityPrincipal
            {
                Email = "delete_test@test.com",
                PrincipalType = SecurityPrincipalType.User,
                Username = "deletetest",
                Provider = StandardIdentityProviders.INTERNAL
            };

            await store.Add(nameof(SecurityPrincipal), sp);

            // Verify item exists
            var retrieved = await store.Get<SecurityPrincipal>(nameof(SecurityPrincipal), sp.Id);
            Assert.IsNotNull(retrieved, $"[{storeType}] Item should exist before delete");

            // Delete should succeed
            var deleted = await store.Delete<SecurityPrincipal>(nameof(SecurityPrincipal), sp.Id);
            Assert.IsTrue(deleted, $"[{storeType}] Delete should return true");

            // Item should no longer exist
            var afterDelete = await store.Get<SecurityPrincipal>(nameof(SecurityPrincipal), sp.Id);
            Assert.IsNull(afterDelete, $"[{storeType}] Item should not exist after delete");
        }

        [TestMethod, Description("Test Get returns null for non-existent items")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestGetNonExistentItem(string storeType)
        {
            var store = GetStore(storeType ?? _storeType);

            var result = await store.Get<SecurityPrincipal>(nameof(SecurityPrincipal), "non_existent_id_" + Guid.NewGuid());
            Assert.IsNull(result, $"[{storeType}] Get should return null for non-existent item");
        }

        [TestMethod, Description("Test multiple items of the same type can be stored and retrieved")]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestStoreMultipleItems(string storeType)
        {
            var store = GetStore(storeType ?? _storeType);

            var roles = new List<Role>
            {
                new Role("multi_role_1", "Multi Role 1", "First role"),
                new Role("multi_role_2", "Multi Role 2", "Second role"),
                new Role("multi_role_3", "Multi Role 3", "Third role")
            };

            try
            {
                foreach (var role in roles)
                {
                    await store.Add(nameof(Role), role);
                }

                var retrieved = await store.GetItems<Role>(nameof(Role));

                foreach (var role in roles)
                {
                    Assert.IsTrue(retrieved.Any(r => r.Id == role.Id), $"[{storeType}] Should find role {role.Id}");
                }

                // Verify Get by specific ID
                var specific = await store.Get<Role>(nameof(Role), roles[1].Id);
                Assert.IsNotNull(specific, $"[{storeType}] Should retrieve specific role by ID");
                Assert.AreEqual(roles[1].Title, specific.Title, $"[{storeType}] Retrieved role should have correct title");
            }
            finally
            {
                foreach (var role in roles)
                {
                    await store.Delete<Role>(nameof(Role), role.Id);
                }
            }
        }
    }
}
