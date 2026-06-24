using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Core.Management.Challenges;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Models.Reporting;
using Certify.Providers;
using Certify.Shared.Core.Utils;
using Microsoft.Extensions.Logging;
using Registration.Core.Models.Shared;
using Serilog;

namespace Certify.Management
{
    public partial class CertifyManager : ICertifyManager, IDisposable
    {
        private IConfigurationStore _configStore = null;
        /// <summary>
        /// Storage service for managed certificates
        /// </summary>
        private IManagedItemStore _itemManager = null;

        /// <summary>
        /// Service to load and use available plugins (deployment tasks etc)
        /// </summary>
        private PluginManager _pluginManager = null;

        /// <summary>
        /// Stored Credentials service
        /// </summary>
        private ICredentialsManager _credentialsManager = null;

        /// <summary>
        /// Provider for access control, role based feature access etc
        /// </summary>
        private IAccessControl _accessControl;

        /// <summary>
        /// Application Insights logging
        /// </summary>
        private TelemetryManager _tc = null;

        /// <summary>
        /// Service (text file) logging
        /// </summary>
        private ILog _serviceLog { get; set; }

        /// <summary>
		/// Server targets for this service (e.g. local IIS, nginx etc)
        /// </summary>
		private List<ITargetWebServer> _serverProviders = [];

        /// <summary>
        /// Provider for general challenge responses
        /// </summary>
        private ChallengeResponseService _challengeResponseService = null;

        private List<ActionStep> _systemStatusItems = [];

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
        private ConcurrentDictionary<string, IACMEClientProvider> _acmeClientProviders = new();

        /// <summary>
        /// Cache of current known challenges and responses, used for dynamic challenge responses
        /// </summary>
        private ConcurrentDictionary<string, SimpleAuthorizationChallengeItem> _currentChallenges = new();

        /// <summary>
        /// Service for reporting status/progress results back to client(s)
        /// </summary>
        private IStatusReporting _statusReporting { get; set; }

        /// <summary>
        /// Set of (cached) known ACME Certificate Authorities
        /// </summary>
        private ConcurrentDictionary<string, CertificateAuthority> _certificateAuthorities = new();

        /// <summary>
        /// If true, we are running on Windows and can use windows specific features (cert store, IIS etc)
        /// </summary>
        private bool _useWindowsNativeFeatures = true;

        /// <summary>
        /// cached check result for license info
        /// </summary>
        private Registration.Core.Models.Shared.LicenseCheckResult? _cachedLicenseCheck = null;

        private ILicensingManager _licensingManager = new Providers.Internal.LicensingManager();
        private IDashboardClient _dashboardClient = new Providers.Internal.DashboardClient();

        /// <summary>
        ///  Config info/preferences such as log level, challenge service config, powershell execution policy etc
        /// </summary>
        private Shared.ServiceConfig _serverConfig;
        private readonly SemaphoreSlim _hubApiClientLock = new(1, 1);
        private HttpClient? _hubApiHttpClient;
        private Certify.Server.Hub.Api.Client? _hubApiClient;

        private System.Timers.Timer _initTimer;
        private System.Timers.Timer _heartbeatTimer;
        private System.Timers.Timer _frequentTimer;
        private System.Timers.Timer _hourlyTimer;
        private System.Timers.Timer _dailyTimer;

        private IServiceProvider _injectedServiceProvider;
        public CertifyManager(IServiceProvider injectedServiceProvider) : this()
        {
            _injectedServiceProvider = injectedServiceProvider;
        }

        public CertifyManager()
        {
            // load setting here so that we know our instance ID etc early on. Other longer tasks are deferred until Init is called.
            SettingsManager.LoadAppSettings();

            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_APPSETTINGS,
                title: "Core Service Settings",
                description: $"Loaded core service settings."
            );
        }

        private void AddSystemStatusItem(string systemStatusCategory, string systemStatusKey, string title, string description, bool hasError = false, bool hasWarning = false)
        {
            _serviceLog?.Information($"Status: {title} - {description} ");

            _systemStatusItems.RemoveAll(s => s.Key == systemStatusKey);

            _systemStatusItems.Add(new ActionStep(systemStatusKey, systemStatusCategory, title, description, hasError, hasWarning));
        }

