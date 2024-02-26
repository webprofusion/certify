using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Providers;
using Certify.Shared;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private object _dataStoreLocker = new object();

        private async Task<IManagedItemStore> GetManagedItemStoreProvider(DataStoreConnection dataStore)
        {

            foreach (var p in _pluginManager.ManagedItemStoreProviders)
            {
                var providers = p.GetProviders(p.GetType());
                foreach (var provider in providers)
                {
                    if (provider.ProviderCategoryId == dataStore.TypeId)
                    {
                        var pr = p.GetProvider(p.GetType(), provider.Id);
                        if (pr != null)
                        {
                            if (provider.ProviderCategoryId == "sqlite" && string.IsNullOrEmpty(dataStore.ConnectionConfig))
                            {
                                pr.Init("", _serviceLog);
                            }
                            else
                            {
                                pr.Init(dataStore.ConnectionConfig, _serviceLog);
                            }

                            if (!await pr.IsInitialised())
                            {
                                _tc?.TrackEvent("DataStore_Init_Failed", new Dictionary<string, string> {
                                    { "provider", provider.Id }
                                });

                                _serviceLog.Error($"Managed item data store failed to initialise {dataStore.Id} : {dataStore.Title}");
                                return null;
                            }
                            else
                            {
                                _tc?.TrackEvent("DataStore_Init", new Dictionary<string, string> {
                                    { "provider", provider.Id }
                                });
                            }

                            return pr;
                        }
                        else
                        {
                            _serviceLog.Error($"Could not load data store plugin for data store {dataStore.Id} :{dataStore.Title} ");
                        }
                    }
                }
            }

            return null;
        }

        private async Task<ICredentialsManager> GetCredentialManagerProvider(DataStoreConnection dataStore)
        {

            foreach (var p in _pluginManager.CredentialStoreProviders)
            {
                var providers = p.GetProviders(p.GetType());
                foreach (var provider in providers)
                {
                    if (provider.ProviderCategoryId == dataStore.TypeId)
                    {
                        var pr = p.GetProvider(p.GetType(), provider.Id);

                        if (pr != null)
                        {
                            if (provider.ProviderCategoryId == "sqlite" && string.IsNullOrEmpty(dataStore.ConnectionConfig))
                            {
                                pr.Init("credentials", _useWindowsNativeFeatures, _serviceLog);
                            }
                            else
                            {
                                pr.Init(dataStore.ConnectionConfig, _useWindowsNativeFeatures, _serviceLog);
                            }

                            if (!await pr.IsInitialised())
                            {
                                _serviceLog.Error($"Credential data store failed to initialise {dataStore.Id} : {dataStore.Title}");
                            }
                            else
                            {
                                return pr;
                            }
                        }
                        else
                        {
                            _serviceLog.Error($"Could not load data store plugin for data store {dataStore.Id} : {dataStore.Title}");
                        }
                    }
                }
            }

            return null;
        }

        public async Task<bool> SelectManagedItemStore(string dataStoreId)
        {
            var dataStore = await GetDataStore(dataStoreId);

            if (dataStore == null)
            {
                _serviceLog.Error($"Could not match data store connection information to the specified store id: {dataStoreId}");
                return false;
            }

            var provider = await GetManagedItemStoreProvider(dataStore);

            if (provider == null)
            {
                _serviceLog.Error($"Could not match data store plugin for data store {dataStore.Id}");
                return false;
            }
            else
            {
                _itemManager = provider;
                return true;
            }
        }

        public async Task<bool> SelectCredentialsStore(string dataStoreId)
        {
            var dataStore = await GetDataStore(dataStoreId);
            var provider = await GetCredentialManagerProvider(dataStore);
            if (provider == null)
            {
                _serviceLog.Error($"Could not match data store plugin for data store {dataStore.Id}");
                return false;
            }
            else
            {
                _credentialsManager = provider;
                return true;
            }
        }

        public async Task<DataStoreConnection> GetDataStore(string dataStoreId)
        {
            var dataStores = await GetDataStores();
            return dataStores.FirstOrDefault(d => d.Id == dataStoreId);
        }
        public async Task<List<ProviderDefinition>> GetDataStoreProviders()
        {
            var allProviders = new List<ProviderDefinition>();

            foreach (var p in _pluginManager.ManagedItemStoreProviders)
            {
                var providers = p.GetProviders(p.GetType());
                allProviders.AddRange(providers);
            }

            return allProviders.OrderBy(p => p.Title).ToList();
        }

        public async Task<List<DataStoreConnection>> GetDataStores()
        {
            var dataStores = new List<DataStoreConnection>();

            var appDataPath = EnvironmentUtil.CreateAppDataPath();
            var path = Path.Combine(appDataPath, "datastores.json");

            if (System.IO.File.Exists(path))
            {
                // load content
                lock (_dataStoreLocker)
                {
                    var configData = System.IO.File.ReadAllText(path);
                    dataStores = Newtonsoft.Json.JsonConvert.DeserializeObject<List<DataStoreConnection>>(configData);
                }
            }
            else
            {
                // return a default data store for sqlite
                dataStores.Add(new DataStoreConnection { Id = "(default)", Title = "(Default SQLite)", TypeId = "sqlite" });
            }

            return dataStores.OrderBy(t => t.Title).ToList();
        }
        public async Task<List<ActionStep>> CopyDateStoreToTarget(string sourceId, string destId)
        {

            // connect to source and dest, copy data to target
            var results = new List<ActionStep>();

            // copy credentials TODO: may require re-encryption if being decrypted by a different machine
            var sourceCredManager = await GetCredentialManagerProvider(await GetDataStore(sourceId));
            var destCredManager = await GetCredentialManagerProvider(await GetDataStore(destId));
            var sourceItemManager = await GetManagedItemStoreProvider(await GetDataStore(sourceId));
            var destItemManager = await GetManagedItemStoreProvider(await GetDataStore(destId));

            if (!await sourceCredManager.IsInitialised())
            {
                results.Add(new ActionStep { HasError = true, Title = "Source Credentials Store", Description = "Failed to initialise the credential source." });
                return results;
            }

            if (!await destCredManager.IsInitialised())
            {
                results.Add(new ActionStep { HasError = true, Title = "Destination Credentials Store", Description = "Failed to initialise the target credential store." });
                return results;
            }

            if (!await sourceItemManager.IsInitialised())
            {
                results.Add(new ActionStep { HasError = true, Title = "Source Managed Item Store", Description = "Failed to initialise the managed item source." });
                return results;
            }

            if (!await destItemManager.IsInitialised())
            {
                results.Add(new ActionStep { HasError = true, Title = "Destination Managed Item Store", Description = "Failed to initialise the target managed item store." });
                return results;
            }

            // copy credentials
            var allCredentials = await sourceCredManager.GetCredentials();
            foreach (var cred in allCredentials)
            {
                try
                {
                    var unprotected = await sourceCredManager.GetUnlockedCredential(cred.StorageKey);
                    cred.Secret = unprotected;
                    await destCredManager.Update(cred);
                }
                catch
                {
                    results.Add(new ActionStep { HasWarning = true, Description = $"Could not decrypt credential {cred.Title}. Copy will continue." });
                }
            }

            results.Add(new ActionStep { Title = "Copied credentials from source to target", Description = $"{allCredentials.Count} credentials copied to target." });

            // copy managed items
            var allItems = await sourceItemManager.Find(new ManagedCertificateFilter { });
            await destItemManager.StoreAll(allItems);

            results.Add(new ActionStep { Title = "Copied managed item from source to target", Description = $"{allItems.Count} managed items copied to target." });
            return results;
        }

        public async Task<List<ActionStep>> TestDataStoreConnection(DataStoreConnection dataStore)
        {
            // connect to data store and check schema
            var results = new List<ActionStep>();

            var dataStoreAvailable = false;

            try
            {
                var itemProvider = await GetManagedItemStoreProvider(dataStore);
                var credProvider = await GetCredentialManagerProvider(dataStore);

                if (itemProvider != null && credProvider != null)
                {
                    dataStoreAvailable = true;
                }
            }
            catch
            {
                dataStoreAvailable = false;
            }

            if (!dataStoreAvailable)
            {
                results.Add(new ActionStep
                {
                    Title = "Data Store Init Failed",
                    Description = "The data store failed to connect. Verify the connection string is correct and the required connectivity, schema and permissions are present.",
                    HasError = true
                });
            }

            return results;
        }

        public async Task<List<ActionStep>> SetDefaultDataStore(string dataStoreId)
        {
            var dataStores = await GetDataStores();

            var store = dataStores.FirstOrDefault(d => d.Id == dataStoreId);

            // test connection before switching
            var testResults = await TestDataStoreConnection(store);

            if (testResults.Any(t => t.HasError))
            {
                return testResults;
            }

            SettingsManager.LoadAppSettings();
            CoreAppSettings.Current.ConfigDataStoreConnectionId = dataStoreId;
            SettingsManager.SaveAppSettings();

            await SelectManagedItemStore(dataStoreId);
            await SelectCredentialsStore(dataStoreId);

            var result = new List<ActionStep> { new ActionStep { Title = "Changed Default Data Store" } };
            return result;
        }

        public async Task<List<ActionStep>> UpdateDataStoreConnection(DataStoreConnection dataStore)
        {
            var testResults = await TestDataStoreConnection(dataStore);

            if (testResults.Any(t => t.HasError))
            {
                return testResults;
            }

            var dataStores = await GetDataStores();

            var existing = dataStores.FirstOrDefault(d => d.Id == dataStore.Id);
            if (existing != null)
            {
                dataStores.Remove(existing);
                dataStores.Add(dataStore);
            }
            else
            {
                dataStores.Add(dataStore);
            }

            //save
            var appDataPath = EnvironmentUtil.CreateAppDataPath();
            var path = Path.Combine(appDataPath, "datastores.json");

            lock (_dataStoreLocker)
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(dataStores);
                try
                {
                    System.IO.File.WriteAllText(path, json);
                }
                catch
                {
                    testResults.Add(new ActionStep { HasError = true, Title = "Data Store Config Save Failed", Description = "Failed to store the data store configuration to disk" });

                }
            }

            return testResults;
        }

        public async Task<List<ActionStep>> RemoveDataStoreConnection(string dataStoreId)
        {
            var results = new List<ActionStep>();
            if (CoreAppSettings.Current.ConfigDataStoreConnectionId == dataStoreId)
            {
                results.Add(new ActionStep("Data Store Remove Failed", "Cannot remove the data store currently in use.", true));
                return results;
            }

            var dataStores = await GetDataStores();

            var existing = dataStores.FirstOrDefault(d => d.Id == dataStoreId);
            if (existing != null)
            {
                dataStores.Remove(existing);

                //save
                var appDataPath = EnvironmentUtil.CreateAppDataPath();
                var path = Path.Combine(appDataPath, "datastores.json");

                lock (_dataStoreLocker)
                {
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(dataStores);
                    try
                    {
                        System.IO.File.WriteAllText(path, json);
                    }
                    catch
                    {
                        results.Add(new ActionStep("Failed to Save Data Stores Config", "The data store configuration coudl not be saved to disk", true));
                    }
                }
            }

            return results;
        }
    }
}
