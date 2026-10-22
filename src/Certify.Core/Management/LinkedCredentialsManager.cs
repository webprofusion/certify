using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers;

namespace Certify.Management
{
    /// <summary>
    /// Credentials manager over a credential store which resolves secret fields linked to another stored credential (see <see cref="StoredCredentialLinks"/>)
    /// when a credential's values are unlocked for use. Raw secrets (e.g. for export) keep the links.
    /// </summary>
    public class LinkedCredentialsManager : ICredentialsManager
    {
        private readonly ICredentialsManager _store;
        private readonly ILog _log;

        public LinkedCredentialsManager(ICredentialsManager store, ILog log)
        {
            _store = store;
            _log = log;
        }

        public bool Init(string connectionString, ILog log, string instanceId = null) => _store.Init(connectionString, log, instanceId);

        public Task<bool> IsInitialised() => _store.IsInitialised();

        public Task<List<StoredCredential>> GetCredentials(string type = null, string storageKey = null) => _store.GetCredentials(type, storageKey);

        public Task<StoredCredential> GetCredential(string storageKey) => _store.GetCredential(storageKey);

        public Task<string> GetUnlockedCredential(string storageKey) => _store.GetUnlockedCredential(storageKey);

        public Task<StoredCredential> Update(StoredCredential credentialInfo) => _store.Update(credentialInfo);

        /// <summary>
        /// Get the unlocked values of a credential, with linked fields set to the linked credential's value. Result is null if a linked value is unavailable,
        /// as for any credential which cannot be unlocked
        /// </summary>
        public async Task<Dictionary<string, string>> GetUnlockedCredentialsDictionary(string storageKey)
        {
            var values = await _store.GetUnlockedCredentialsDictionary(storageKey);

            foreach (var linkKey in values?.Keys.Where(StoredCredentialLinks.IsLinkKey).ToList() ?? [])
            {
                var linkedStorageKey = values[linkKey]?.Trim();
                values.Remove(linkKey);

                var linkedValue = await GetLinkedValue(linkedStorageKey);
                if (linkedValue == null)
                {
                    _log?.Error("Stored credential [{storageKey}] uses stored credential [{linkedStorageKey}] for a value, which could not be found or unlocked.", storageKey, linkedStorageKey);
                    return null;
                }

                values[linkKey.Substring(0, linkKey.Length - StoredCredentialLinks.LinkKeySuffix.Length)] = linkedValue;
            }

            return values;
        }

        private async Task<string> GetLinkedValue(string linkedStorageKey)
        {
            if (string.IsNullOrEmpty(linkedStorageKey))
            {
                return null;
            }

            var linked = await _store.GetCredential(linkedStorageKey);
            if (linked?.ProviderType != StoredCredentialLinks.LinkedCredentialType)
            {
                return null;
            }

            var linkedValues = await _store.GetUnlockedCredentialsDictionary(linkedStorageKey);

            return linkedValues != null && linkedValues.TryGetValue(StoredCredentialLinks.LinkedValueKey, out var value) && !string.IsNullOrEmpty(value) ? value : null;
        }

        /// <summary>
        /// Delete a credential, unless it is in use by a managed item or other stored credentials link to it
        /// </summary>
        public async Task<ActionResult> Delete(IManagedItemStore itemStore, string storageKey)
        {
            var credential = await _store.GetCredential(storageKey);

            if (credential?.ProviderType == StoredCredentialLinks.LinkedCredentialType)
            {
                var linkedFrom = new List<string>();

                foreach (var c in await _store.GetCredentials())
                {
                    if (c.StorageKey == storageKey)
                    {
                        continue;
                    }

                    try
                    {
                        if (StoredCredentialLinks.GetLinkedStorageKeys(await _store.GetUnlockedCredential(c.StorageKey)).Contains(storageKey))
                        {
                            linkedFrom.Add(c.Title);
                        }
                    }
                    catch (Exception)
                    {
                        // skip credentials which cannot be unlocked here
                    }
                }

                if (linkedFrom.Any())
                {
                    return new ActionResult($"Credential is used by stored credentials {string.Join(", ", linkedFrom)}, could not delete.", false);
                }
            }

            return await _store.Delete(itemStore, storageKey);
        }
    }
}
