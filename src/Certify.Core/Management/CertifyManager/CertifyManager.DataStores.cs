using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Datastore.SQLite;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Models.Reporting;
using Certify.Providers;
using Certify.Shared;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private object _dataStoreLocker = new object();

        /// <summary>
        /// Tracks the current data store connection status
        /// </summary>
        private DataStoreStatus _dataStoreStatus = new DataStoreStatus();

        /// <summary>
        /// Gets the current data store connection status
        /// </summary>
        public DataStoreStatus GetDataStoreStatus() => _dataStoreStatus;

        /// <summary>
        /// Returns true if the service is running in degraded mode (data store unavailable)
        /// </summary>
        public bool IsInDegradedMode => _dataStoreStatus.IsDegradedMode;

        private async Task InitDataStore()
        {
            var enableExtendedDataStores = true;

            _dataStoreStatus = new DataStoreStatus();

            try
            {
                if (enableExtendedDataStores)
                {

                    var defaultStoreId = CoreAppSettings.Current.ConfigDataStoreConnectionId;
                    var dataStoreInfo = await GetDataStore(defaultStoreId);

                    _dataStoreStatus.DataStoreId = defaultStoreId;
                    _dataStoreStatus.DataStoreType = dataStoreInfo?.TypeId;

                    if (string.IsNullOrEmpty(defaultStoreId) || defaultStoreId == "(default)" || defaultStoreId == "0")
                    {
                        // default sqlite storage
                        _itemManager = new SQLiteManagedItemStore("", _serviceLog);
                        _credentialsManager = new SQLiteCredentialStore("", _serviceLog);

                        // config store is a generic store for settings etc
                        _configStore = new SQLiteConfigurationStore("", _serviceLog);
                        _accessControl = new AccessControl(_serviceLog, _configStore);

                        _dataStoreStatus.DataStoreType = "sqlite";
                    }
                    else
                    {
                        // select data store based on current default selection
                        var managedItemStoreOK = await SelectManagedItemStore(defaultStoreId);
                        if (!managedItemStoreOK)
                        {
                            var msg = $"Managed Item Store {defaultStoreId} could not connect or load.";
                            _serviceLog.Error(msg);
                            throw new DataStoreConnectionException(msg, defaultStoreId, dataStoreInfo?.TypeId);
                        }

                        var credentialStoreOK = await SelectCredentialsStore(defaultStoreId);

                        if (!credentialStoreOK)
                        {
                            var msg = $"Credential Store {defaultStoreId} could not connect or load.";
                            _serviceLog.Error(msg);
                            throw new DataStoreConnectionException(msg, defaultStoreId, dataStoreInfo?.TypeId);
                        }

                        var configStoreOK = await SelectConfigurationStore(defaultStoreId);
                        if (!configStoreOK)
                        {
                            var msg = $"Configuration Store {defaultStoreId} could not connect or load.";
                            _serviceLog.Error(msg);
                            throw new DataStoreConnectionException(msg, defaultStoreId, dataStoreInfo?.TypeId);
                        }

                        _serviceLog.Information($"Certify Manager is connected to data store {dataStoreInfo.Id} '{dataStoreInfo.Title}' [{dataStoreInfo.TypeId}]");
                    }
                }
                else
                {
                    _itemManager = new SQLiteManagedItemStore("", _serviceLog);
                    _credentialsManager = new SQLiteCredentialStore("", _serviceLog);

                    _configStore = new SQLiteConfigurationStore("", _serviceLog);
                    _accessControl = new AccessControl(_serviceLog, _configStore);
                }

                // attempt to create and delete a test item
                try
                {
                    var item = new ManagedCertificate { Id = $"writecheck_{Guid.NewGuid()}" };

                    await _itemManager.Update(item);

                    await _itemManager.Delete(item);
                }
                catch (Exception ex)
                {
                    _serviceLog?.Error(ex, $"Data store write failed. Check connection and data integrity. Ensure file based databases are not subject to locks via AV scanning etc as this can cause data corruption. {ex}", ex.Message);
                    throw new DataStoreConnectionException($"Data store write test failed: {ex.Message}", _dataStoreStatus.DataStoreId, _dataStoreStatus.DataStoreType);
                }

                var isInitialised = await _itemManager.IsInitialised();
                if (!isInitialised)
                {
                    var msg = $"Managed Item Store is not initialised.";
                    _serviceLog?.Error(msg);
                    throw new DataStoreConnectionException(msg, _dataStoreStatus.DataStoreId, _dataStoreStatus.DataStoreType);
                }

                // Data store connected successfully
                _dataStoreStatus.IsConnected = true;
                _dataStoreStatus.IsDegradedMode = false;
                _dataStoreStatus.StatusMessage = "Data store connected and operational.";
                _dataStoreStatus.LastSuccessfulConnection = DateTimeOffset.UtcNow;
                _dataStoreStatus.ConsecutiveFailures = 0;
            }
            catch (DataStoreConnectionException dsEx)
            {
                HandleDataStoreFailure(dsEx.Message, dsEx.DataStoreId, dsEx.DataStoreType);
                throw;
            }
            catch (Exception exp)
            {
                var msg = $"Failed to open or upgrade the managed items data store. :: {exp}";
                _serviceLog?.Error(msg);
                HandleDataStoreFailure(msg, _dataStoreStatus.DataStoreId, _dataStoreStatus.DataStoreType);
                throw new DataStoreConnectionException(msg, _dataStoreStatus.DataStoreId, _dataStoreStatus.DataStoreType);
            }
        }

        /// <summary>
        /// Initialize the service in degraded mode when data store connection fails
        /// </summary>
        private async Task InitDataStoreDegradedMode(string errorMessage, string? dataStoreId, string? dataStoreType)
        {

            _serviceLog?.Warning("Initializing service in DEGRADED MODE due to data store failure.");

            _dataStoreStatus = new DataStoreStatus
            {
                IsConnected = false,
                IsDegradedMode = true,
                StatusMessage = $"Service running in degraded mode. Data store unavailable: {errorMessage}",
                DataStoreId = dataStoreId,
                DataStoreType = dataStoreType,
                LastErrorTime = DateTimeOffset.UtcNow,
                LastErrorMessage = errorMessage,
                ConsecutiveFailures = 1
            };

            // Set item manager and credentials manager to null - all operations will fail gracefully
            _itemManager = null;
            _credentialsManager = null;
            _configStore = null;

            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_DATASTORE_STATUS,
                title: "Data Store Status",
                description: $"DEGRADED MODE: {errorMessage}",
                hasError: true
            );

            await Task.CompletedTask;
        }

        /// <summary>
        /// Handle data store connection failure
        /// </summary>
        private void HandleDataStoreFailure(string errorMessage, string? dataStoreId, string? dataStoreType)
        {
            _dataStoreStatus.IsConnected = false;
            _dataStoreStatus.IsDegradedMode = true;
            _dataStoreStatus.StatusMessage = $"Data store connection failed: {errorMessage}";
            _dataStoreStatus.DataStoreId = dataStoreId;
            _dataStoreStatus.DataStoreType = dataStoreType;
            _dataStoreStatus.LastErrorTime = DateTimeOffset.UtcNow;
            _dataStoreStatus.LastErrorMessage = errorMessage;
            _dataStoreStatus.ConsecutiveFailures++;

            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_DATASTORE_STATUS,
                title: "Data Store Status",
                description: $"Connection failed: {errorMessage}",
                hasError: true
            );
        }

        /// <summary>
        /// Attempt to reconnect to the data store after a failure
        /// </summary>
        public async Task<ActionResult> AttemptDataStoreReconnection()
        {
            if (!_dataStoreStatus.IsDegradedMode)
            {
                return new ActionResult("Data store is already connected.", true);
            }

            _serviceLog?.Information("Attempting to reconnect to data store...");

            try
            {
                await InitDataStore();

                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_DATASTORE_STATUS,
                    title: "Data Store Status",
                    description: "Data store reconnected successfully.",
                    hasError: false
                );

                return new ActionResult("Data store reconnected successfully.", true);
            }
            catch (Exception ex)
            {
                var msg = $"Failed to reconnect to data store: {ex.Message}";
                _serviceLog?.Error(ex, msg);
                return new ActionResult(msg, false);
            }
        }

        /// <summary>
        /// Check if data store operations are available
        /// </summary>
        private void EnsureDataStoreAvailable()
        {
            if (_dataStoreStatus.IsDegradedMode || _itemManager == null)
            {
                throw new InvalidOperationException(
                    $"Data store is not available. Service is running in degraded mode. " +
                    $"Error: {_dataStoreStatus.LastErrorMessage ?? "Unknown error"}. " +
                    $"Please resolve the database connection issue and restart the service or attempt reconnection.");
            }
        }

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
                                pr.Init(dataStore.ConnectionConfig, _serviceLog, CoreAppSettings.Current.InstanceId);
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
                                pr.Init(string.Empty, _serviceLog);
                            }
                            else
                            {
                                pr.Init(dataStore.ConnectionConfig, _serviceLog, CoreAppSettings.Current.InstanceId);
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

        private IConfigurationStore CreateExternalConfigurationStore(string typeId, string connectionConfig, string instanceId)
        {
            var assemblies = new HashSet<Assembly>();

            if (_pluginManager?.ManagedItemStoreProviders != null)
            {
                foreach (var provider in _pluginManager.ManagedItemStoreProviders)
                {
                    assemblies.Add(provider.GetType().Assembly);
                }
            }

            if (_pluginManager?.CredentialStoreProviders != null)
            {
                foreach (var provider in _pluginManager.CredentialStoreProviders)
                {
                    assemblies.Add(provider.GetType().Assembly);
                }
            }

            foreach (var assembly in assemblies)
            {
                IConfigurationStore store = TryCreateConfigurationStoreFromAssembly(assembly, typeId, connectionConfig, instanceId);
                if (store != null)
                {
                    return store;
                }
            }

            return null;
        }

        private IConfigurationStore TryCreateConfigurationStoreFromAssembly(Assembly assembly, string typeId, string connectionConfig, string instanceId)
        {
            try
            {
                var candidates = assembly.GetTypes()
                    .Where(t => typeof(IConfigurationStore).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

                foreach (var candidate in candidates)
                {
                    var definitionProperty = candidate.GetProperty("Definition", BindingFlags.Public | BindingFlags.Static);
                    if (definitionProperty == null)
                    {
                        continue;
                    }

                    if (definitionProperty.GetValue(null) is ProviderDefinition definition && definition.ProviderCategoryId == typeId)
                    {
                        var constructor = candidate.GetConstructor(new[] { typeof(string), typeof(ILog), typeof(string) });
                        if (constructor != null)
                        {
                            return (IConfigurationStore)constructor.Invoke(new object[] { connectionConfig, _serviceLog, instanceId });
                        }

                        var instance = (IConfigurationStore)Activator.CreateInstance(candidate);
                        if (instance != null)
                        {
                            var initMethod = candidate.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(ILog), typeof(string) }, null);
                            initMethod?.Invoke(instance, new object[] { connectionConfig, _serviceLog, instanceId });
                            return instance;
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                _serviceLog?.Error(ex, $"Failed to inspect configuration store types in assembly {assembly.FullName}.");
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, $"Failed to create configuration store from assembly {assembly.FullName}.");
            }

            return null;
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

        public async Task<bool> SelectConfigurationStore(string dataStoreId)
        {
            var dataStore = await GetDataStore(dataStoreId);

            if (dataStore == null)
            {
                _serviceLog.Error($"Could not match data store connection information to the specified store id: {dataStoreId}");
                return false;
            }

            if (dataStore.TypeId == "sqlite")
            {
                _configStore = new SQLiteConfigurationStore("", _serviceLog);
            }
            else
            {
                _configStore = CreateExternalConfigurationStore(dataStore.TypeId, dataStore.ConnectionConfig, CoreAppSettings.Current.InstanceId);
            }

            if (_configStore == null)
            {
                _serviceLog.Error($"Unsupported configuration store type {dataStore.TypeId}");
                return false;
            }

            _accessControl = new AccessControl(_serviceLog, _configStore);

            if (!await _configStore.IsInitialised())
            {
                _serviceLog.Error($"Configuration store failed to initialise {dataStore.Id} : {dataStore.Title}");
                return false;
            }

            return true;
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

            return await Task.FromResult(allProviders.OrderBy(p => p.Title).ToList());
        }

        public async Task<List<DataStoreConnection>> GetDataStores()
        {
            var dataStores = new List<DataStoreConnection>();

            var appDataPath = EnvironmentUtil.EnsuredAppDataPath();
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

            return await Task.FromResult(dataStores.OrderBy(t => t.Title).ToList());
        }
        public async Task<List<ActionStep>> CopyDateStoreToTarget(string sourceId, string destId)
        {

            // connect to source and dest, copy all data to target via the configuration store
            // which now contains all item types (managed certificates, credentials, and other configuration items)
            var results = new List<ActionStep>();

            var sourceConfigStore = await GetConfigurationStoreProvider(await GetDataStore(sourceId));
            var destConfigStore = await GetConfigurationStoreProvider(await GetDataStore(destId));

            if (sourceConfigStore == null || !await sourceConfigStore.IsInitialised())
            {
                results.Add(new ActionStep { HasError = true, Title = "Source Data Store", Description = "Failed to initialise the source data store." });
                return results;
            }

            if (destConfigStore == null || !await destConfigStore.IsInitialised())
            {
                results.Add(new ActionStep { HasError = true, Title = "Destination Data Store", Description = "Failed to initialise the target data store." });
                return results;
            }

            // copy all items from source to destination (managed certificates, credentials with protected secrets, and configuration items)
            var allItems = await sourceConfigStore.GetAllSerializedItems();

            var managedCertCount = 0;
            var credentialCount = 0;
            var configItemCount = 0;

            foreach (var item in allItems)
            {
                try
                {
                    await destConfigStore.UpsertSerializedItem(item);

                    if (item.ItemType == "managedcertificate")
                    {
                        managedCertCount++;
                    }
                    else if (item.ItemType == "credential")
                    {
                        credentialCount++;
                    }
                    else
                    {
                        configItemCount++;
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new ActionStep { HasWarning = true, Description = $"Could not copy item {item.Id} [{item.ItemType}]: {ex.Message}" });
                }
            }

            results.Add(new ActionStep { Title = "Copied managed certificates", Description = $"{managedCertCount} managed certificates copied to target." });
            results.Add(new ActionStep { Title = "Copied credentials", Description = $"{credentialCount} credentials copied to target." });
            results.Add(new ActionStep { Title = "Copied configuration items", Description = $"{configItemCount} configuration items copied to target." });

            return results;
        }

        private async Task<IConfigurationStore> GetConfigurationStoreProvider(DataStoreConnection dataStore)
        {
            if (dataStore == null)
            {
                return null;
            }

            if (dataStore.TypeId == "sqlite")
            {
                return new SQLiteConfigurationStore("", _serviceLog);
            }

            return CreateExternalConfigurationStore(dataStore.TypeId, dataStore.ConnectionConfig, CoreAppSettings.Current.InstanceId);
        }

        public async Task<List<ActionStep>> TestDataStoreConnection(DataStoreConnection dataStore)
        {
            // connect to data store and check schema
            var results = new List<ActionStep>();

            var dataStoreAvailable = false;
            var errorDetail = "";
            try
            {
                var itemProvider = await GetManagedItemStoreProvider(dataStore);
                var credProvider = await GetCredentialManagerProvider(dataStore);

                if (itemProvider != null && credProvider != null)
                {
                    dataStoreAvailable = true;
                }
            }
            catch (Exception exp)
            {
                dataStoreAvailable = false;
                errorDetail = exp.Message;
            }

            if (!dataStoreAvailable)
            {
                results.Add(new ActionStep
                {
                    Title = "Data Store Init Failed",
                    Description = $"The data store failed to connect. Verify the connection string is correct and the required connectivity, schema and permissions are present. [{errorDetail}]",
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
            var appDataPath = EnvironmentUtil.EnsuredAppDataPath();
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
                var appDataPath = EnvironmentUtil.EnsuredAppDataPath();
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
