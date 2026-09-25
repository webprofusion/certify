using System;
using System.IO;
using System.Threading.Tasks;
using Certify.Datastore.SQLite;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The SQLite stores keep every item type in one table keyed by id alone. A write under an id already held by an item
    /// of another type is refused, rather than silently replacing that item.
    /// </summary>
    [TestClass]
    public class SQLiteItemIdConflictTests
    {
        private string _storageSubfolder = default!;
        private SQLiteConfigurationStore _configStore = default!;
        private SQLiteManagedItemStore _itemStore = default!;
        private SQLiteCredentialStore _credentialStore = default!;

        [TestInitialize]
        public void Setup()
        {
            _storageSubfolder = $"_tests_idconflict_{Guid.NewGuid():N}";

            // all three share one database file, as they do in a default install
            _configStore = new SQLiteConfigurationStore(_storageSubfolder);
            _itemStore = new SQLiteManagedItemStore(_storageSubfolder);
            _credentialStore = new SQLiteCredentialStore(_storageSubfolder);
        }

        [TestCleanup]
        public void Cleanup()
        {
            SqliteConnection.ClearAllPools();

            var path = EnvironmentUtil.EnsuredAppDataPath(_storageSubfolder);

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        [TestMethod]
        public async Task ManagedItemUnderARoleId_IsRefused_AndTheRoleIsKept()
        {
            await _configStore.Add(nameof(Role), new Role("test_role", "Test Role", "role under test"));

            await Assert.ThrowsExactlyAsync<ItemIdConflictException>(() => _itemStore.Update(new ManagedCertificate { Id = "test_role", Name = "item" }));

            Assert.IsNotNull(await _configStore.Get<Role>(nameof(Role), "test_role"));
        }

        [TestMethod]
        public async Task CredentialUnderAPrincipalId_IsRefused_AndThePrincipalIsKept()
        {
            await _configStore.Add(nameof(SecurityPrincipal), new SecurityPrincipal { Id = "test_principal", Username = "user" });

            await Assert.ThrowsExactlyAsync<ItemIdConflictException>(() => _credentialStore.Update(new StoredCredential
            {
                StorageKey = "test_principal",
                Title = "credential",
                ProviderType = StandardAuthTypes.STANDARD_AUTH_PASSWORD,
                Secret = "secret"
            }));

            Assert.IsNotNull(await _configStore.Get<SecurityPrincipal>(nameof(SecurityPrincipal), "test_principal"));
        }

        [TestMethod]
        public async Task ConfigItemUnderAManagedItemId_IsRefused_AndTheManagedItemIsKept()
        {
            await _itemStore.Update(new ManagedCertificate { Id = "test_item", Name = "item" });

            await Assert.ThrowsExactlyAsync<ItemIdConflictException>(() => _configStore.Add(nameof(Role), new Role("test_item", "Test Role", "role under test")));

            Assert.IsNotNull(await _itemStore.GetById("test_item"));
        }

        [TestMethod]
        public async Task UpdatingAnItemOfTheSameType_IsAllowed()
        {
            await _configStore.Add(nameof(Role), new Role("test_role", "Test Role", "first"));
            await _configStore.Update(nameof(Role), new Role("test_role", "Test Role", "second"));

            await _itemStore.Update(new ManagedCertificate { Id = "test_item", Name = "first" });
            await _itemStore.Update(new ManagedCertificate { Id = "test_item", Name = "second" });

            Assert.AreEqual("second", (await _configStore.Get<Role>(nameof(Role), "test_role")).Description);
            Assert.AreEqual("second", (await _itemStore.GetById("test_item")).Name);
        }
    }
}
