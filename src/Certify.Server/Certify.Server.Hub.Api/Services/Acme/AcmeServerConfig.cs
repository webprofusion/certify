using System.Collections.Concurrent;
using Certify.Models;
using Certify.Providers;
using Certify.Server.Hub.Api.Models.Acme;

namespace Certify.Server.Hub.Api.Services.Acme
{
    /// <summary>
    /// 
    /// </summary>
    public class AcmeServerConfig
    {
        // In-memory caches for performance (loaded from persistent storage)

        private static readonly ConcurrentDictionary<string, AcmeOrder> _orders = new();
        private static readonly ConcurrentDictionary<string, AcmeAuthorization> _authorizations = new();
        private static readonly ConcurrentDictionary<string, DateTime> _nonces = new();

        /// <summary>
        /// Last time an account's usage was persisted, so a client polling an order does not rewrite the account
        /// row on every request. Only used to decide whether a write is due; the stored account is the record.
        /// </summary>
        private static readonly ConcurrentDictionary<string, DateTimeOffset> _lastUsedWrites = new();

        /// <summary>
        /// Id prefix under which account resources are stored, so accounts can be listed and their kid recovered.
        /// </summary>
        private const string AccountIdPrefix = "account_";

        /// <summary>
        /// Maximum age of an issued replay nonce before it is rejected.
        /// </summary>
        public static readonly TimeSpan NonceMaxAge = TimeSpan.FromHours(1);

        /// <summary>
        /// How stale a recorded last-used time has to be before it is written again. Last use is reported to an
        /// operator, so minute granularity is enough and order polling does not turn into a write per request.
        /// </summary>
        public static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(1);

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

        /// <summary>
        /// Loads the saved state from persistent storage and migrates file-based state to the database if it exists.
        /// </summary>
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
            await AddTypedStoreItem($"{AccountIdPrefix}{accountKid}", account);
        }

        public Task StoreAcmeOrder(string orderId, AcmeOrder orderDetails)
        {
            if (orderDetails != null && orderDetails.CreatedAt == default)
            {
                orderDetails.CreatedAt = DateTime.UtcNow;
            }

            _orders[orderId] = orderDetails;
            return Task.CompletedTask;
        }