        public async Task Init(bool enablePlugins = true)
        {
            _useWindowsNativeFeatures = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            AddSystemStatusItem(
                SystemStatusCategories.SERVICE_CORE,
                SystemStatusKeys.SERVICE_CORE_PLATFORM,
                title: "Core Service Platform",
                description: $"Core service platform is {RuntimeInformation.OSDescription}"
            );

            _serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            if (_serverConfig.ConfigStatus == Shared.ConfigStatus.DefaultFailed)
            {
                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_SVCCONFIG,
                    title: "Core Service Config",
                    description: $"Could not load service config for core service.", hasError: true
                );
            }
            else
            {
                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_SVCCONFIG,
                    title: "Core Service Config",
                    description: $"Loaded service config"
                );
            }

            InitLogging(_serverConfig);

            _serviceLog?.Information($"Certify Manager: {Util.GetAppVersion()}");

            Util.SetSupportedTLSVersions();

            _pluginManager = new PluginManager(_injectedServiceProvider)
            {
                EnableExternalPlugins = CoreAppSettings.Current.IncludeExternalPlugins
            };

            if (enablePlugins)
            {
                _pluginManager.LoadPlugins(new List<string> {
                    PluginManager.PLUGINS_DEPLOYMENT_TASKS,
                    PluginManager.PLUGINS_CERTIFICATE_MANAGERS,
                    PluginManager.PLUGINS_DNS_PROVIDERS,
                    PluginManager.PLUGINS_SERVER_PROVIDERS,
                    PluginManager.PLUGINS_DATASTORE_PROVIDERS
                }, usePluginSubfolder: false);

                if (_isMgtmHubBackend || _isDirectMgmtHubBackend)
                {
                    // for the hub instance use a built in managed DNS provider that can be used for hub managed challenges,
                    // and remove the Hub managed Challenge API provider that relies on a hub api connection
                    _pluginManager.DnsProviderProviders.RemoveAll(provider => provider.GetProviders(provider.GetType()).Any(p => p.Id == "DNS01.API.CertifyManaged"));
                    _pluginManager.DnsProviderProviders.Add(new ManagedDnsChallengeAuto());

                }
            }

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
                            if (await pr.IsAvailable())
                            {
                                _serverProviders.Add(pr);
                            }
                        }
                    }
                }
            }

            if (_pluginManager.PluginLoadResults?.Any(r => !r.IsSuccess) == true)
            {
                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_LOADPLUGINS,
                    title: "Core Service Load Plugins",
                    description: $"One or more service plugins failed to load. Some functionality may be unavailable.",
                    hasError: true
                );

                foreach (var r in _pluginManager.PluginLoadResults.Where(r => !r.IsSuccess))
                {
                    _serviceLog.Error($"Plugin load error: {r.PluginName} - {r.Message}");
                }
            }
            else
            {
                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_LOADPLUGINS,
                    title: "Core Service Load Plugins",
                    description: $"Plugins loaded with no errors."
                );
            }

            // add default IIS target server provider
            var iisServerProvider = new Servers.ServerProviderIIS();
            iisServerProvider.Init(_serviceLog);
            _serverProviders.Add(iisServerProvider);

            try
            {
                await InitDataStore();

                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_DATASTORE_INIT,
                    title: "Core Service Datastore Init",
                    description: $"Data store initialized OK."
                );

                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_DATASTORE_STATUS,
                    title: "Data Store Status",
                    description: $"Data store connected and operational."
                );
            }
            catch (DataStoreConnectionException dsEx)
            {
                var msg = $"Data store initialization failed: {dsEx.Message}";
                _serviceLog.Error(dsEx, msg);

                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_DATASTORE_INIT,
                    title: "Core Service Datastore Init",
                    description: $"Data store failed to initialize. Service running in DEGRADED MODE.",
                    hasError: true
                );

                // Initialize in degraded mode instead of throwing
                await InitDataStoreDegradedMode(dsEx.Message, dsEx.DataStoreId, dsEx.DataStoreType);

                _serviceLog.Warning("Service started in DEGRADED MODE. All certificate management functionality is disabled until the database connection is restored.");
            }
            catch (Exception exp)
            {
                var msg = $"Certify Manager data store initialization failed: {exp.Message}";
                _serviceLog.Error(exp, msg);

                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_DATASTORE_INIT,
                    title: "Core Service Datastore Init",
                    description: $"Data store failed to initialize. Service running in DEGRADED MODE.",
                    hasError: true
                );

                // Initialize in degraded mode instead of throwing
                await InitDataStoreDegradedMode(exp.Message, null, null);

                _serviceLog.Warning("Service started in DEGRADED MODE. All certificate management functionality is disabled until the database connection is restored.");
            }

            LoadCertificateAuthorities();

            // init remaining utilities and optionally enable telematics
            _challengeResponseService = new ChallengeResponseService(CoreAppSettings.Current.EnableValidationProxyAPI);

