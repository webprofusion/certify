using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Core.Management.Challenges;
using Certify.Datastore.SQLite;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

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
        private LogLevel _loggingLevelSwitch { get; set; }

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

        private System.Timers.Timer _heartbeatTimer;
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
            catch (Exception exp)
            {
                var msg = $"Certify Manager failed to start. Failed to load datastore {exp}";
                _serviceLog.Error(exp, msg);
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

#if DEBUG
            if (Environment.GetEnvironmentVariable("CERTIFY_GENERATE_DEMO_ITEMS") == "true")
            {
                GenerateDemoItems();
            }
#endif

            await EnsureMgmtHubConnection();
        }

        /// <summary>
        /// Setup the continuous job tasks for renewals and maintenance
        /// </summary>
        private void SetupJobs()
        {
            // 60 second job timer (reporting etc)
            _heartbeatTimer = new System.Timers.Timer(60 * 1000); // every n seconds
            _heartbeatTimer.Elapsed += _heartbeatTimer_Elapsed;
            _heartbeatTimer.Start();

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

        private async void _heartbeatTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            await EnsureMgmtHubConnection();
        }

        private async Task EnsureMgmtHubConnection()
        {
            // connect/reconnect to management hub if enabled
            if (_managementServerClient == null || !_managementServerClient.IsConnected())
            {
                var mgmtHubUri = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB") ?? _serverConfig.ManagementServerHubUri;

                if (!string.IsNullOrWhiteSpace(mgmtHubUri))
                {
                    await StartManagementHubConnection(mgmtHubUri);
                }
            }
            else
            {

                // send heartbeat message to management hub
                SendHeartbeatToManagementHub();
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
                    throw;
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
            _loggingLevelSwitch = LogLevel.Information;

            SetLoggingLevel(serverConfig?.LogLevel);

            var serilogLog = new Serilog.LoggerConfiguration()
               .Enrich.FromLogContext()
               .MinimumLevel.ControlledBy(LogLevelSwitchFromLogLevel(_loggingLevelSwitch))
               .WriteTo.File(Path.Combine(EnvironmentUtil.CreateAppDataPath("logs"), "session.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10), rollOnFileSizeLimit: true, fileSizeLimitBytes: 5 * 1024 * 1024)
               .CreateLogger();

            var msLogger = new Serilog.Extensions.Logging.SerilogLoggerFactory(serilogLog).CreateLogger<ManagedCertificate>();

            _serviceLog = new Loggy(msLogger);

            _serviceLog?.Information($"-------------------- Logging started: {_loggingLevelSwitch} --------------------");
        }

        private LoggingLevelSwitch LogLevelSwitchFromLogLevel(LogLevel level)
        {
            switch (level)
            {
               case  LogLevel.Error:
                    return new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Error);
                case LogLevel.Debug:
                    return new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Debug);
                case LogLevel.Warning:
                    return new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Warning);
                default: 
                    return new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Information);
            }
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
                    _loggingLevelSwitch = LogLevel.Trace;
                    break;

                case "verbose":
                    _loggingLevelSwitch = LogLevel.Debug;
                    break;

                default:
                    _loggingLevelSwitch = LogLevel.Information;
                    break;
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