        internal Task StoreAcmeAuthorization(string authId, AcmeAuthorization authorization)
        {
            _authorizations[authId] = authorization;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns a snapshot of all currently tracked ACME orders.
        /// </summary>
        public IReadOnlyCollection<AcmeOrder> GetAcmeOrders()
        {
            return _orders.Values.ToList();
        }

        /// <summary>
        /// Returns orders that are past the supplied maximum age or past their Expires timestamp.
        /// </summary>
        public IReadOnlyCollection<AcmeOrder> GetStaleAcmeOrders(TimeSpan maxAge)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            return _orders.Values
                .Where(o =>
                    o.Status != OrderStatus.Processing &&
                    o.Status != OrderStatus.InternalFinalizationInProgress &&
                    ((o.CreatedAt != default && o.CreatedAt <= cutoff) ||
                     (o.CreatedAt == default && o.Expires != default && o.Expires <= DateTime.UtcNow) ||
                     (o.Expires != default && o.Expires <= DateTime.UtcNow)))
                .ToList();
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

        private async Task<List<TypedConfigurationItem<T>>> GetTypedStoreItems<T>()
        {
            return await _configStore.GetItems<TypedConfigurationItem<T>>(typeof(T).Name.ToLowerInvariant()) ?? [];
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
            _lastUsedWrites.TryRemove(accountKid, out _);
            await DeleteTypedStoreItem<AcmeAccount>($"{AccountIdPrefix}{accountKid}");
        }

        public async Task<AcmeAccount?> GetAccount(string accountKid)
        {
            return await GetTypedStoreItem<AcmeAccount>($"{AccountIdPrefix}{accountKid}");
        }

        /// <summary>
        /// All registered ACME accounts, keyed by the account url (kid) the client signs its requests with.
        /// </summary>
        public async Task<IReadOnlyCollection<KeyValuePair<string, AcmeAccount>>> GetAccounts()
        {
            var storedItems = await GetTypedStoreItems<AcmeAccount>();
            var accounts = new List<KeyValuePair<string, AcmeAccount>>();

            foreach (var storedItem in storedItems)
            {
                if (storedItem.Id?.StartsWith(AccountIdPrefix, StringComparison.Ordinal) != true)
                {
                    continue;
                }

                var account = storedItem.GetItem();

                if (account != null)
                {
                    accounts.Add(new KeyValuePair<string, AcmeAccount>(storedItem.Id[AccountIdPrefix.Length..], account));
                }
            }

            return accounts;
        }

        /// <summary>
        /// Record that an account was used to sign a request which passed validation. Writes are spaced by
        /// <see cref="LastUsedWriteInterval"/>, so a client polling an order does not rewrite the account each time.
        /// </summary>
        public async Task RecordAccountUsed(string accountKid)
        {
            if (string.IsNullOrWhiteSpace(accountKid))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;

            if (_lastUsedWrites.TryGetValue(accountKid, out var lastWrite) && now - lastWrite < LastUsedWriteInterval)
            {
                return;
            }

            var account = await GetAccount(accountKid);

            if (account == null)
            {
                return;
            }

            account.DateLastUsed = now;
            _lastUsedWrites[accountKid] = now;

            await UpdateTypedStoreItem($"{AccountIdPrefix}{accountKid}", account);
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

        /// <summary>
        /// Removes an ACME order and its associated authorizations from the in-memory cache.
        /// </summary>
        /// <param name="id">The identifier of the ACME order to remove.</param>
        public async Task RemoveAcmeOrder(string id)
        {
            var order = await GetAcmeOrder(id);
            if (order == null)
            {
                return;
            }

            var authIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var authId in order.AuthorizationIds ?? [])
            {
                if (!string.IsNullOrWhiteSpace(authId))
                {
                    authIds.Add(authId);
                }
            }

            // Authorizations on the ACME resource are URLs; extract trailing ids for cache keys.
            foreach (var authUrlOrId in order.Authorizations ?? [])
            {
                var extracted = ExtractTrailingId(authUrlOrId);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    authIds.Add(extracted);
                }
            }

            foreach (var authId in authIds)
            {
                _authorizations.TryRemove(authId, out _);
            }

            _orders.TryRemove(id, out _);
        }

        private static string? ExtractTrailingId(string? urlOrId)
        {
            if (string.IsNullOrWhiteSpace(urlOrId))
            {
                return null;
            }

            var value = urlOrId.Trim().TrimEnd('/');
            var separator = value.LastIndexOf('/');
            return separator >= 0 && separator < value.Length - 1
                ? value[(separator + 1)..]
                : value;
        }

        /// <summary>
        /// Records an issued replay nonce.
        /// </summary>
        public Task StoreAcmeNonce(string nonce, DateTime issuedAt)
        {
            _nonces[nonce] = issuedAt;

            // opportunistically drop expired nonces so the cache does not grow unbounded
            var cutoff = DateTime.UtcNow - NonceMaxAge;
            foreach (var expired in _nonces.Where(n => n.Value <= cutoff).Select(n => n.Key).ToList())
            {
                _nonces.TryRemove(expired, out _);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Atomically consumes a replay nonce. Returns true only if the nonce was previously issued,
        /// has not already been used and has not expired. Nonces are single use per RFC 8555 Section 6.5.
        /// </summary>
        public Task<bool> ConsumeAcmeNonce(string nonce)
        {
            if (string.IsNullOrEmpty(nonce) || !_nonces.TryRemove(nonce, out var issuedAt))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(issuedAt > DateTime.UtcNow - NonceMaxAge);
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
