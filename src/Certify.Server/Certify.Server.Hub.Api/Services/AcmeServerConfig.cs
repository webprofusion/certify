using System.Collections.Concurrent;
using Certify.Models;
using Certify.Providers;
using Certify.Server.Hub.Api.Models.Acme;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class AcmeServerConfig
    {
        // In-memory caches for performance (loaded from persistent storage)

        private static readonly ConcurrentDictionary<string, AcmeOrder> _orders = new();
        private static readonly ConcurrentDictionary<string, AcmeAuthorization> _authorizations = new();
        private static readonly ConcurrentDictionary<string, string> _nonces = new();

        private IConfigurationStore _configStore;
        private string _acmeServerConfigPath;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="configStore"></param>
        /// <param name="acmeServerConfigPath"></param>
        public AcmeServerConfig(IConfigurationStore configStore, string acmeServerConfigPath)
        {
            _configStore = configStore;
            _acmeServerConfigPath = acmeServerConfigPath;

            LoadSavedState();
        }

        public void LoadSavedState()
        {
            // migrate file based state to database if exists
            MigrateSavedState();
        }

        public async Task MigrateSavedState()
        {
            var accounts = new ConcurrentDictionary<string, AcmeAccount>();
            var accountKeys = new ConcurrentDictionary<string, JsonWebKey>();
            var consumedEab = new ConcurrentDictionary<string, string>();
            LoadStateFromFile("accounts.json", accounts, removeExistingAfterRead: true);
            LoadStateFromFile("account-keys.json", accountKeys, removeExistingAfterRead: true);
            LoadStateFromFile("consumed-eab-keys.json", consumedEab, removeExistingAfterRead: true);

            foreach (var acc in accounts)
            {
                var existing = await GetAccount(acc.Key);
                if (existing == null)
                {
                    await StoreAcmeAccount(acc.Key, acc.Value);
                }
            }

            foreach (var key in accountKeys)
            {
                var existing = await GetAccountKey(key.Key);
                if (existing == null)
                {
                    await StoreAcmeAccountKey(key.Key, key.Value);
                }
            }

            foreach (var eab in consumedEab)
            {
                if (await IsEabKeyConsumed(eab.Key))
                {
                    await StoreAcmeConsumedEabKey(eab.Key, eab.Value);
                }
            }
        }

        public void SaveStateToFile<T>(string fileName, ConcurrentDictionary<string, T> data)
        {
            var settingsPath = EnvironmentUtil.EnsuredAppDataPath(_acmeServerConfigPath);
            var filePath = Path.Join(settingsPath, fileName);
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            System.IO.File.WriteAllText(filePath, json);
        }

        public void LoadStateFromFile<T>(string fileName, ConcurrentDictionary<string, T> targetDictionary, bool removeExistingAfterRead = false)
        {
            if (targetDictionary.Count > 0)
            {
                return;
            }

            var settingsPath = EnvironmentUtil.EnsuredAppDataPath(_acmeServerConfigPath);
            var filePath = Path.Join(settingsPath, fileName);

            if (!System.IO.File.Exists(filePath))
            {
                return;
            }

            var json = System.IO.File.ReadAllText(filePath);
            var data = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, T>>(json);

            if (data == null)
            {
                return;
            }

            targetDictionary.Clear();
            foreach (var item in data)
            {
                targetDictionary.TryAdd(item.Key, item.Value);
            }

            if (removeExistingAfterRead)
            {
                try
                {
                    System.IO.File.Delete(filePath);
                }
                catch
                {
                    // best effort
                }
            }
        }

        /// <summary>
        /// Stores a consumed External Account Binding (EAB) key with the specified key ID and value.
        /// </summary>
        /// <param name="keyId">The identifier of the EAB key.</param>
        /// <param name="v">The value associated with the consumed EAB key.</param>
        public async Task StoreAcmeConsumedEabKey(string keyId, string v)
        {
            //_consumedEabKeys[keyId] = v;
            await AddTypedStoreItem($"consumed_{keyId}", v);
        }

        public async Task StoreAcmeAccountKey(string accountKid, JsonWebKey newAccountKey)
        {
            await AddTypedStoreItem($"key_{accountKid}", newAccountKey);
        }

        public async Task StoreAcmeAccount(string accountKid, AcmeAccount account)
        {
            await AddTypedStoreItem($"account_{accountKid}", account);
        }

        public async Task StoreAcmeOrder(string orderId, AcmeOrder orderDetails)
        {
            _orders[orderId] = orderDetails;
        }

        internal async Task StoreAcmeAuthorization(string authId, AcmeAuthorization authorization)
        {
            _authorizations[authId] = authorization;
        }

        private async Task AddTypedStoreItem<T>(string id, T item)
        {
            var storeItem = new TypedConfigurationItem<T>(id, item);
            await _configStore.Add<TypedConfigurationItem<T>>(typeof(T).Name.ToLowerInvariant(), storeItem);
        }

        private async Task<T?> GetTypedStoreItem<T>(string id)
        {
            var result = await _configStore.Get<TypedConfigurationItem<T>>(typeof(T).Name.ToLowerInvariant(), id);

            if (result != null)
            {
                return result.GetItem();
            }
            else
            {
                return default;
            }
        }

        private async Task UpdateTypedStoreItem<T>(string id, T item)
        {
            var storeItem = new TypedConfigurationItem<T>(id, item);
            await _configStore.Update<TypedConfigurationItem<T>>(typeof(T).Name.ToLowerInvariant(), storeItem);
        }

        private async Task<bool> DeleteTypedStoreItem<T>(string id)
        {
            return await _configStore.Delete<TypedConfigurationItem<T>>(typeof(T).Name.ToLowerInvariant(), id);
        }

        public async Task RemoveAcmeAccountKey(string accountKid)
        {
            await DeleteTypedStoreItem<JsonWebKey>($"key_{accountKid}");
        }
        public async Task RemoveAcmeAccount(string accountKid)
        {
            await DeleteTypedStoreItem<AcmeAccount>($"account_{accountKid}");
        }

        public async Task<AcmeAccount?> GetAccount(string accountKid)
        {
            return await GetTypedStoreItem<AcmeAccount>($"account_{accountKid}");
        }
        internal async Task<JsonWebKey?> GetAccountKey(string kid)
        {
            return await GetTypedStoreItem<JsonWebKey>($"key_{kid}");
        }

        internal async Task<AcmeOrder?> GetAcmeOrder(string orderId)
        {
            _orders.TryGetValue(orderId, out var order);
            return order;
        }
        public async Task<AcmeAuthorization?> GetAcmeAuthorization(string authId)
        {
            _authorizations.TryGetValue(authId, out var auth);
            return auth;
        }
        public async Task<AcmeOrder?> GetAcmeOrderByCertificateUri(string certUri)
        {
            var order = _orders.FirstOrDefault(o => o.Value.Certificate == certUri).Value;
            return order;
        }

        public async Task RemoveAcmeOrder(string id)
        {
            var order = await GetAcmeOrder(id);

            foreach (var authId in order?.Authorizations ?? [])
            {
                _authorizations.Remove(authId, out _);
            }

            _orders.Remove(id, out _);
        }

        public async Task StoreAcmeNonce(string nonce, string timestamp)
        {
            _nonces[nonce] = timestamp;
        }

        public async Task<string?> GetAcmeNonce(string nonce)
        {
            return _nonces.TryGetValue(nonce, out var timestamp) ? timestamp : null;
        }

        internal async Task<bool> IsEabKeyConsumed(string kid)
        {
            var existing = await GetTypedStoreItem<string>($"consumed_{kid}");
            if (string.IsNullOrEmpty(existing))
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
