using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Secret fields of a stored credential can take their value from an API Key or Token stored credential, so one secret can feed many credentials
    /// </summary>
    [TestClass]
    public class LinkedCredentialsTests
    {
        private const string AzureClientSecretType = "ExternalAuth.Azure.ClientSecret";

        private static ProviderDefinition AzureDefinition => new()
        {
            Id = AzureClientSecretType,
            ProviderParameters =
            [
                new() { Key = "tenantid", Name = "Directory (tenant) Id", IsRequired = true },
                new() { Key = "secret", Name = "Client Secret", IsRequired = true, IsPassword = true }
            ]
        };

        [TestMethod]
        public void EditableFields_OfferLinkForEachSecretField()
        {
            var fields = StoredCredentialLinks.GetEditableFields(AzureDefinition);

            CollectionAssert.AreEqual(new[] { "tenantid", "secret", "secret.credential" }, fields.Select(f => f.Key).ToArray());

            var link = fields.Last();
            Assert.AreEqual(OptionType.StoredCredential, link.Type);
            Assert.AreEqual(StandardAuthTypes.STANDARD_AUTH_API_TOKEN, link.ExtendedConfig);
        }

        [TestMethod]
        public void EditableFields_ApiTokenCannotLinkToAnotherCredential()
        {
            var definition = new ProviderDefinition
            {
                Id = StandardAuthTypes.STANDARD_AUTH_API_TOKEN,
                ProviderParameters = [new() { Key = "api_token", IsRequired = true, IsPassword = true }]
            };

            Assert.AreEqual(1, StoredCredentialLinks.GetEditableFields(definition).Count);
        }

        [TestMethod]
        public void ValuesToStore_LinkedFieldStoresLinkInsteadOfValue()
        {
            var fields = StoredCredentialLinks.GetEditableFields(AzureDefinition);
            fields.Single(f => f.Key == "tenantid").Value = "tenant-1";
            fields.Single(f => f.Key == "secret").Value = "typed-secret";
            fields.Single(f => f.Key == "secret.credential").Value = "token-1";

            var result = StoredCredentialLinks.GetValuesToStore(fields);

            Assert.IsTrue(result.IsSuccess);
            CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["tenantid"] = "tenant-1", ["secret.credential"] = "token-1" }, result.Result);
        }

        [TestMethod]
        public void ValuesToStore_RequiredSecretNeedsValueOrLink()
        {
            var fields = StoredCredentialLinks.GetEditableFields(AzureDefinition);
            fields.Single(f => f.Key == "tenantid").Value = "tenant-1";

            var result = StoredCredentialLinks.GetValuesToStore(fields);

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("Client Secret is a required value", result.Message);
        }

        [TestMethod]
        public async Task UnlockedValues_ResolveLinkedSecret()
        {
            var store = CreateStore();
            var manager = new LinkedCredentialsManager(store, null);

            var values = await manager.GetUnlockedCredentialsDictionary("azure-1");

            CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["tenantid"] = "tenant-1", ["secret"] = "shared-secret" }, values);
        }

        [TestMethod]
        public async Task UnlockedValues_UnavailableLinkIsNotUnlocked()
        {
            var store = CreateStore();
            store.Add(new StoredCredential { StorageKey = "azure-2", ProviderType = AzureClientSecretType }, new Dictionary<string, string> { ["secret.credential"] = "missing" });
            store.Add(new StoredCredential { StorageKey = "azure-3", ProviderType = AzureClientSecretType }, new Dictionary<string, string> { ["secret.credential"] = "azure-1" });

            var manager = new LinkedCredentialsManager(store, null);

            Assert.IsNull(await manager.GetUnlockedCredentialsDictionary("azure-2"), "a link to a missing credential should not unlock");
            Assert.IsNull(await manager.GetUnlockedCredentialsDictionary("azure-3"), "only API Key or Token credentials can supply linked values");
        }

        [TestMethod]
        public async Task RawSecret_KeepsLink()
        {
            var manager = new LinkedCredentialsManager(CreateStore(), null);

            CollectionAssert.AreEqual(new List<string> { "token-1" }, StoredCredentialLinks.GetLinkedStorageKeys(await manager.GetUnlockedCredential("azure-1")));
        }

        [TestMethod]
        public async Task Delete_LinkedCredentialIsKept()
        {
            var store = CreateStore();
            var manager = new LinkedCredentialsManager(store, null);

            var result = await manager.Delete(null, "token-1");

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "Azure App");
            Assert.IsNotNull(await store.GetCredential("token-1"));

            Assert.IsTrue((await manager.Delete(null, "azure-1")).IsSuccess);
            Assert.IsTrue((await manager.Delete(null, "token-1")).IsSuccess, "once nothing links to it the credential can be deleted");
        }

        [TestMethod]
        public async Task Unlock_ReturnsLinkedValue()
        {
            var store = CreateStore();

            var result = await Unlock(store, "azure-1");

            Assert.IsTrue(result.IsSuccess, result.Message);
            CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["tenantid"] = "tenant-1", ["secret"] = "shared-secret" }, JsonConvert.DeserializeObject<Dictionary<string, string>>(result.Result.Secret));
        }

        [TestMethod]
        public async Task Unlock_LinkedValueNeedsLinkedCredentialToAllowUnlock()
        {
            var store = CreateStore();
            (await store.GetCredential("token-1")).AllowUnlock = false;

            var result = await Unlock(store, "azure-1");

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.Result, "the linked value should not be released");
            StringAssert.Contains(result.Message, "'Shared Secret'", "the message should name the credential to change");
        }

        [TestMethod]
        public async Task LinksCommand_ReturnsLinkedStorageKeys()
        {
            var manager = new CertifyManager();
            typeof(CertifyManager).GetField("_credentialsManager", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, new LinkedCredentialsManager(CreateStore(), null));

            var result = await manager.PerformHubCommandWithResult(new InstanceCommandRequest(ManagementHubCommands.GetStoredCredentialLinks, [new("storageKey", "azure-1")]));

            CollectionAssert.AreEqual(new List<string> { "token-1" }, (List<string>)result.ObjectValue);
        }

        private static async Task<StoredCredentialUnlockResult> Unlock(TestCredentialStore store, string storageKey)
        {
            var manager = new CertifyManager();
            typeof(CertifyManager).GetField("_credentialsManager", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, new LinkedCredentialsManager(store, null));

            var result = await manager.PerformHubCommandWithResult(new InstanceCommandRequest(ManagementHubCommands.UnlockStoredCredential, [new("storageKey", storageKey)]));

            return (StoredCredentialUnlockResult)result.ObjectValue;
        }

        private static TestCredentialStore CreateStore()
        {
            var store = new TestCredentialStore();
            store.Add(new StoredCredential { StorageKey = "token-1", Title = "Shared Secret", ProviderType = StandardAuthTypes.STANDARD_AUTH_API_TOKEN, AllowUnlock = true }, new Dictionary<string, string> { ["api_token"] = "shared-secret" });
            store.Add(new StoredCredential { StorageKey = "azure-1", Title = "Azure App", ProviderType = AzureClientSecretType, AllowUnlock = true }, new Dictionary<string, string> { ["tenantid"] = "tenant-1", ["secret.credential"] = "token-1" });
            store.AddRaw(new StoredCredential { StorageKey = "acme-1", Title = "ACME Account", ProviderType = StandardAuthTypes.STANDARD_ACME_ACCOUNT }, "{\"AccountKey\":{\"nested\":true}}");
            return store;
        }

        private sealed class TestCredentialStore : ICredentialsManager
        {
            private readonly Dictionary<string, (StoredCredential Credential, string Secret)> _items = new();

            public void Add(StoredCredential credential, Dictionary<string, string> values) => AddRaw(credential, JsonConvert.SerializeObject(values));
            public void AddRaw(StoredCredential credential, string secret) => _items[credential.StorageKey] = (credential, secret);

            public bool Init(string connectionString, ILog log, string instanceId = null) => true;
            public Task<bool> IsInitialised() => Task.FromResult(true);
            public Task<ActionResult> Delete(IManagedItemStore itemStore, string storageKey) => Task.FromResult(new ActionResult("Deleted", _items.Remove(storageKey)));
            public Task<List<StoredCredential>> GetCredentials(string type = null, string storageKey = null) => Task.FromResult(_items.Values.Select(i => i.Credential).ToList());
            public Task<StoredCredential> GetCredential(string storageKey) => Task.FromResult(_items.TryGetValue(storageKey, out var i) ? i.Credential : null);
            public Task<string> GetUnlockedCredential(string storageKey) => Task.FromResult(_items.TryGetValue(storageKey, out var i) ? i.Secret : null);
            public Task<Dictionary<string, string>> GetUnlockedCredentialsDictionary(string storageKey) => Task.FromResult(_items.TryGetValue(storageKey, out var i) ? JsonConvert.DeserializeObject<Dictionary<string, string>>(i.Secret) : null);
            public Task<StoredCredential> Update(StoredCredential credentialInfo) => Task.FromResult(credentialInfo);
        }
    }
}