#if ENABLE_TELEMETRY
            if (CoreAppSettings.Current.EnableAppTelematics)
            {
                _tc = new TelemetryManager(Locales.ConfigResources.AIInstrumentationKey);
            }
#endif
            _httpChallengePort = _serverConfig.HttpChallengeServerPort;
            _httpChallengeServerClient.Timeout = new TimeSpan(0, 0, 20);

            _tc?.TrackEvent("ServiceStarted");

            SetupJobs();

            await UpgradeSettings();

            // Ensure default tag categories exist (only when running as a hub)
            if (!IsInDegradedMode && (_isMgtmHubBackend || _isDirectMgmtHubBackend))
            {
                await EnsureDefaultTagCategories();
            }

            _ = RefreshCachedLicenseCheck();

            _serviceLog?.Information("Certify Manager Started");
        }

        private async Task RefreshCachedLicenseCheck()
        {
            _serviceLog?.Information("Refreshing cached license check.");

            try
            {
                if (_licensingManager != null)
                {
                    var productType = _isMgtmHubBackend ? 2 : 1; // 1 = ccm or agent, 2 = hub

                    // check local license config is valid
                    _cachedLicenseCheck = _licensingManager?.GetCurrentLicense(productType, EnvironmentUtil.EnsuredAppDataPath(), CoreAppSettings.Current.InstanceId ?? string.Empty);
                    if (_cachedLicenseCheck.IsValid)
                    {
                        // check remote license state is valid
                        if (await _licensingManager?.IsInstallActive(productType, EnvironmentUtil.EnsuredAppDataPath(), CoreAppSettings.Current.InstanceId ?? string.Empty) == false)
                        {
                            _cachedLicenseCheck.StatusCode = LicenseCheckStatusCode.Invalid;
                            _cachedLicenseCheck.IsValid = false;
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Failed to refresh cached license check: {exp}");
                _cachedLicenseCheck = null;
            }
        }
        /// <summary>
        /// Setup the continuous job tasks for renewals and maintenance
        /// </summary>
        private void SetupJobs()
        {
            // one shot init of async startup dependencies (e.g. initial connection to mgmt hub instance)
            _initTimer = new System.Timers.Timer(2 * 1000); // 2 seconds
            _initTimer.Elapsed += async (s, e) =>
            {
                _initTimer.Stop();

                if (string.IsNullOrWhiteSpace(_serverConfig.HubAssignedInstanceId) && Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_AUTOJOIN") == "true")
                {
                    _serverConfig.ManagementServerHubAPI = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB");
                    SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(_serverConfig);

                    await JoinManagementHub(
                        Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB"),
                        new Models.Hub.ClientSecret
                        {
                            ClientId = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_CLIENT_ID"),
                            Secret = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_CLIENT_SECRET")
                        });
                }

                await EnsureMgmtHubConnection();
            };
            _initTimer.Start();

            // n second job timer (reporting etc)
            _heartbeatTimer = new System.Timers.Timer(30 * 1000); // every n seconds
            _heartbeatTimer.Elapsed += _heartbeatTimer_Elapsed;
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Start();

            // 5 minute job timer (maintenance etc)
            _frequentTimer = new System.Timers.Timer(5 * 60 * 1000); // every 5 minutes
            _frequentTimer.Elapsed += _frequentTimer_Elapsed;
            _frequentTimer.AutoReset = true;
            _frequentTimer.Start();

            // hourly jobs timer (renewal etc)
            _hourlyTimer = new System.Timers.Timer(60 * 60 * 1000); // every 60 minutes
            _hourlyTimer.Elapsed += _hourlyTimer_Elapsed;
            _hourlyTimer.AutoReset = true;
            _hourlyTimer.Start();

            // daily jobs timer (cleanup etc)
            _dailyTimer = new System.Timers.Timer(24 * 60 * 60 * 1000); // every 24 hrs
            _dailyTimer.Elapsed += _dailyTimer_Elapsed;
            _dailyTimer.AutoReset = true;
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

            if (Environment.GetEnvironmentVariable("CERTIFY_GENERATE_DEMO_ITEMS") == "true")
            {
                if (Environment.GetEnvironmentVariable("CERTIFY_GENERATE_DEMO_ITEM_UPDATES") == "true")
                {
                    await RandomlyUpdateDemoItems();
                }
            }
        }

        private async void _frequentTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {

            // perform frequent tasks such as checking for due renewals
            await PerformRenewalTasks(CancellationToken.None);

            // process external subscription polls, pending deployments, and queued push notifications
            await PerformExternalCertificateSubscriptionTasks(CancellationToken.None);

            // perform managhed challenge cleanup tasks (if any)
            _ = PerformManagedChallengeCleanup();

            CleanupStaleChallengeResponses();
        }

        // Add periodic cleanup for stale challenges
        private void CleanupStaleChallengeResponses()
        {
            var staleKeys = _currentChallenges
                .Where(kvp => DateTimeOffset.Now - kvp.Value.Created > TimeSpan.FromHours(1))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                _currentChallenges.TryRemove(key, out _);
            }
        }

        private async Task PerformServiceUpgrades()
        {
            _serviceLog?.Warning($"Service version has changed. Performing upgrade checks.");

            try
            {
                await PerformAccountUpgrades();
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Failed to perform ACME account upgrades. :: {exp}");
            }

            await PerformManagedCertificateMigrations();
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
               .MinimumLevel.ControlledBy(ManagedCertificateLog.LogLevelSwitchFromLogLevel(_loggingLevelSwitch))
               .WriteTo.Console()
               .WriteTo.File(Path.Combine(EnvironmentUtil.EnsuredAppDataPath("logs"), "session.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10), rollOnFileSizeLimit: true, fileSizeLimitBytes: 5 * 1024 * 1024)
               .CreateLogger();

            var msLogger = new Serilog.Extensions.Logging.SerilogLoggerFactory(serilogLog).CreateLogger<CertifyManager>();

            _serviceLog = new Loggy(msLogger);

            _serviceLog?.Information($"-------------------- Logging started: {_loggingLevelSwitch} --------------------");
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

            // report request state to status hub clients and optionally mgmt hub

            _statusReporting?.ReportRequestProgress(state);

            ReportRequestProgressToMgmtHub(state);

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
            _hubApiHttpClient?.Dispose();
            _hubApiClientLock.Dispose();
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
        public Task<ICollection<SystemLogFileInfo>> GetServiceLogFiles(int maxFiles = 20)
        {
            var logPath = EnvironmentUtil.EnsuredAppDataPath("logs");

            if (!Directory.Exists(logPath))
            {
                return Task.FromResult<ICollection<SystemLogFileInfo>>([]);
            }

            var files = Directory
                .EnumerateFiles(logPath, "*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(maxFiles)
                .Select(f => new SystemLogFileInfo
                {
                    Name = f.Name,
                    LogType = f.Name,
                    Size = f.Length,
                    DateModified = new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)
                })
                .ToList();

            return Task.FromResult<ICollection<SystemLogFileInfo>>(files);
        }

        public async Task<string[]> GetServiceLog(string type, int limit)
        {
            string logPath = null;
            var logsDir = EnvironmentUtil.EnsuredAppDataPath("logs");

            if (type == "session")
            {
                logPath = Path.Combine(logsDir, "session.log");
            }
            else if (!string.IsNullOrWhiteSpace(type))
            {
                var candidatePath = Path.GetFullPath(Path.Combine(logsDir, type));
                if (candidatePath.StartsWith(logsDir, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candidatePath))
                {
                    logPath = candidatePath;
                }
            }

            if (logPath != null && System.IO.File.Exists(logPath))
            {
                try
                {
                    // TODO: use reverse stream reader for large files

                    // get last n rows in date order
                    var log = LogParsing.ReadLogTail(logPath, limit).ToArray();

                    return await Task.FromResult(log);
                }
                catch (Exception exp)
                {
                    return [$"Failed to read log: {exp}"];
                }
            }
            else
            {
                return [""];
            }
        }

        public ICredentialsManager GetCredentialsManager() => _credentialsManager;
        public IManagedItemStore GetManagedItemStore() => _itemManager;
        public Task ApplyPreferences()
        {
#if ENABLE_TELEMETRY
            if (CoreAppSettings.Current.EnableAppTelematics && _tc == null)
            {
                _tc = new TelemetryManager(Locales.ConfigResources.AIInstrumentationKey);
            }
            else if (!CoreAppSettings.Current.EnableAppTelematics && _tc != null)
            {
                _tc?.Dispose();
                _tc = null;
            }
#endif
            return Task.FromResult(true);
        }

        public Task<IAccessControl> GetCurrentAccessControl()
        {
            return Task.FromResult(_accessControl);
        }
    }
}
