using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Datastore.SQLite;
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

        public static IEnumerable<object[]> TestDataStores
        {
            get
            {
                return new[]
                {
                    new object[] { "sqlite" },
                    //new object[] { "postgres" },
                    //new object[] { "sqlserver" }
                };
            }
        }

        private IConfigurationStore GetStore(string storeType = null)
        {
            IConfigurationStore store = null;

            if (storeType == null)
            {
                storeType = _storeType;
            }

            if (storeType == "sqlite")
            {
                store = new SQLiteConfigurationStore(storageSubfolder: TEST_PATH);
            }
            /*  else if (storeType == "postgres")
              {
                  return new PostgresCredentialStore(Environment.GetEnvironmentVariable("CERTIFY_TEST_POSTGRES"));
              }
              else if (storeType == "sqlserver")
              {
                  return new SQLServerCredentialStore(Environment.GetEnvironmentVariable("CERTIFY_TEST_SQLSERVER"));
              }*/
            else
            {
                throw new ArgumentOutOfRangeException(nameof(storeType), "Unsupported store type " + storeType);
            }

            return store;
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

                Assert.IsTrue(item.Id == role1.Id, "Role retrieved");
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
    }
}
