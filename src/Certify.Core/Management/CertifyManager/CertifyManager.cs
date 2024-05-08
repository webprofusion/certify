using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Certify.Core.Management;
using Certify.Core.Management.Access;
using Certify.Core.Management.Challenges;
using Certify.Datastore.SQLite;
using Certify.Models;
using Certify.Models.Config.Migration;
using Certify.Models.Providers;
using Certify.Providers;
using Certify.Providers.ACME.Anvil;
using Serilog;

namespace Certify.Management
{
    public partial class CertifyManager : ICertifyManager, IDisposable
    {
        /// <summary>
        /// Storage service for managed certificates
        /// </summary>
        private IManagedItemStore _itemManager = null;

        /// <summary>
		/// Server targets for this service (e.g. local IIS, nginx etc)
        /// </summary>
		private List<ITargetWebServer> _serverProviders = new List<ITargetWebServer>();

        /// <summary>
        /// Provider for general challenge responses
        /// </summary>
        private ChallengeResponseService _challengeResponseService = null;

        /// <summary>
        /// Service to load and use available plugins (deployment tasks etc)
        /// </summary>
        private PluginManager _pluginManager { get; set; }

        /// <summary>
        /// Stored Credentials service
        /// </summary>
        private ICredentialsManager _credentialsManager { get; set; }

        /// <summary>
        /// Application Insights logging
        /// </summary>
        private TelemetryManager _tc = null;

        /// <summary>
        /// Service (text file) logging
        /// </summary>
        private ILog _serviceLog { get; set; }

        /// <summary>
        /// Current service log level setting
        /// </summary>
        private Serilog.Core.LoggingLevelSwitch _loggingLevelSwitch { get; set; }

        /// <summary>
        /// If true, http challenge service is started
        /// </summary>
        private bool _httpChallengeServerAvailable = false;

        /// <summary>
        /// Set of ACME clients, one per ACME account
        /// </summary>
        private ConcurrentDictionary<string, IACMEClientProvider> _acmeClientProviders = new ConcurrentDictionary<string, IACMEClientProvider>();

        /// <summary>
        /// Cache of current known challenges and responses, used for dynamic challenge responses
        /// </summary>
        private ConcurrentDictionary<string, SimpleAuthorizationChallengeItem> _currentChallenges = new ConcurrentDictionary<string, SimpleAuthorizationChallengeItem>();

        /// <summary>
        /// Service for reporting status/progress results back to client(s)
        /// </summary>
        private IStatusReporting _statusReporting { get; set; }

        /// <summary>
        /// Set of (cached) known ACME Certificate Authorities
        /// </summary>
        private ConcurrentDictionary<string, CertificateAuthority> _certificateAuthorities = new ConcurrentDictionary<string, CertificateAuthority>();

        /// <summary>
        /// If true, we are running on Windows and can use windows specific features (cert store, IIS etc)
        /// </summary>
        private bool _useWindowsNativeFeatures = true;

        /// <summary>
        ///  Config info/preferences such as log level, challenge service config, powershell execution policy etc
        /// </summary>
        private Shared.ServiceConfig _serverConfig;

        private System.Timers.Timer _frequentTimer;
        private System.Timers.Timer _hourlyTimer;
        private System.Timers.Timer _dailyTimer;

        public CertifyManager() : this(true)
        {

        }

        public CertifyManager(bool useWindowsNativeFeatures = true)
        {
            _useWindowsNativeFeatures = useWindowsNativeFeatures;
        }

