using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Datastore.Postgres;
using Certify.Datastore.SQLServer;
using Certify.Management;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.DataStores
{
    [TestClass]
    public class StoredCredentialsDataStoreTests
    {
        private string _storeType = "postgres";
        private const string TEST_PATH = "Tests\\credentials";

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

        private ICredentialsManager GetCredentialManager(string storeType = null)
        {
            if (storeType == null)
            {
                storeType = _storeType;
            }

            if (storeType == "sqlite")
            {
                return new SQLiteCredentialStore(storageSubfolder: TEST_PATH);
            }
            else if (storeType == "postgres")
            {
                return new PostgresCredentialStore(Environment.GetEnvironmentVariable("CERTIFY_TEST_POSTGRES"));
            }
            else if (storeType == "sqlserver")
            {
                return new SQLServerCredentialStore(Environment.GetEnvironmentVariable("CERTIFY_TEST_SQLSERVER"));
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(storeType), "Unsupported store type " + storeType);
            }
        }

        [TestMethod]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestStoreCredential(string storeType)
        {
            var testSecret = "This is a secret";
            var test = new StoredCredential
            {
                ProviderType = "DNS01.API.Route53",
                Title = "A test credential",
                StorageKey = Guid.NewGuid().ToString(),
                Secret = testSecret
            };

            var credentialsManager = GetCredentialManager(storeType ?? _storeType);

            var result = await credentialsManager.Update(test);

            Assert.IsNotNull(result, "Credential stored OK");

            var list = await credentialsManager.GetCredentials();

            Assert.IsTrue(list.Any(l => l.StorageKey == test.StorageKey), "Credential retrieved");

            var secret = await credentialsManager.GetUnlockedCredential(test.StorageKey);
            Assert.IsNotNull(secret);
            Assert.IsTrue(secret == testSecret, "Credential decrypted");

            // perform test update to existing credential
            test.Title = "Updated title 1";
            result = await credentialsManager.Update(test);

            Assert.IsNotNull(result, "Credential updated OK");
            Assert.AreEqual(test.Title, result.Title);

            // cleanup

            await credentialsManager.Delete(null, test.StorageKey);
        }

        [TestMethod]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestStoreCredentialDictionary(string storeType)
        {
            var secrets = new Dictionary<string, string>();
            secrets.Add("zoneid", "ABC123");
            secrets.Add("secretid", "thereisnosecret");

            var test = new StoredCredential
            {
                ProviderType = "DNS01.API.Route53",
                Title = "A test credential",
                StorageKey = Guid.NewGuid().ToString(),
                Secret = Newtonsoft.Json.JsonConvert.SerializeObject(secrets)
            };

            var credentialsManager = GetCredentialManager(storeType ?? _storeType);

            var result = await credentialsManager.Update(test);

            Assert.IsNotNull(result, "Credential stored OK");

            var list = await credentialsManager.GetCredentials();

            Assert.IsTrue(list.Any(l => l.StorageKey == test.StorageKey), "Credential retrieved");

            var secret = await credentialsManager.GetUnlockedCredentialsDictionary(test.StorageKey);

            Assert.IsNotNull(secret);
            Assert.IsTrue(secret["zoneid"] == "ABC123", "Credential decrypted");
        }

        [TestMethod]
        [DynamicData(nameof(TestDataStores))]
        public async Task TestMultipleStoredCredential(string storeType)
        {
            var credentialsManager = GetCredentialManager(storeType ?? _storeType);

            var testCredentials = new List<StoredCredential>()
            {
                new StoredCredential
                {
                    ProviderType = "DNS01.API.Route53",
                    Title = "A test credential",
                    StorageKey = Guid.NewGuid().ToString(),
                    Secret = "A test secret"
                },
                new StoredCredential { Title="test credential 2", StorageKey = Guid.NewGuid().ToString() , ProviderType="An.Example.Provider.1", Secret="test2"},
                new StoredCredential { Title="test credential 3", StorageKey = Guid.NewGuid().ToString() , ProviderType="An.Example.Provider.1", Secret="test3"},
                new StoredCredential { Title="test credential 4", StorageKey = Guid.NewGuid().ToString() , ProviderType="An.Example.Provider.2", Secret="test4"},
                new StoredCredential { Title="test credential 5", StorageKey = Guid.NewGuid().ToString() , ProviderType="An.Example.Provider.2", Secret="test5"}
            };

            try
            {
                foreach (var c in testCredentials)
                {
                    var result = await credentialsManager.Update(c);
                    Assert.IsNotNull(result, "Credential stored OK");
                }

                var multiResults = await credentialsManager.GetCredentials("An.Example.Provider.1");
                Assert.IsTrue(multiResults.Count == 2, "Expected number of results for specific provider type");
                Assert.AreEqual("An.Example.Provider.1", multiResults[0].ProviderType, "Expected specific provider type");

                var bystorageKey = await credentialsManager.GetCredentials(storageKey: testCredentials[1].StorageKey);
                Assert.IsTrue(bystorageKey.Count == 1, "Expected number of results for specific storage key");
                Assert.AreEqual(testCredentials[1].StorageKey, bystorageKey.First().StorageKey, "Expected specific storage key");

            }
            finally
            {
                // cleanup
                foreach (var c in testCredentials)
                {
                    await credentialsManager.Delete(itemStore: null, c.StorageKey);
                }
            }
        }
    }
}
