using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Core.Management.Challenges;
using Certify.Models;
using Certify.Models.Config;
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
        private static readonly ActivitySource _activitySource = new("Certify.CertifyManager");
        private static readonly Meter _meter = new("Certify.CertifyManager", "1.0.0");
        
        // Counters
        private static readonly Counter<int> _certificateRequestsCounter = _meter.CreateCounter<int>("certify.certificate.requests", "requests", "Number of certificate requests initiated");
        private static readonly Counter<int> _certificateRequestsSuccessCounter = _meter.CreateCounter<int>("certify.certificate.requests_success", "requests", "Number of successful certificate requests");
        private static readonly Counter<int> _certificateRequestsFailedCounter = _meter.CreateCounter<int>("certify.certificate.requests_failed", "requests", "Number of failed certificate requests");
        private static readonly Counter<int> _renewalsAttemptedCounter = _meter.CreateCounter<int>("certify.renewals.attempted", "renewals", "Number of renewal attempts");
        private static readonly Counter<int> _renewalsSuccessCounter = _meter.CreateCounter<int>("certify.renewals.success", "renewals", "Number of successful renewals");
        private static readonly Counter<int> _renewalsFailedCounter = _meter.CreateCounter<int>("certify.renewals.failed", "renewals", "Number of failed renewals");
        private static readonly Counter<int> _challengeResponsesCounter = _meter.CreateCounter<int>("certify.challenges.responses", "challenges", "Number of challenge responses provided");
        private static readonly Counter<int> _deploymentTasksExecutedCounter = _meter.CreateCounter<int>("certify.deployment_tasks.executed", "tasks", "Number of deployment tasks executed");
        private static readonly Counter<int> _deploymentTasksFailedCounter = _meter.CreateCounter<int>("certify.deployment_tasks.failed", "tasks", "Number of deployment task failures");
        
        // Histograms
        private static readonly Histogram<double> _certificateRequestDurationHistogram = _meter.CreateHistogram<double>("certify.certificate.request_duration", "ms", "Duration of certificate request operations");
        private static readonly Histogram<double> _renewalDurationHistogram = _meter.CreateHistogram<double>("certify.renewal.duration", "ms", "Duration of renewal operations");
        private static readonly Histogram<double> _deploymentTaskDurationHistogram = _meter.CreateHistogram<double>("certify.deployment_task.duration", "ms", "Duration of deployment task operations");
        private static readonly Histogram<int> _managedCertificatesCountHistogram = _meter.CreateHistogram<int>("certify.managed_certificates.count", "certificates", "Number of managed certificates");
        
        // Gauges
        private static int _activeCertificateRequests = 0;
        private static int _activeRenewals = 0;
        private static int _activeDeploymentTasks = 0;
        private static readonly ObservableGauge<int> _activeCertificateRequestsGauge = _meter.CreateObservableGauge<int>("certify.certificate.requests_active", () => _activeCertificateRequests, "requests", "Number of certificate requests currently in progress");
        private static readonly ObservableGauge<int> _activeRenewalsGauge = _meter.CreateObservableGauge<int>("certify.renewals.active", () => _activeRenewals, "renewals", "Number of renewals currently in progress");
        private static readonly ObservableGauge<int> _activeDeploymentTasksGauge = _meter.CreateObservableGauge<int>("certify.deployment_tasks.active", () => _activeDeploymentTasks, "tasks", "Number of deployment tasks currently executing");

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
            using var activity = _activitySource.StartActivity("Init", ActivityKind.Internal);
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _useWindowsNativeFeatures = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

                activity?.SetTag("platform", RuntimeInformation.OSDescription);
                activity?.SetTag("use_windows_native_features", _useWindowsNativeFeatures);
                activity?.SetTag("enable_plugins", enablePlugins);

                AddSystemStatusItem(
                    SystemStatusCategories.SERVICE_CORE,
                    SystemStatusKeys.SERVICE_CORE_PLATFORM,
                    title: "Core Service Platform",
                    description: $"Core service platform is {RuntimeInformation.OSDescription}"
                );

                _serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

                if (_serverConfig.ConfigStatus == Shared.ConfigStatus.DefaultFailed)
                {
                    activity?.AddEvent(new ActivityEvent("ConfigLoadFailed"));
                    
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
                    }, usePluginSubfolder:false);

                    if (_isMgtmHubBackend || _isDirectMgmtHubBackend)
                    {
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
                                _serverProviders.Add(pr);
                            }
                        }
                    }
                }

                activity?.SetTag("server_providers_count", _serverProviders.Count);

                if (_pluginManager.PluginLoadResults?.Any(r => !r.IsSuccess) == true)
                {
                    var failedPlugins = _pluginManager.PluginLoadResults.Where(r => !r.IsSuccess).Count();
                    activity?.SetTag("plugins.failed_count", failedPlugins);
                    activity?.AddEvent(new ActivityEvent("PluginLoadFailures", 
                        tags: new ActivityTagsCollection { { "failed_count", failedPlugins } }));
                    
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
                }
                catch (Exception exp)
                {
                    var msg = $"Certify Manager failed to start. Failed to load datastore {exp}";
                    
                    activity?.SetStatus(ActivityStatusCode.Error, "Datastore initialization failed");
                    activity?.AddException(exp);
                    
                    _serviceLog.Error(exp, msg);

                    AddSystemStatusItem(
                        SystemStatusCategories.SERVICE_CORE,
                        SystemStatusKeys.SERVICE_CORE_DATASTORE_INIT,
                        title: "Core Service Datastore Init",
                        description: $"Data store failed to initialize. All functionality will be impaired or unavailable."
                    );

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

                _ = RefreshCachedLicenseCheck();

                stopwatch.Stop();
                
                activity?.SetTag("init.duration_ms", stopwatch.ElapsedMilliseconds);
                activity?.SetStatus(ActivityStatusCode.Ok);
                
                _serviceLog?.Information($"Certify Manager Started (initialization took {stopwatch.ElapsedMilliseconds}ms)");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                
                _serviceLog?.Error(ex, "Fatal error during CertifyManager initialization");
                throw;
            }
        }

        private async Task RefreshCachedLicenseCheck()
        {
            _serviceLog?.Information("Refreshing cached license check.");

            try
            {
                if (_licensingManager != null)
                {
                    var productType = _isMgtmHubBackend ? 2 : 1; // 1 = ccm or agent, 2 = hub

                    _cachedLicenseCheck = _licensingManager?.GetCurrentLicense(productType, EnvironmentUtil.EnsuredAppDataPath());
                    if (_cachedLicenseCheck.IsValid)
                    {
                        if (await _licensingManager?.IsInstallActive(productType, EnvironmentUtil.EnsuredAppDataPath()) == false)
                        {
                            _cachedLicenseCheck.StatusCode = LicenseCheckStatusCode.Invalid;
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
                logPath = Path.Combine(EnvironmentUtil.EnsuredAppDataPath("logs"), "session.log");
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

        public Task<IAccessControl> GetCurrentAccessControl()
        {
            return Task.FromResult(_accessControl);
        }
    }
}
