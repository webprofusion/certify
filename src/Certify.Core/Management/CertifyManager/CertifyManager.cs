using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config.Migration;
using Certify.Core.Management;
using Certify.Core.Management.Challenges;
using Certify.Management.Servers;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Providers;
using Certify.Providers.ACME.Certes;
using Microsoft.ApplicationInsights;
using Serilog;

namespace Certify.Management
{
    public partial class CertifyManager : ICertifyManager, IDisposable
    {
        private IItemManager _itemManager = null;
        private MigrationManager _migrationManager = null;
        private ICertifiedServer _serverProvider = null;
        private ChallengeDiagnostics _challengeDiagnostics = null;
        private IdnMapping _idnMapping = new IdnMapping();
        private PluginManager _pluginManager { get; set; }
        private ICredentialsManager _credentialsManager { get; set; }

        private TelemetryClient _tc = null;
        private bool _isRenewAllInProgress { get; set; }
        private ILog _serviceLog { get; set; }
        private Serilog.Core.LoggingLevelSwitch _loggingLevelSwitch { get; set; }

        private bool _httpChallengeServerAvailable = false;

        private ConcurrentDictionary<string, IACMEClientProvider> _acmeClientProviders = new ConcurrentDictionary<string, IACMEClientProvider>();
        private ConcurrentDictionary<string, SimpleAuthorizationChallengeItem> _currentChallenges = new ConcurrentDictionary<string, SimpleAuthorizationChallengeItem>();
        private ObservableCollection<RequestProgressState> _progressResults { get; set; }
        private IStatusReporting _statusReporting { get; set; }

        private ConcurrentDictionary<string, CertificateAuthority> _certificateAuthorities = new ConcurrentDictionary<string, CertificateAuthority>();
        private bool _useWindowsNativeFeatures = true;
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
            try
            {
                _itemManager = new ItemManager();
            }
            catch (Exception exp)
            {
                _serviceLog.Error($"Failed to open or upgrade the managed items database. Check service has required file access permissions. :: {exp}");
            }

            _credentialsManager = new CredentialsManager(useWindowsNativeFeatures);
            _serverProvider = (ICertifiedServer)new ServerProviderIIS();

            _progressResults = new ObservableCollection<RequestProgressState>();

            _pluginManager = new PluginManager();
            _pluginManager.EnableExternalPlugins = CoreAppSettings.Current.IncludeExternalPlugins;
            _pluginManager.LoadPlugins(new List<string> { "Licensing", "DashboardClient", "DeploymentTasks", "CertificateManagers", "DnsProviders" });

            _migrationManager = new MigrationManager(_itemManager, _credentialsManager, _serverProvider);

            LoadCertificateAuthorities();


            // init remaining utilities and optionally enable telematics
            _challengeDiagnostics = new ChallengeDiagnostics(CoreAppSettings.Current.EnableValidationProxyAPI);

            if (CoreAppSettings.Current.EnableAppTelematics)
            {
                _tc = new Util().InitTelemetry();
            }

            _httpChallengePort = _serverConfig.HttpChallengeServerPort;
            _httpChallengeServerClient.Timeout = new TimeSpan(0, 0, 20);

            if (_tc != null)
            {
                _tc.TrackEvent("ServiceStarted");
            }

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
        }

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

        public void SetStatusReporting(IStatusReporting statusReporting)
        {
            _statusReporting = statusReporting;
        }

        private async Task PerformManagedCertificateMigrations()
        {

            IEnumerable<ManagedCertificate> list = await GetManagedCertificates();

            list = list.Where(i => !string.IsNullOrEmpty(i.RequestConfig.WebhookUrl) || !string.IsNullOrEmpty(i.RequestConfig.PreRequestPowerShellScript) || !string.IsNullOrEmpty(i.RequestConfig.PostRequestPowerShellScript)
            || i.PostRequestTasks?.Any(t => t.TaskTypeId == StandardTaskTypes.POWERSHELL && t.Parameters?.Any(p => p.Key == "url") == true) == true);

            foreach (var i in list)
            {
                var result = MigrateDeploymentTasks(i);
                if (result.Item2 == true)
                {
                    // save change
                    await UpdateManagedCertificate(result.Item1);
                }
            }
        }


