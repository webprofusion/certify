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

        /// <summary>
        /// How long service startup keeps retrying the data store connection before treating it as fatal. This
        /// covers a data store which is still coming up alongside the service, such as a database container
        /// starting at the same time.
        /// </summary>
        private static readonly TimeSpan _dataStoreInitRetryDuration = TimeSpan.FromSeconds(60);

        /// <summary>
        /// How long to wait between data store connection attempts during service startup.
        /// </summary>
        private static readonly TimeSpan _dataStoreInitRetryInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Connect to the data store during service startup, retrying for up to
        /// <see cref="_dataStoreInitRetryDuration"/> and logging each failed attempt. The service cannot do
        /// anything useful without its data store, so when the retries are exhausted this throws and the service
        /// stops rather than running on in a degraded state where certificates silently stop being renewed.
        /// </summary>
        private async Task InitDataStoreWithRetry()
        {
            var startedAt = DateTimeOffset.UtcNow;
            var attempt = 0;
            Exception lastException;

            while (true)
            {
                attempt++;

                try
                {
                    await InitDataStore();

                    if (attempt > 1)
                    {
                        _serviceLog?.Information($"Data store connected on attempt {attempt} after {(DateTimeOffset.UtcNow - startedAt).TotalSeconds:0.#}s.");
                    }

                    return;
                }
                catch (Exception exp)
                {
                    lastException = exp;

                    var elapsed = DateTimeOffset.UtcNow - startedAt;
                    var remaining = _dataStoreInitRetryDuration - elapsed;

                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    // the retry budget is wall clock time, so a slow failing attempt uses up part of it
                    var delay = remaining < _dataStoreInitRetryInterval ? remaining : _dataStoreInitRetryInterval;

                    _serviceLog?.Warning($"Data store connection attempt {attempt} failed after {elapsed.TotalSeconds:0.#}s, retrying in {delay.TotalSeconds:0.#}s. [{exp.Message}]");

                    await Task.Delay(delay);
                }
            }

            var msg = $"Data store connection failed after {attempt} attempts over {_dataStoreInitRetryDuration.TotalSeconds:0}s. The service cannot run without its data store and is stopping. Check the data store is reachable and that the connection configuration, schema and permissions are correct, then restart the service. [{lastException.Message}]";

            _serviceLog?.Error(lastException, msg);

            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_DATASTORE_STATUS,
                title: "Data Store Status",
                description: msg,
                hasError: true
            );

            await ReportDiagnosticActionRequired(
                SystemStatusKeys.SERVICE_CORE_DATASTORE_STATUS,
                "Data Store Unavailable",
                msg,
                isServiceStopping: true);

            throw new DataStoreConnectionException(msg, _dataStoreStatus.DataStoreId, _dataStoreStatus.DataStoreType);
        }

        /// <summary>
        /// Send an operator facing diagnostic to connected clients where status reporting is wired up. Failing to
        /// report must never replace the condition being reported, so any error here is logged and swallowed.
        /// </summary>
        private async Task ReportDiagnosticActionRequired(string key, string title, string description, bool isServiceStopping)
        {
            if (_statusReporting == null)
            {
                return;
            }

            try
            {
                await _statusReporting.ReportDiagnosticActionRequired(new DiagnosticActionRequired
                {
                    Key = key,
                    Title = title,
                    Description = description,
                    IsServiceStopping = isServiceStopping
                });
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Failed to send diagnostic notification to connected clients :: {exp.Message}");
            }
        }

        /// <summary>
        /// Check the data store can be written to, by storing and removing a throwaway item. Returns the failure when it
        /// cannot, or null when it can
        /// </summary>
        /// <returns></returns>
        private async Task<Exception?> GetDataStoreWriteFailure()
        {
            try
            {
                var item = new ManagedCertificate { Id = $"writecheck_{Guid.NewGuid()}" };

                await _itemManager.Update(item);
                await _itemManager.Delete(item);

                return null;
            }
            catch (Exception exp)
            {
                return exp;
            }
        }

        private async Task InitDataStore()
        {
            var enableExtendedDataStores = true;

            // the failure count survives the reset, so repeated reconnection attempts are counted rather than each
            // attempt reporting a first failure
            _dataStoreStatus = new DataStoreStatus { ConsecutiveFailures = _dataStoreStatus?.ConsecutiveFailures ?? 0 };

            try
            {
                if (enableExtendedDataStores)
                {
                    await UpgradeDataStoreConfigProtection();

                    var defaultStoreId = CoreAppSettings.Current.ConfigDataStoreConnectionId;
                    var dataStoreInfo = await GetDataStore(defaultStoreId);

                    _dataStoreStatus.DataStoreId = defaultStoreId;
                    _dataStoreStatus.DataStoreType = dataStoreInfo?.TypeId;

                    if (IsBuiltInDefaultDataStoreId(defaultStoreId))
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
                var writeFailure = await GetDataStoreWriteFailure();

                if (writeFailure != null)
                {
                    _serviceLog?.Error(writeFailure, $"Data store write failed. Check connection and data integrity. Ensure file based databases are not subject to locks via AV scanning etc as this can cause data corruption. {writeFailure}", writeFailure.Message);
                    throw new DataStoreConnectionException($"Data store write test failed: {writeFailure.Message}", _dataStoreStatus.DataStoreId, _dataStoreStatus.DataStoreType);
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

        /// <summary>
        /// True when the given id refers to the built in SQLite store rather than a stored data store connection.
        /// The stored connection list only contains an entry for it when no other connections have been added.
        /// </summary>
        private static bool IsBuiltInDefaultDataStoreId(string dataStoreId) =>
            string.IsNullOrEmpty(dataStoreId) || dataStoreId == "(default)" || dataStoreId == "0";

        /// <summary>
        /// Resolve the connection details for a data store id, falling back to the built in SQLite store so that
        /// the service can be switched back to it once other connections have been added.
        /// </summary>
        private async Task<DataStoreConnection> ResolveDataStoreConnection(string dataStoreId)
        {
            var dataStore = await GetDataStore(dataStoreId);

            if (dataStore == null && IsBuiltInDefaultDataStoreId(dataStoreId))
            {
                dataStore = new DataStoreConnection
                {
                    Id = string.IsNullOrEmpty(dataStoreId) ? "(default)" : dataStoreId,
                    Title = "(Default SQLite)",
                    TypeId = "sqlite",
                    ConnectionConfig = ""
                };
            }

            return dataStore;
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
            var dataStore = await ResolveDataStoreConnection(dataStoreId);

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
            var dataStore = await ResolveDataStoreConnection(dataStoreId);

            if (dataStore == null)
            {
                _serviceLog.Error($"Could not match data store connection information to the specified store id: {dataStoreId}");
                return false;
            }

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
            var dataStore = await ResolveDataStoreConnection(dataStoreId);

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
            var dataStores = await GetDataStoresInternal();
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

        /// <summary>
        /// Get the configured data store connections, with the connection configuration masked so that database
        /// credentials are not exposed to clients. A masked connection can be sent back to UpdateDataStoreConnection
        /// unchanged, which leaves the stored connection details as they are.
        /// </summary>
        public async Task<List<DataStoreConnection>> GetDataStores()
        {
            var dataStores = await GetDataStoresInternal();

            // the service setting is the source of truth for which store is in use, so the flag is reported from
            // there rather than from the stored connection, which is only updated when a connection is saved
            var currentStoreId = CoreAppSettings.Current.ConfigDataStoreConnectionId;

            return dataStores.Select(d =>
            {
                var maskedConfig = DataStoreConnectionProtection.Mask(d.ConnectionConfig);

                return new DataStoreConnection
                {
                    Id = d.Id,
                    Title = d.Title,
                    TypeId = d.TypeId,
                    ConnectionConfig = maskedConfig,
                    IsDefault = d.Id == currentStoreId || (IsBuiltInDefaultDataStoreId(currentStoreId) && IsBuiltInDefaultDataStoreId(d.Id)),
                    IsProtected = DataStoreConnectionProtection.IsMasked(maskedConfig)
                };
            }).ToList();
        }

        /// <summary>
        /// Get the configured data store connections including their real connection configuration, for service
        /// use. The result contains database credentials and must not be returned to a client.
        /// </summary>
        private async Task<List<DataStoreConnection>> GetDataStoresInternal()
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

                foreach (var dataStore in dataStores)
                {
                    dataStore.ConnectionConfig = DataStoreConnectionProtection.Unprotect(dataStore.ConnectionConfig, _serviceLog);
                }
            }
            else
            {
                // return a default data store for sqlite
                dataStores.Add(new DataStoreConnection { Id = "(default)", Title = "(Default SQLite)", TypeId = "sqlite" });
            }

            return await Task.FromResult(dataStores.OrderBy(t => t.Title).ToList());
        }

        /// <summary>
        /// Write the data store connection list to disk, encrypting each connection configuration.
        /// </summary>
        private void PersistDataStores(List<DataStoreConnection> dataStores)
        {
            if (dataStores.Any(d => DataStoreConnectionProtection.IsMasked(d.ConnectionConfig)))
            {
                // a masked config has had its secrets stripped, so saving it would discard the real connection
                // details. Callers must resolve masked values before saving
                throw new InvalidOperationException("Data store connection configuration is masked and cannot be saved.");
            }

            var appDataPath = EnvironmentUtil.EnsuredAppDataPath();
            var path = Path.Combine(appDataPath, "datastores.json");

            var protectedDataStores = dataStores.Select(d => new DataStoreConnection
            {
                Id = d.Id,
                Title = d.Title,
                TypeId = d.TypeId,
                ConnectionConfig = DataStoreConnectionProtection.Protect(d.ConnectionConfig, _serviceLog),
                IsDefault = d.IsDefault
            }).ToList();

            lock (_dataStoreLocker)
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(protectedDataStores);
                System.IO.File.WriteAllText(path, json);
            }
        }

        /// <summary>
        /// Clients receive connection configuration with the secrets masked, so that a connection can be managed
        /// without exposing database credentials. A masked value sent back to the service means the stored
        /// connection details should be kept as they are, so restore the real value before the connection is used.
        /// </summary>
        private async Task ResolveProtectedConnectionConfig(DataStoreConnection dataStore)
        {
            if (dataStore == null || !DataStoreConnectionProtection.IsMasked(dataStore.ConnectionConfig))
            {
                return;
            }

            var stored = (await GetDataStoresInternal()).FirstOrDefault(d => d.Id == dataStore.Id);

            if (stored != null)
            {
                dataStore.ConnectionConfig = stored.ConnectionConfig;
            }
            else
            {
                _serviceLog?.Warning($"Data store {dataStore.Id} was submitted with masked connection configuration but there is no stored connection to restore it from.");
            }

            dataStore.IsProtected = false;
        }

        /// <summary>
        /// Encrypt any data store connection configuration still stored as cleartext by a previous version. This
        /// is best effort - failing to upgrade must not prevent the service connecting to its data store.
        /// </summary>
        private async Task UpgradeDataStoreConfigProtection()
        {
            try
            {
                var appDataPath = EnvironmentUtil.EnsuredAppDataPath();
                var path = Path.Combine(appDataPath, "datastores.json");

                if (!System.IO.File.Exists(path))
                {
                    return;
                }

                List<DataStoreConnection> storedDataStores;

                lock (_dataStoreLocker)
                {
                    var configData = System.IO.File.ReadAllText(path);
                    storedDataStores = Newtonsoft.Json.JsonConvert.DeserializeObject<List<DataStoreConnection>>(configData);
                }

                var hasClearTextConfig = storedDataStores?.Any(d => !string.IsNullOrEmpty(d.ConnectionConfig) && !DataStoreConnectionProtection.IsProtectedValue(d.ConnectionConfig)) == true;

                if (!hasClearTextConfig)
                {
                    return;
                }

                PersistDataStores(await GetDataStoresInternal());

                _serviceLog?.Information("Upgraded data store connection configuration to encrypted storage.");
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Failed to upgrade data store connection configuration to encrypted storage :: {exp.Message}");
            }
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

        /// <summary>
        /// Locate the schema provider for a data store type. The provider is returned uninitialised so that the
        /// schema can be inspected or created before any attempt is made to read or write data.
        /// </summary>
        private IDataStoreSchemaProvider GetDataStoreSchemaProvider(DataStoreConnection dataStore)
        {
            if (dataStore == null)
            {
                return null;
            }

            foreach (var p in _pluginManager.ManagedItemStoreProviders)
            {
                foreach (var provider in p.GetProviders(p.GetType()))
                {
                    if (provider.ProviderCategoryId == dataStore.TypeId)
                    {
                        if (p.GetProvider(p.GetType(), provider.Id) is IDataStoreSchemaProvider schemaProvider)
                        {
                            return schemaProvider;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Inspect the schema of a data store without modifying it, reporting whether migrations are outstanding
        /// and whether the credentials on this connection are able to apply them.
        /// </summary>
        public async Task<DataStoreSchemaCheckResult> CheckDataStoreSchema(DataStoreConnection dataStore)
        {
            if (dataStore == null)
            {
                return new DataStoreSchemaCheckResult { State = DataStoreSchemaState.Unknown, Message = "No data store was specified." };
            }

            await ResolveProtectedConnectionConfig(dataStore);

            var schemaProvider = GetDataStoreSchemaProvider(dataStore);

            if (schemaProvider == null)
            {
                // stores such as sqlite manage their own schema and have nothing to apply
                return new DataStoreSchemaCheckResult
                {
                    State = DataStoreSchemaState.Current,
                    Message = $"The {dataStore.TypeId} data store does not require managed schema migrations."
                };
            }

            return await schemaProvider.CheckSchema(dataStore.ConnectionConfig, _serviceLog);
        }

        /// <summary>
        /// Apply outstanding schema migrations to a data store, creating the schema if it is not present. The
        /// credentials on the given data store connection must have schema modification rights - where the
        /// runtime database user is restricted to reading and writing data, add a separate data store connection
        /// pointing at the same database using credentials which can modify the schema, and apply migrations
        /// using that connection.
        /// </summary>
        public async Task<List<ActionStep>> ApplyDataStoreSchemaMigrations(DataStoreConnection dataStore)
        {
            var results = new List<ActionStep>();

            if (dataStore == null)
            {
                results.Add(new ActionStep
                {
                    Key = DataStoreActionKeys.ApplyMigrations,
                    Title = "Apply Migrations",
                    Description = "No data store was specified.",
                    HasError = true
                });
                return results;
            }

            await ResolveProtectedConnectionConfig(dataStore);

            var schemaProvider = GetDataStoreSchemaProvider(dataStore);

            if (schemaProvider == null)
            {
                results.Add(new ActionStep
                {
                    Key = DataStoreActionKeys.ApplyMigrations,
                    Title = "Apply Migrations",
                    Description = $"The {dataStore.TypeId} data store does not require managed schema migrations."
                });
                return results;
            }

            var check = await schemaProvider.CheckSchema(dataStore.ConnectionConfig, _serviceLog);

            if (check.State == DataStoreSchemaState.Unknown)
            {
                results.Add(new ActionStep
                {
                    Key = DataStoreActionKeys.ApplyMigrations,
                    Title = "Apply Migrations",
                    Description = $"The schema could not be inspected. {check.Message}",
                    HasError = true
                });
                return results;
            }

            if (!check.IsMigrationRequired && !check.HasOptionalMigrations)
            {
                results.Add(new ActionStep
                {
                    Key = DataStoreActionKeys.ApplyMigrations,
                    Title = "Apply Migrations",
                    Description = "The schema is already up to date, no migrations were applied."
                });
                return results;
            }

            if (!check.CanApplySchemaChanges)
            {
                results.Add(new ActionStep
                {
                    Key = DataStoreActionKeys.ApplyMigrations,
                    Title = "Apply Migrations",
                    Description = "This connection does not have schema modification rights. Add a data store connection for the same database using credentials which can modify the schema, then apply migrations using that connection.",
                    HasError = true
                });
                return results;
            }

            // this is an explicit operator action, so optional structural steps are applied as well - they are
            // never applied unattended on connection
            var applyResult = await schemaProvider.ApplySchemaMigrations(dataStore.ConnectionConfig, _serviceLog, includeOptional: true);

            results.Add(new ActionStep
            {
                Key = DataStoreActionKeys.ApplyMigrations,
                Title = "Apply Migrations",
                Description = applyResult.Message,
                HasError = !applyResult.IsSuccess,
                Substeps = applyResult.Result?.Select(m => new ActionStep { Title = m.Id, Description = m.Description }).ToList()
            });

            if (applyResult.IsSuccess)
            {
                _serviceLog?.Information($"Applied schema migrations to data store {dataStore.Id}.");
            }

            return results;
        }

        public async Task<List<ActionStep>> TestDataStoreConnection(DataStoreConnection dataStore)
        {
            // connect to data store and check schema
            var results = new List<ActionStep>();

            await ResolveProtectedConnectionConfig(dataStore);

            // inspect the schema first - an empty or out of date database cannot be connected to for normal use,
            // so reporting that a migration is needed is more useful than a generic connection failure
            var schemaCheck = await CheckDataStoreSchema(dataStore);

            if (schemaCheck.State == DataStoreSchemaState.Unknown)
            {
                results.Add(new ActionStep
                {
                    Key = DataStoreActionKeys.ConnectionFailed,
                    Title = "Data Store Connection Failed",
                    Description = $"The data store could not be reached. Verify the connection string is correct and the required connectivity and permissions are present. [{schemaCheck.Message}]",
                    HasError = true
                });

                return results;
            }

            // only a genuinely required migration stops the store being usable. An optional upgrade is reported
            // further down, after the connection has been tested as normal
            if (schemaCheck.IsMigrationRequired)
            {
                var canApply = schemaCheck.CanApplySchemaChanges;

                results.Add(new ActionStep
                {
                    Key = DataStoreActionKeys.SchemaMigrationRequired,
                    Title = "Schema Migration Required",
                    Description = canApply
                        ? $"{schemaCheck.Message} These credentials can apply them - use Apply Migrations to continue."
                        : $"{schemaCheck.Message} These credentials cannot modify the schema. Add a data store connection for the same database using credentials which can, then apply migrations using that connection.",
                    HasError = !canApply,
                    HasWarning = canApply,
                    Substeps = schemaCheck.RequiredMigrations.Select(m => new ActionStep { Title = m.Id, Description = m.Description }).ToList()
                });

                return results;
            }

            var dataStoreAvailable = false;
            var errorDetail = "";
            try
            {
                var itemProvider = await GetManagedItemStoreProvider(dataStore);
                var credProvider = await GetCredentialManagerProvider(dataStore);

                // the configuration store holds settings, access control and hub state, so a store which cannot
                // provide one is not usable as the service default even if items and credentials connect
                var configProvider = await GetConfigurationStoreProvider(dataStore);

                if (itemProvider != null && credProvider != null && configProvider != null && await configProvider.IsInitialised())
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
                    Key = DataStoreActionKeys.InitFailed,
                    Title = "Data Store Init Failed",
                    Description = $"The data store failed to connect. Verify the connection string is correct and the required connectivity, schema and permissions are present. [{errorDetail}]",
                    HasError = true
                });
            }
            else
            {
                results.Add(new ActionStep
                {
                    Key = DataStoreActionKeys.ConnectionOK,
                    Title = "Data Store Connection OK",
                    Description = "Connected successfully and the schema is up to date."
                });
            }

            // a recommended upgrade is information, never a failure - an existing installation is free to carry
            // on without it
            if (schemaCheck.HasOptionalMigrations)
            {
                var canApply = schemaCheck.CanApplySchemaChanges;

                results.Add(new ActionStep
                {
                    Key = DataStoreActionKeys.SchemaUpgradeAvailable,
                    Title = "Optional Schema Upgrade Available",
                    Description = canApply
                        ? "This data store works as it is. An optional schema upgrade is available - use Apply Migrations to apply it."
                        : "This data store works as it is. An optional schema upgrade is available, but these credentials cannot modify the schema. Apply it using a data store connection with schema modification rights.",
                    // not an error - the store is usable either way - but flag when these credentials could not
                    // apply it, so the UI does not offer an action which would fail
                    HasWarning = !canApply,
                    Substeps = schemaCheck.OptionalMigrations
                        .Select(m => new ActionStep
                        {
                            Title = m.Id,
                            Description = string.IsNullOrEmpty(m.OptionalReason) ? m.Description : $"{m.Description} {m.OptionalReason}"
                        })
                        .ToList()
                });
            }

            return results;
        }

        public async Task<List<ActionStep>> SetDefaultDataStore(string dataStoreId)
        {
            var store = await ResolveDataStoreConnection(dataStoreId);

            if (store == null)
            {
                return new List<ActionStep>
                {
                    new ActionStep
                    {
                        Key = DataStoreActionKeys.ConnectionFailed,
                        Title = "Data Store Not Found",
                        Description = $"There is no data store connection configured with the id '{dataStoreId}'.",
                        HasError = true
                    }
                };
            }

            // test connection before switching
            var testResults = await TestDataStoreConnection(store);

            if (testResults.Any(t => t.HasError))
            {
                return testResults;
            }

            SettingsManager.LoadAppSettings();

            var previousDataStoreId = CoreAppSettings.Current.ConfigDataStoreConnectionId;

            CoreAppSettings.Current.ConfigDataStoreConnectionId = dataStoreId;
            SettingsManager.SaveAppSettings();

            try
            {
                // re-run the full data store initialisation rather than selecting individual stores, so that
                // managed items, credentials and configuration (and therefore access control) all move to the new
                // store together, and the reported connection status matches what the service is connected to
                await InitDataStore();
            }
            catch (Exception exp)
            {
                _serviceLog?.Error(exp, $"Failed to switch to data store {dataStoreId}, reverting to {previousDataStoreId}. {exp.Message}");

                var revertOutcome = await RevertToDataStore(previousDataStoreId);

                return new List<ActionStep>
                {
                    new ActionStep
                    {
                        Key = DataStoreActionKeys.SwitchFailed,
                        Title = "Data Store Switch Failed",
                        Description = $"The service could not connect to '{store.Title}' ({dataStoreId}) and the default data store was not changed. [{exp.Message}] {revertOutcome}",
                        HasError = true
                    }
                };
            }

            await OnDataStoreChanged();

            _serviceLog?.Information($"Default data store changed to {dataStoreId} '{store.Title}' [{store.TypeId}].");

            return new List<ActionStep>
            {
                new ActionStep
                {
                    Key = DataStoreActionKeys.SwitchOK,
                    Title = "Changed Default Data Store",
                    Description = $"The service is now using data store '{store.Title}' ({dataStoreId})."
                }
            };
        }

        /// <summary>
        /// Restore a previous default data store after a failed switch, so that the service carries on using the
        /// store it was already connected to. Returns a description of the outcome for the operator, as this runs
        /// while a switch failure is already being reported.
        /// </summary>
        private async Task<string> RevertToDataStore(string previousDataStoreId)
        {
            try
            {
                SettingsManager.LoadAppSettings();
                CoreAppSettings.Current.ConfigDataStoreConnectionId = previousDataStoreId;
                SettingsManager.SaveAppSettings();

                await InitDataStore();

                await OnDataStoreChanged();

                _serviceLog?.Information($"Reverted to the previous data store {previousDataStoreId}.");

                return $"The service has reverted to the previous data store '{previousDataStoreId}'.";
            }
            catch (Exception exp)
            {
                _serviceLog?.Error(exp, $"Failed to revert to the previous data store {previousDataStoreId}. {exp.Message}");

                // neither store can be used, so drop the store references rather than leaving the service holding
                // a partially connected set of stores
                await InitDataStoreDegradedMode(exp.Message, previousDataStoreId, _dataStoreStatus.DataStoreType);

                return $"The service could also not reconnect to the previous data store '{previousDataStoreId}' and is now running in degraded mode. [{exp.Message}]";
            }
        }

        /// <summary>
        /// Refresh the state derived from the data store once the service has connected to a different one, so
        /// that values cached from the previous store are not reused and the new store has the content the
        /// service expects to be present.
        /// </summary>
        private async Task OnDataStoreChanged()
        {
            // each of these is loaded on demand and repopulated by the next read, which now goes to the store the
            // service has just connected to, so dropping them is all that is required - see GetHubSettings,
            // GetAccountDetails and GetACMEProvider
            _cachedHubSettings = null;

            lock (_accountsLock)
            {
                _accounts = null;
            }

            // ACME providers are initialised with account details read from the previous credential store
            _acmeClientProviders.Clear();

            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_DATASTORE_STATUS,
                title: "Data Store Status",
                description: "Data store connected and operational.",
                hasError: false
            );

            // startup only initialisation which the newly selected store may not have had applied to it
            if (!IsInDegradedMode && (_isMgtmHubBackend || _isDirectMgmtHubBackend))
            {
                try
                {
                    await EnsureDefaultTagCategories();
                }
                catch (Exception exp)
                {
                    _serviceLog?.Error(exp, $"Failed to create the default tag categories in the selected data store. {exp.Message}");
                }
            }
        }

        public async Task<List<ActionStep>> UpdateDataStoreConnection(DataStoreConnection dataStore)
        {
            // the client may have sent back the masked configuration to keep the stored connection details as they are
            await ResolveProtectedConnectionConfig(dataStore);

            var testResults = await TestDataStoreConnection(dataStore);

            if (testResults.Any(t => t.HasError))
            {
                return testResults;
            }

            var dataStores = await GetDataStoresInternal();

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
            try
            {
                PersistDataStores(dataStores);
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Failed to save data store configuration :: {exp.Message}");
                testResults.Add(new ActionStep { HasError = true, Title = "Data Store Config Save Failed", Description = "Failed to store the data store configuration to disk" });
            }

            return testResults;
        }

        public async Task<List<ActionStep>> RemoveDataStoreConnection(string dataStoreId)
        {
            var results = new List<ActionStep>();

            var currentStoreId = CoreAppSettings.Current.ConfigDataStoreConnectionId;

            if (currentStoreId == dataStoreId || (IsBuiltInDefaultDataStoreId(currentStoreId) && IsBuiltInDefaultDataStoreId(dataStoreId)))
            {
                results.Add(new ActionStep("Data Store Remove Failed", "Cannot remove the data store currently in use.", true));
                return results;
            }

            var dataStores = await GetDataStoresInternal();

            var existing = dataStores.FirstOrDefault(d => d.Id == dataStoreId);
            if (existing != null)
            {
                dataStores.Remove(existing);

                //save
                try
                {
                    PersistDataStores(dataStores);
                }
                catch (Exception exp)
                {
                    _serviceLog?.Error($"Failed to save data store configuration :: {exp.Message}");
                    results.Add(new ActionStep("Failed to Save Data Stores Config", "The data store configuration could not be saved to disk", true));
                }
            }

            return results;
        }
    }
}
