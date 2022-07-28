using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config.Migration;
using Certify.Core.Management;
using Certify.Core.Management.Challenges;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Providers;
using Certify.Providers.ACME.Certes;
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
        /// Set of current in-progress renewals
        /// </summary>
        private ObservableCollection<RequestProgressState> _progressResults { get; set; }

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

        public CertifyManager() : this(true)
        {

        }

        public CertifyManager(bool useWindowsNativeFeatures = true)
        {
            _useWindowsNativeFeatures = useWindowsNativeFeatures;

            _serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            SettingsManager.LoadAppSettings();

            InitLogging(_serverConfig);

            Util.SetSupportedTLSVersions();

            _pluginManager = new PluginManager();
            _pluginManager.EnableExternalPlugins = CoreAppSettings.Current.IncludeExternalPlugins;
            _pluginManager.LoadPlugins(new List<string> { "Licensing", "DashboardClient", "DeploymentTasks", "CertificateManagers", "DnsProviders", "ServerProviders" });

            // setup supported target server types for default deployment
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

            // add default IIS target server provider
            var iisServerProvider = new Servers.ServerProviderIIS();
            iisServerProvider.Init(_serviceLog);
            _serverProviders.Add(iisServerProvider);

            try
            {
                if (_serverConfig.ConfigDataStoreType == "sqlite" || string.IsNullOrEmpty(_serverConfig.ConfigDataStoreType))
                {
                    _itemManager = new SQLiteItemManager(_serverConfig.ConfigDataStoreConnectionString, _serviceLog);
                }
                else if (_serverConfig.ConfigDataStoreType == "postgres")
                {
                    _itemManager = new Datastore.Postgres.PostgresItemManager(_serverConfig.ConfigDataStoreConnectionString, _serviceLog);
                }
                else if (_serverConfig.ConfigDataStoreType == "sqlserver")
                {
                    _itemManager = new Datastore.SQLServer.SQLServerItemManager(_serverConfig.ConfigDataStoreConnectionString, _serviceLog);
                }

                if (!_itemManager.IsInitialised().Result)
                {
                    _serviceLog.Error($"Item Manager failed to initialise properly. Check service logs for more information.");
                }
            }
            catch (Exception exp)
            {
                _serviceLog.Error($"Failed to open or upgrade the managed items database. Check service has required file access permissions. :: {exp}");
            }

            _credentialsManager = new SQLiteCredentialsManager(useWindowsNativeFeatures);

            _progressResults = new ObservableCollection<RequestProgressState>();

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

            _serviceLog?.Information("Certify Manager Started");

            try
            {
                PerformAccountUpgrades().Wait();
            }
            catch (Exception exp)
            {
                _serviceLog.Error($"Failed to perform ACME account upgrades. :: {exp}");
            }

            PerformManagedCertificateMigrations().Wait();

            PerformCAMaintenance();

            // if jwt auth mode is enabled, init auth key for first windows user
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
               .WriteTo.Debug()
               .WriteTo.File(Path.Combine(EnvironmentUtil.GetAppDataFolder("logs"), "session.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10), rollOnFileSizeLimit: true, fileSizeLimitBytes: 5 * 1024 * 1024)
               .CreateLogger()
               );

            _serviceLog?.Information($"Logging started: {_loggingLevelSwitch.MinimumLevel}");
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
                _serviceLog.Error(exp.Message);
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
        /// Get the ACME client applicable for the given managed certificate
        /// </summary>
        /// <param name="managedItem"></param>
        /// <returns></returns>
        public async Task<IACMEClientProvider> GetACMEProvider(ManagedCertificate managedItem)
        {
            // determine account to use for the given managed cert
            var acc = await GetAccountDetailsForManagedItem(managedItem);
            if (acc != null)
            {
                _certificateAuthorities.TryGetValue(acc.CertificateAuthorityId, out var ca);

                if (ca != null)
                {
                    var acmeBaseUrl = managedItem.UseStagingMode ? ca.StagingAPIEndpoint : ca.ProductionAPIEndpoint;

                    return await GetACMEProvider(acc.StorageKey, acmeBaseUrl, acc, ca.AllowUntrustedTls);
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
        /// <param name="allowUntrustedTsl"></param>
        /// <returns></returns>
        private async Task<IACMEClientProvider> GetACMEProvider(string storageKey, string acmeApiEndpoint = null, AccountDetails account = null, bool allowUntrustedTsl = false)
        {
            // get or init acme provider required for the given account
            if (_acmeClientProviders.TryGetValue(storageKey, out var provider))
            {
                return provider;
            }
            else
            {
                var userAgent = Util.GetUserAgent();
                var settingBaseFolder = EnvironmentUtil.GetAppDataFolder();
                var providerPath = Path.Combine(settingBaseFolder, "certes_" + storageKey);

                var newProvider = new CertesACMEProvider(acmeApiEndpoint, settingBaseFolder, providerPath, userAgent, allowUntrustedTsl);

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
        /// Begin/restart progress tracking for renewal requests of a given managed certificate
        /// </summary>
        /// <param name="state"></param>
        public void BeginTrackingProgress(RequestProgressState state)
        {
            lock (_progressResults)
            {
                var existing = _progressResults?.FirstOrDefault(p => p.ManagedCertificate.Id == state.ManagedCertificate.Id);
                if (existing != null)
                {
                    _progressResults.Remove(existing);
                }

                _progressResults.Add(state);
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

            // report request state to staus hub clients

            _statusReporting?.ReportRequestProgress(state);

            if (state.ManagedCertificate != null && logThisEvent)
            {
                LogMessage(state.ManagedCertificate.Id, state.Message, LogItemType.GeneralInfo);
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
            EventDate = DateTime.UtcNow,
            LogItemType = logType,
            Message = msg
        }, _loggingLevelSwitch);

        /// <summary>
        /// Get current progress result for the given managed certificate id
        /// </summary>
        /// <param name="managedItemId"></param>
        /// <returns></returns>
        public RequestProgressState GetRequestProgressState(string managedItemId)
        {
            var progress = _progressResults.FirstOrDefault(p => p.ManagedCertificate.Id == managedItemId);
            if (progress == null)
            {
                return new RequestProgressState(RequestState.NotRunning, "No request in progress", null);
            }
            else
            {
                return progress;
            }
        }

        /// <summary>
        /// When called, look for periodic tasks we can perform such as renewal
        /// </summary>
        /// <returns>  </returns>
        public async Task<bool> PerformPeriodicTasks()
        {
            try
            {
                Debug.WriteLine("Checking for periodic tasks..");

                SettingsManager.LoadAppSettings();

                // perform pending renewals
                await PerformRenewalAllManagedCertificates(new RenewalSettings { }, null);
            }
            catch (Exception exp)
            {
                _tc?.TrackException(exp);
                return await Task.FromResult(false);
            }

            return await Task.FromResult(true);
        }

        /// <summary>
        /// When called, perform daily cache cleanup, cert cleanup, diagnostics and maintenance
        /// </summary>
        /// <returns></returns>
        public async Task<bool> PerformDailyTasks()
        {
            try
            {
                _serviceLog?.Information($"Checking for daily tasks..");

                _tc?.TrackEvent("ServiceDailyTaskCheck");

                // clear old cache of challenge responses
                _currentChallenges = new ConcurrentDictionary<string, SimpleAuthorizationChallengeItem>();

                // use latest settings
                SettingsManager.LoadAppSettings();

                // perform expired cert cleanup (if enabled)
                if (CoreAppSettings.Current.EnableCertificateCleanup)
                {
                    await PerformCertificateCleanup();
                }

                // perform diagnostics and status notifications if required
                await PerformScheduledDiagnostics();

                // perform item db maintenance
                await _itemManager.PerformMaintenance();

                PerformCAMaintenance();

                ApplyLatestAutoUpdateScript();

            }
            catch (Exception exp)
            {
                _tc?.TrackException(exp);

                _serviceLog?.Error($"Exception during daily task check..: {exp}");

                return await Task.FromResult(false);
            }

            return await Task.FromResult(true);
        }

        /// <summary>
        /// //if _AutoUpdate.ps1 file exists, use it to replace AutoUpdate.ps1
        /// </summary>

        private void ApplyLatestAutoUpdateScript()
        {
            if (_useWindowsNativeFeatures)
            {
                var updateScriptLocation = Path.Combine(Environment.CurrentDirectory, "Scripts", "AutoUpdate");
                var pendingUpdateScript = Path.Combine(updateScriptLocation, "_AutoUpdate.ps1");
                if (File.Exists(pendingUpdateScript))
                {
                    try
                    {
                        // move update script
                        var destScript = Path.Combine(updateScriptLocation, "AutoUpdate.ps1");
                        if (File.Exists(destScript))
                        {
                            File.Delete(destScript);
                        }

                        File.Move(pendingUpdateScript, destScript);

                        _serviceLog.Information($"Pending update script {pendingUpdateScript} was found and moved to destination {updateScriptLocation}");
                    }
                    catch
                    {
                        _serviceLog.Warning($"Pending update script {pendingUpdateScript} was found but could not be moved to destination {updateScriptLocation}");
                    }
                }
            }
        }

        /// <summary>
        /// If applicable, perform CA trust store maintenance relevant to our supported set of certificate authorities
        /// </summary>
        private void PerformCAMaintenance()
        {
            if (_useWindowsNativeFeatures)
            {
                try
                {
                    foreach (var ca in _certificateAuthorities.Values)
                    {
                        // check for any intermediate to disable (by thumbprint)
                        if (ca.DisabledIntermediates?.Any() == true)
                        {
                            // check we have disabled usage on all required intermediates
                            foreach (var i in ca.DisabledIntermediates)
                            {
                                try
                                {
                                    // local machine store
                                    CertificateManager.DisableCertificateUsage(i, CertificateManager.CA_STORE_NAME, useMachineStore: true);

                                    // local user store (service user)
                                    CertificateManager.DisableCertificateUsage(i, CertificateManager.CA_STORE_NAME, useMachineStore: false);
                                }
                                catch (Exception ex)
                                {
                                    _serviceLog?.Error(ex, "CA Maintenance: Failed to disable CA certificate usage. {thumb}", i);
                                }

                                try
                                {
                                    // local machine store
                                    if (CertificateManager.MoveCertificate(i, CertificateManager.CA_STORE_NAME, CertificateManager.DISALLOWED_STORE_NAME, useMachineStore: true))
                                    {
                                        _serviceLog?.Information("CA Maintenance: Intermediate CA certificate moved to Disallowed (machine) store. {thumb}", i);
                                    }

                                    if (CertificateManager.MoveCertificate(i, CertificateManager.CA_STORE_NAME, CertificateManager.DISALLOWED_STORE_NAME, useMachineStore: false))
                                    {
                                        _serviceLog?.Information("CA Maintenance: Intermediate CA certificate moved to Disallowed (user) store. {thumb}", i);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _serviceLog?.Error(ex, "CA Maintenance: Failed to move intermediate to Disallowed store. {thumb}", i);
                                }
                            }
                        }

                        // check for any trusted roots to add
                        if (ca.TrustedRoots?.Any() == true)
                        {
                            foreach (var root in ca.TrustedRoots)
                            {
                                if (CertificateManager.GetCertificateByThumbprint(root.Key, CertificateManager.ROOT_STORE_NAME, useMachineStore: true) == null)
                                {
                                    CertificateManager.StoreCertificateFromPem(root.Value, CertificateManager.ROOT_STORE_NAME, useMachineStore: true);
                                }
                            }
                        }

                        // check for any intermediates to add
                        if (ca.Intermediates?.Any() == true)
                        {
                            foreach (var intermediate in ca.Intermediates)
                            {
                                if (CertificateManager.GetCertificateByThumbprint(intermediate.Key, CertificateManager.CA_STORE_NAME, useMachineStore: true) == null)
                                {
                                    CertificateManager.StoreCertificateFromPem(intermediate.Value, CertificateManager.CA_STORE_NAME, useMachineStore: true);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _serviceLog?.Error(ex, "Failed to perform CA maintenance");
                }
            }
        }

        /// <summary>
        /// Perform a subset of diagnostics, report failures if status reporting is enabled.
        /// </summary>
        /// <returns></returns>
        public async Task PerformScheduledDiagnostics()
        {
            try
            {
                _serviceLog.Information("Performing system diagnostics.");

                var diagnosticResults = await PerformServiceDiagnostics();
                if (diagnosticResults.Any(d => d.IsSuccess == false))
                {
                    var reportingEmail = (await GetAccountDetailsForManagedItem(null))?.Email;

                    foreach (var d in diagnosticResults.Where(di => di.IsSuccess == false && di.Result != null))
                    {
                        _serviceLog.Warning("Diagnostic Check Failed: " + d.Message);

                        // report diagnostic failures (if enabled)
                        if (reportingEmail != null && CoreAppSettings.Current.EnableStatusReporting && _pluginManager.DashboardClient != null)
                        {

                            try
                            {
                                await _pluginManager.DashboardClient.ReportUserActionRequiredAsync(new Models.Shared.ItemActionRequired
                                {
                                    InstanceId = null,
                                    ManagedItemId = null,
                                    ItemTitle = "Diagnostic Check Failed",
                                    ActionType = "diagnostic:" + d.Result.ToString(),
                                    InstanceTitle = Environment.MachineName,
                                    Message = d.Message,
                                    NotificationEmail = reportingEmail,
                                    AppVersion = Util.GetAppVersion().ToString() + ";" + Environment.OSVersion.ToString()
                                });
                            }
                            catch (Exception)
                            {
                                _serviceLog.Warning("Failed to send diagnostic status report to API.");
                            }
                        }
                    }
                }
                else
                {
                    _serviceLog.Information("Diagnostics - OK.");
                }
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Diagnostics Error");
            }
        }

        /// <summary>
        /// Perform certificate cleanup (files and store) as per cleanup preferences
        /// </summary>
        /// <returns></returns>
        public async Task PerformCertificateCleanup()
        {
            try
            {
                var mode = CoreAppSettings.Current.CertificateCleanupMode;
                if (mode == null)
                {
                    mode = CertificateCleanupMode.AfterExpiry;
                }

                if (mode != CertificateCleanupMode.None)
                {
                    var excludedCertThumprints = new List<string>();

                    // excluded thumbprints are all certs currently tracked as managed certs
                    var managedCerts = await GetManagedCertificates(ManagedCertificateFilter.ALL);

                    foreach (var c in managedCerts)
                    {
                        if (!string.IsNullOrEmpty(c.CertificateThumbprintHash))
                        {
                            excludedCertThumprints.Add(c.CertificateThumbprintHash.ToLower());
                        }
                    }

                    if (mode == CertificateCleanupMode.FullCleanup)
                    {

                        // cleanup old pfx files in asset store(s), if any
                        var assetPath = Path.Combine(EnvironmentUtil.GetAppDataFolder(), "certes", "assets");
                        if (Directory.Exists(assetPath))
                        {
                            var ext = new List<string> { ".pfx" };
                            DeleteOldCertificateFiles(assetPath, ext);
                        }

                        assetPath = Path.Combine(EnvironmentUtil.GetAppDataFolder(), "assets");
                        if (Directory.Exists(assetPath))
                        {
                            var ext = new List<string> { ".pfx", ".key", ".crt", ".pem" };
                            DeleteOldCertificateFiles(assetPath, ext);
                        }
                    }

                    // this will only perform expiry cleanup, as no specific thumbprint provided
                    var certsRemoved = CertificateManager.PerformCertificateStoreCleanup(
                            (CertificateCleanupMode)mode,
                            DateTime.Now,
                            matchingName: null,
                            excludedThumbprints: excludedCertThumprints,
                            log: _serviceLog
                        );
                }
            }
            catch (Exception exp)
            {
                // log exception
                _serviceLog?.Error("Failed to perform certificate cleanup: " + exp.ToString());
            }
        }

        /// <summary>
        /// Perform cleanup of old certificate asset files
        /// </summary>
        /// <param name="assetPath"></param>
        /// <param name="ext"></param>
        private static void DeleteOldCertificateFiles(string assetPath, List<string> ext)
        {
            // performs a simple delete of certificate files under the assets path where the file creation time is more than 1 year ago

            var allFiles = Directory.GetFiles(assetPath, "*.*", SearchOption.AllDirectories)
                 .Where(s => ext.Contains(Path.GetExtension(s)));

            foreach (var f in allFiles)
            {
                try
                {
                    var createdAt = System.IO.File.GetCreationTime(f);
                    if (createdAt < DateTime.Now.AddMonths(-12))
                    {
                        //remove old file
                        System.IO.File.Delete(f);
                    }
                }
                catch { }
            }
        }

        public void Dispose() => ManagedCertificateLog.DisposeLoggers();

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
            if (!importResult.Any(i => i.HasError))
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

            return importResult;
        }

        /// <summary>
        /// Perform (or preview) and export of settings from this instance
        /// </summary>
        /// <param name="exportRequest"></param>
        /// <returns></returns>
        public async Task<ImportExportPackage> PerformExport(ExportRequest exportRequest)
        {
            var migrationManager = new MigrationManager(_itemManager, _credentialsManager, _serverProviders);
            return await migrationManager.PerformExport(exportRequest.Filter, exportRequest.Settings, exportRequest.IsPreviewMode);
        }

        /// <summary>
        /// Perform basic service diagnostics to check host machine configuration
        /// </summary>
        /// <returns></returns>
        public async Task<List<ActionResult>> PerformServiceDiagnostics()
        {
            return await Certify.Management.Util.PerformAppDiagnostics(includeTempFileCheck: true, ntpServer: CoreAppSettings.Current.NtpServer);
        }

        /// <summary>
        /// Get the current service log (per line)
        /// </summary>
        /// <param name="type"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<string[]> GetLog(string type, int limit)
        {
            string logPath = null;

            if (type == "session")
            {
                logPath = Path.Combine(EnvironmentUtil.GetAppDataFolder("logs"), "session.log");
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
    }
}