        public async Task Init()
        {
            _useWindowsNativeFeatures = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            _serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            SettingsManager.LoadAppSettings();

            InitLogging(_serverConfig);

            Util.SetSupportedTLSVersions();

            _pluginManager = new PluginManager
            {
                EnableExternalPlugins = CoreAppSettings.Current.IncludeExternalPlugins
            };

            _pluginManager.LoadPlugins(new List<string> {
                PluginManager.PLUGINS_LICENSING,
                PluginManager.PLUGINS_DASHBOARD,
                PluginManager.PLUGINS_DEPLOYMENT_TASKS,
                PluginManager.PLUGINS_CERTIFICATE_MANAGERS,
                PluginManager.PLUGINS_DNS_PROVIDERS,
                PluginManager.PLUGINS_SERVER_PROVIDERS,
                PluginManager.PLUGINS_DATASTORE_PROVIDERS
            });

            // setup supported target server types for default deployment
            if (_pluginManager.ServerProviders != null)
            {
                foreach (var p in _pluginManager.ServerProviders)
                {
                    var providers = p.GetProviders(p.GetType());
                    foreach (var provider in providers)
                    {
                        var pr = p.GetProvider(p.GetType(), provider.Id);
                        if (pr != null)
                        {
                            pr.Init(_serviceLog);
                            _serverProviders.Add(pr);
                        }
                    }
                }
            }

            // add default IIS target server provider
            var iisServerProvider = new Servers.ServerProviderIIS();
            iisServerProvider.Init(_serviceLog);
            _serverProviders.Add(iisServerProvider);

            try
            {
                await InitDataStore();
            }
            catch
            {
                var msg = "Certify Manager failed to start. Failed to load datastore";
                _serviceLog.Error(msg);
                throw (new Exception(msg));
            }

            LoadCertificateAuthorities();

            // init remaining utilities and optionally enable telematics
            _challengeResponseService = new ChallengeResponseService(CoreAppSettings.Current.EnableValidationProxyAPI);

            if (CoreAppSettings.Current.EnableAppTelematics)
            {
                _tc = new TelemetryManager(Locales.ConfigResources.AIInstrumentationKey);
            }

            _httpChallengePort = _serverConfig.HttpChallengeServerPort;
            _httpChallengeServerClient.Timeout = new TimeSpan(0, 0, 20);

            _tc?.TrackEvent("ServiceStarted");

            SetupJobs();

            await UpgradeSettings();

            _serviceLog?.Information("Certify Manager Started");
        }

        /// <summary>
        /// Setup the continuous job tasks for renewals and maintenance
        /// </summary>
        private void SetupJobs()
        {
            // 5 minute job timer (maintenance etc)
            _frequentTimer = new System.Timers.Timer(5 * 60 * 1000); // every 5 minutes
            _frequentTimer.Elapsed += _frequentTimer_Elapsed;
            _frequentTimer.Start();

            // hourly jobs timer (renewal etc)
            _hourlyTimer = new System.Timers.Timer(60 * 60 * 1000); // every 60 minutes
            _hourlyTimer.Elapsed += _hourlyTimer_Elapsed;
            _hourlyTimer.Start();

            // daily jobs timer (cleanup etc)
            _dailyTimer = new System.Timers.Timer(24 * 60 * 60 * 1000); // every 24 hrs
            _dailyTimer.Elapsed += _dailyTimer_Elapsed;
            _dailyTimer.Start();
        }

        private async void _dailyTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            await PerformDailyMaintenanceTasks();
        }