        private async Task<IACMEClientProvider> GetACMEProvider(ManagedCertificate managedItem)
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
                var providerPath = Path.Combine(Management.Util.GetAppDataFolder(), "certes_" + storageKey);

                var newProvider = new CertesACMEProvider(acmeApiEndpoint, providerPath, userAgent, allowUntrustedTsl);

                await newProvider.InitProvider(_serviceLog, account);

                _acmeClientProviders.TryAdd(storageKey, newProvider);

                return newProvider;
            }
        }

        private void InitLogging(Shared.ServiceConfig serverConfig)
        {
            _loggingLevelSwitch = new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Information);

            SetLoggingLevel(serverConfig?.LogLevel);

            _serviceLog = new Loggy(
                new LoggerConfiguration()
               .MinimumLevel.ControlledBy(_loggingLevelSwitch)
               .WriteTo.Debug()
               .WriteTo.File(Path.Combine(Util.GetAppDataFolder("logs"), "session.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
               .CreateLogger()
               );

            _serviceLog?.Information($"Logging started: {_loggingLevelSwitch.MinimumLevel}");
        }

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

        private void LogMessage(string managedItemId, string msg, LogItemType logType = LogItemType.GeneralInfo) => ManagedCertificateLog.AppendLog(managedItemId, new ManagedCertificateLogItem
        {
            EventDate = DateTime.UtcNow,
            LogItemType = LogItemType.GeneralInfo,
            Message = msg
        }, _loggingLevelSwitch);

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
            Debug.WriteLine("Checking for periodic tasks..");

            SettingsManager.LoadAppSettings();

            await PerformRenewalAllManagedCertificates(new RenewalSettings { }, null);

            return await Task.FromResult(true);
        }

        public async Task<bool> PerformDailyTasks()
        {
            _serviceLog?.Information($"Checking for daily tasks..");

            // clear old cache of challenge responses
            _currentChallenges = new ConcurrentDictionary<string, SimpleAuthorizationChallengeItem>();

            // use latest settings
            SettingsManager.LoadAppSettings();

            if (_tc != null)
            {
                _tc.TrackEvent("ServiceDailyTaskCheck");
            }

            // perform expired cert cleanup (if enabled)
            if (CoreAppSettings.Current.EnableCertificateCleanup)
            {
                await PerformCertificateCleanup();
            }

            return await Task.FromResult(true);
        }

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
                    var managedCerts = await GetManagedCertificates();

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
                        var assetPath = Path.Combine(Util.GetAppDataFolder(), "certes", "assets");
                        if (Directory.Exists(assetPath))
                        {
                            var ext = new List<string> { ".pfx" };
                            DeleteOldCertificateFiles(assetPath, ext);
                        }

                        assetPath = Path.Combine(Util.GetAppDataFolder(), "assets");
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

        public void Dispose() => ManagedCertificateLog.DisposeLoggers();

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

        public async Task<List<ActionStep>> PerformImport(ImportRequest importRequest)
        {
            var importResult = await _migrationManager.PerformImport(importRequest.Package, importRequest.Settings, importRequest.IsPreviewMode);

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

                importResult.Add(new ActionStep { Title = "Deployment" + (importRequest.IsPreviewMode ? "[Preview]" : ""), Substeps = deploySteps });
            }

            return importResult;
        }

        public async Task<ImportExportPackage> PerformExport(ExportRequest exportRequest)
        {
            return await _migrationManager.PerformExport(exportRequest.Filter, exportRequest.Settings, exportRequest.IsPreviewMode);
        }
    }
}
