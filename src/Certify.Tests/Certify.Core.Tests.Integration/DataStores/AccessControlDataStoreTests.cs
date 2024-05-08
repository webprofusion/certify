using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Datastore.SQLite;
using Certify.Management;
using Certify.Models.Config.AccessControl;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.DataStores
{
    [TestClass]
    public class AccessControlDataStoreTests
    {
        private string _storeType = "sqlite";
        private const string TEST_PATH = "Tests";

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

        private IAccessControlStore GetStore(string storeType = null)
        {
            IAccessControlStore store = null;

            if (storeType == null)
            {
                storeType = _storeType;
            }

            if (storeType == "sqlite")
            {
                store = new SQLiteAccessControlStore(storageSubfolder: TEST_PATH);
            }
            /* else if (storeType == "postgres")
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
        public async Task TestStoreSecurityPrinciple(string storeType)
        {
            var store = GetStore(storeType ?? _storeType);

            var sp = new SecurityPrinciple
            {
                Email = "test@test.com",
                PrincipleType = SecurityPrincipleType.User,
                Username = "test",
                Provider = StandardIdentityProviders.INTERNAL
            };

            try
            {
                await store.Add(nameof(SecurityPrinciple), sp);

                var list = await store.GetItems<SecurityPrinciple>(nameof(SecurityPrinciple));

                Assert.IsTrue(list.Any(l => l.Id == sp.Id), "Security Principle retrieved");
            }
            finally
            {
                // cleanup
                await store.Delete<SecurityPrinciple>(nameof(SecurityPrinciple), sp.Id);
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
            var access = new AccessControl(null, store);

            var firstHash = access.HashPassword("secret");

            Assert.IsNotNull(firstHash);

            Assert.IsTrue(access.IsPasswordValid("secret", firstHash));
        }

        [TestMethod]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestStoreGeneralAccessControl(string storeType)
        {

            var store = GetStore(storeType ?? _storeType);

            var access = new AccessControl(null, store);

            var adminSp = new SecurityPrinciple
            {
                Id = "admin_01",
                Email = "admin@test.com",
                Description = "Primary test admin",
                PrincipleType = SecurityPrincipleType.User,
                Username = "admin01",
                Provider = StandardIdentityProviders.INTERNAL
            };

            var consumerSp = new SecurityPrinciple
            {
                Id = "dev_01",
                Email = "dev_test01@test.com",
                Description = "Consumer test",
                PrincipleType = SecurityPrincipleType.User,
                Username = "dev01",
                Password = "oldpassword",
                Provider = StandardIdentityProviders.INTERNAL
            };

            try
            {
                var list = await access.GetSecurityPrinciples(adminSp.Id);

                // add first admin security principle, bypass role check as there is no user to check yet

                await access.AddSecurityPrinciple(adminSp.Id, adminSp, bypassIntegrityCheck: true);

                await access.AddAssignedRole(new AssignedRole { Id = new Guid().ToString(), SecurityPrincipleId = adminSp.Id, RoleId = StandardRoles.Administrator.Id });

                // add second security principle, bypass role check as this is just a data store test
                var added = await access.AddSecurityPrinciple(adminSp.Id, consumerSp, bypassIntegrityCheck: true);

                Assert.IsTrue(added, "Should be able to add a security principle");

                list = await access.GetSecurityPrinciples(adminSp.Id);

                Assert.IsTrue(list.Any(), "Should have security principles in store");

                // get updated sp so that password is hashed for comparison check
                consumerSp = await access.GetSecurityPrinciple(adminSp.Id, consumerSp.Id);

                Assert.IsTrue(access.IsPasswordValid("oldpassword", consumerSp.Password));
            }
            finally
            {
                await access.DeleteSecurityPrinciple(adminSp.Id, consumerSp.Id);
                await access.DeleteSecurityPrinciple(adminSp.Id, adminSp.Id, allowSelfDelete: true);
            }
        }
    }
}