        private async void _hourlyTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            await PerformCertificateMaintenanceTasks();

            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Default);
            }
            catch
            {
                // failed to perform garbage collection, ignore.
            }
        }

        private async void _frequentTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            await PerformRenewalTasks();
        }

        private async Task PerformServiceUpgrades()
        {
            _serviceLog?.Error($"Service version has changed. Performing upgrade checks.");

            try
            {
                await PerformAccountUpgrades();
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Failed to perform ACME account upgrades. :: {exp}");
            }

            await PerformManagedCertificateMigrations();

            // PerformCAMaintenance();
        }

        private async Task InitDataStore()
        {
            var enableExtendedDataStores = true;

            try
            {
                if (enableExtendedDataStores)
                {

                    var defaultStoreId = CoreAppSettings.Current.ConfigDataStoreConnectionId;
                    var dataStoreInfo = await GetDataStore(defaultStoreId);

                    if (string.IsNullOrEmpty(defaultStoreId) || defaultStoreId == "(default)")
                    {
                        // default sqlite storage
                        _itemManager = new SQLiteManagedItemStore("", _serviceLog);
                        _credentialsManager = new SQLiteCredentialStore("", _serviceLog);
                    }
                    else
                    {
                        // select data store based on current default selection
                        var managedItemStoreOK = await SelectManagedItemStore(defaultStoreId);
                        if (!managedItemStoreOK)
                        {
                            var msg = $"FATAL: Managed Item Store {defaultStoreId} could not connect or load. Service will not start.";
                            _serviceLog.Error(msg);
                            throw new Exception(msg);
                        }

                        var credentialStoreOK = await SelectCredentialsStore(defaultStoreId);

                        if (!credentialStoreOK)
                        {
                            var msg = $"FATAL: Credential Store {defaultStoreId} could not connect or load. Service will not start.";
                            _serviceLog.Error(msg);
                            throw new Exception(msg);
                        }

                        _serviceLog.Information($"Certify Manager is connected to data store {dataStoreInfo.Id} '{dataStoreInfo.Title}' [{dataStoreInfo.TypeId}]");
                    }
                }
                else
                {
                    _itemManager = new SQLiteManagedItemStore("", _serviceLog);
                    _credentialsManager = new SQLiteCredentialStore("", _serviceLog);
                }

                if (!_itemManager.IsInitialised().Result)
                {
                    _serviceLog?.Error($"Item Manager failed to initialise properly. Check service logs for more information.");
                }
            }
            catch (Exception exp)
            {
                var msg = $"Failed to open or upgrade the managed items data store. :: {exp}";
                _serviceLog?.Error(msg);
                throw new Exception(msg);
            }
        }
        /// <summary>
        /// Setup service logging
        /// </summary>
        /// <param name="serverConfig"></param>
        private void InitLogging(Shared.ServiceConfig serverConfig)
        {
            _loggingLevelSwitch = new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Information);

            SetLoggingLevel(serverConfig?.LogLevel);

            _serviceLog = new Loggy(
                new LoggerConfiguration()
               .MinimumLevel.ControlledBy(_loggingLevelSwitch)
               .WriteTo.File(Path.Combine(EnvironmentUtil.CreateAppDataPath("logs"), "session.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10), rollOnFileSizeLimit: true, fileSizeLimitBytes: 5 * 1024 * 1024)
               .CreateLogger()
               );

            _serviceLog?.Information($"-------------------- Logging started: {_loggingLevelSwitch.MinimumLevel} --------------------");
        }

        /// <summary>
        /// Update the current service log level
        /// </summary>
        /// <param name="logLevel"></param>
        public void SetLoggingLevel(string logLevel)
        {
            switch (logLevel?.ToLower())
            {
                case "debug":
                    _loggingLevelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
                    break;

                case "verbose":
                    _loggingLevelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Verbose;
                    break;

                default:
                    _loggingLevelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Information;
                    break;
            }
        }

        /// <summary>
        /// Load cached set of ACME Certificate authorities
        /// </summary>
        private void LoadCertificateAuthorities()
        {
            _certificateAuthorities.Clear();

            // load core CAs and custom CAs
            foreach (var ca in CertificateAuthority.CoreCertificateAuthorities)
            {
                _certificateAuthorities.TryAdd(ca.Id, ca);
            }

            try
            {
                var customCAs = SettingsManager.GetCustomCertificateAuthorities();

                foreach (var ca in customCAs)
                {
                    _certificateAuthorities.TryAdd(ca.Id, ca);
                }
            }
            catch (Exception exp)
            {
                // failed to load custom CAs
                _serviceLog?.Error(exp.Message);
            }
        }

        /// <summary>
        /// Set the status reporting provider to report back to client(s) (UI etc)
        /// </summary>
        /// <param name="statusReporting"></param>
        public void SetStatusReporting(IStatusReporting statusReporting)
        {
            _statusReporting = statusReporting;
        }

        /// <summary>
        /// used to set a specific account for testing, instead of loading from config
        /// </summary>
        public AccountDetails OverrideAccountDetails { get; set; }
        /// <summary>
        /// Get the ACME client applicable for the given managed certificate
        /// </summary>
        /// <param name="managedItem"></param>
        /// <returns></returns>
        public async Task<IACMEClientProvider> GetACMEProvider(ManagedCertificate managedItem, AccountDetails caAccount)
        {
            // determine account to use for the given managed cert

            if (caAccount != null)
            {
                _certificateAuthorities.TryGetValue(caAccount?.CertificateAuthorityId, out var ca);

                if (ca != null)
                {
                    var acmeBaseUrl = managedItem.UseStagingMode ? ca.StagingAPIEndpoint : ca.ProductionAPIEndpoint;

                    return await GetACMEProvider(caAccount.StorageKey, acmeBaseUrl, caAccount, ca.AllowUntrustedTls);
                }
                else
                {
                    // Unknown acme CA. May have been removed from CA list.
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Get the ACME client for the given storage key or create and add a new one
        /// </summary>
        /// <param name="storageKey"></param>
        /// <param name="acmeApiEndpoint"></param>
        /// <param name="account"></param>
        /// <param name="allowUntrustedTls"></param>
        /// <returns></returns>
        private async Task<IACMEClientProvider> GetACMEProvider(string storageKey, string acmeApiEndpoint = null, AccountDetails account = null, bool allowUntrustedTls = false)
        {
            // get or init acme provider required for the given account
            if (_acmeClientProviders.TryGetValue(storageKey, out var provider))
            {
                return provider;
            }
            else
            {
                var userAgent = Util.GetUserAgent();
                var settingBaseFolder = EnvironmentUtil.CreateAppDataPath();
                var providerPath = Path.Combine(settingBaseFolder, "certes_" + storageKey);

                var newProvider = new AnvilACMEProvider(new AnvilACMEProviderSettings
                {
                    AcmeBaseUri = acmeApiEndpoint,
                    ServiceSettingsBasePath = settingBaseFolder,
                    LegacySettingsPath = providerPath,
                    UserAgentName = userAgent,
                    AllowUntrustedTls = allowUntrustedTls,
                    DefaultACMERetryIntervalSeconds = CoreAppSettings.Current.DefaultACMERetryInterval,
                    EnableIssuerCache = CoreAppSettings.Current.EnableIssuerCache
                });

                if (!_useWindowsNativeFeatures)
                {
                    newProvider.DefaultCertificateFormat = "pem";
                }

                await newProvider.InitProvider(_serviceLog, account);

                _acmeClientProviders.TryAdd(storageKey, newProvider);

                return newProvider;
            }
        }

        /// <summary>
        /// Update progress tracking and send status report to client(s). optionally logging to service log
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="state"></param>
        /// <param name="logThisEvent"></param>
        public void ReportProgress(IProgress<RequestProgressState> progress, RequestProgressState state, bool logThisEvent = true)
        {
            if (progress != null)
            {
                progress.Report(state);
            }

            // report request state to status hub clients

            _statusReporting?.ReportRequestProgress(state);

            if (state.ManagedCertificate != null && logThisEvent)
            {
                if (state.CurrentState == RequestState.Error)
                {
                    LogMessage(state.ManagedCertificate.Id, "[Progress] " + state.Message, LogItemType.GeneralError);
                }
                else
                {
                    LogMessage(state.ManagedCertificate.Id, "[Progress] " + state.Message, LogItemType.GeneralInfo);
                }
            }
        }

        /// <summary>
        /// Append to log for given managed certificate id
        /// </summary>
        /// <param name="managedItemId"></param>
        /// <param name="msg"></param>
        /// <param name="logType"></param>
        private void LogMessage(string managedItemId, string msg, LogItemType logType = LogItemType.GeneralInfo) => ManagedCertificateLog.AppendLog(managedItemId, new ManagedCertificateLogItem
        {
            EventDate = DateTimeOffset.UtcNow,
            LogItemType = logType,
            Message = msg
        }, _loggingLevelSwitch);

        /// <summary>
        /// When called, look for periodic maintenance tasks we can perform such as renewal
        /// </summary>
        /// <returns>  </returns>
        public async Task<bool> PerformRenewalTasks()
        {
            try
            {
                Debug.WriteLine("Checking for renewal tasks..");

                SettingsManager.LoadAppSettings();

                // perform pending renewals
                await PerformRenewAll(new RenewalSettings { });

                // flush status report queue
                await SendQueuedStatusReports();
            }
            catch (Exception exp)
            {
                _tc?.TrackException(exp);
                return await Task.FromResult(false);
            }

            return await Task.FromResult(true);
        }

        public void Dispose() => Cleanup();

        private void Cleanup()
        {
            ManagedCertificateLog.DisposeLoggers();
            if (_tc != null)
            {
                _tc.Dispose();
            }
        }

        /// <summary>
        /// Perform (or preview) an import of settings from another instance
        /// </summary>
        /// <param name="importRequest"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> PerformImport(ImportRequest importRequest)
        {
            var migrationManager = new MigrationManager(_itemManager, _credentialsManager, _serverProviders);

            var importResult = await migrationManager.PerformImport(importRequest.Package, importRequest.Settings, importRequest.IsPreviewMode);

            // store and apply certs if we have no errors

            var hasError = false;
            if (!importResult.Any(i => i.HasError))
            {
                if (importRequest.Settings.IncludeDeployment)
                {

                    var deploySteps = new List<ActionStep>();
                    foreach (var m in importRequest.Package.Content.ManagedCertificates)
                    {
                        var managedCert = await GetManagedCertificate(m.Id);

                        if (managedCert != null && !string.IsNullOrEmpty(managedCert.CertificatePath))
                        {
                            var deployResult = await DeployCertificate(managedCert, null, isPreviewOnly: importRequest.IsPreviewMode);

                            deploySteps.Add(new ActionStep { Category = "Deployment", HasError = !deployResult.IsSuccess, Key = managedCert.Id, Description = deployResult.Message });
                        }
                    }

                    importResult.Add(new ActionStep { Title = "Deployment" + (importRequest.IsPreviewMode ? " [Preview]" : ""), Substeps = deploySteps });
                }
            }
            else
            {
                hasError = true;
            }

            _tc?.TrackEvent("Import" + (importRequest.IsPreviewMode ? "_Preview" : ""), new Dictionary<string, string> {
                { "hasErrors", hasError.ToString() }
            });

            return importResult;
        }

        /// <summary>
        /// Perform (or preview) and export of settings from this instance
        /// </summary>
        /// <param name="exportRequest"></param>
        /// <returns></returns>
        public async Task<ImportExportPackage> PerformExport(ExportRequest exportRequest)
        {
            _tc?.TrackEvent("Export" + (exportRequest.IsPreviewMode ? "_Preview" : ""));

            var migrationManager = new MigrationManager(_itemManager, _credentialsManager, _serverProviders);
            return await migrationManager.PerformExport(exportRequest.Filter, exportRequest.Settings, exportRequest.IsPreviewMode);
        }

        /// <summary>
        /// Get the current service log (per line)
        /// </summary>
        /// <param name="type"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<string[]> GetServiceLog(string type, int limit)
        {
            string logPath = null;

            if (type == "session")
            {
                logPath = Path.Combine(EnvironmentUtil.CreateAppDataPath("logs"), "session.log");
            }

            if (logPath != null && System.IO.File.Exists(logPath))
            {
                try
                {
                    // TODO: use reverse stream reader for large files

                    // get last n rows in date order
                    var log = System.IO.File.ReadAllLines(logPath)
                        .Reverse()
                        .Take(limit)
                        .Reverse()
                        .ToArray();

                    return await Task.FromResult(log);
                }
                catch (Exception exp)
                {
                    return new string[] { $"Failed to read log: {exp}" };
                }
            }
            else
            {
                return new string[] { "" };
            }
        }

        public ICredentialsManager GetCredentialsManager() => _credentialsManager;
        public IManagedItemStore GetManagedItemStore() => _itemManager;
        public Task ApplyPreferences()
        {
            if (CoreAppSettings.Current.EnableAppTelematics && _tc == null)
            {
                _tc = new TelemetryManager(Locales.ConfigResources.AIInstrumentationKey);
            }
            else if (!CoreAppSettings.Current.EnableAppTelematics && _tc != null)
            {
                _tc?.Dispose();
                _tc = null;
            }

            return Task.FromResult(true);
        }

        private IAccessControl _accessControl;
        public Task<IAccessControl> GetCurrentAccessControl()
        {
            if (_accessControl == null)
            {
                var store = new SQLiteAccessControlStore();
                _accessControl = new AccessControl(_serviceLog, store);
            }

            return Task.FromResult(_accessControl);
        }
    }
}
