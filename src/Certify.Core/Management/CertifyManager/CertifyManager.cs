using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Challenges;
using Certify.Management.Servers;
using Certify.Models;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Microsoft.ApplicationInsights;
using Serilog;

namespace Certify.Management
{
    public partial class CertifyManager : ICertifyManager, IDisposable
    {
        private ItemManager _itemManager = null;
        private IACMEClientProvider _acmeClientProvider = null;
        private IVaultProvider _vaultProvider = null;
        private ICertifiedServer _serverProvider = null;
        private ChallengeDiagnostics _challengeDiagnostics = null;
        private IdnMapping _idnMapping = new IdnMapping();
        private PluginManager _pluginManager { get; set; }
        private TelemetryClient _tc = null;
        private bool _isRenewAllInProgress { get; set; }
        private ILog _serviceLog { get; set; }
        private Serilog.Core.LoggingLevelSwitch _loggingLevelSwitch { get; set; }

        private bool _httpChallengeServerAvailable = false;
        private ConcurrentDictionary<string, SimpleAuthorizationChallengeItem> _currentChallenges = new ConcurrentDictionary<string, SimpleAuthorizationChallengeItem>();

        private ObservableCollection<RequestProgressState> _progressResults { get; set; }

        public event Action<RequestProgressState> OnRequestProgressStateUpdated;

        public CertifyManager()
        {
            var serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            SettingsManager.LoadAppSettings();

            InitLogging(serverConfig);

            Util.SetSupportedTLSVersions();

            _itemManager = new ItemManager();
            _serverProvider = (ICertifiedServer)new ServerProviderIIS();

            _progressResults = new ObservableCollection<RequestProgressState>();

            _pluginManager = new PluginManager();
            _pluginManager.LoadPlugins();

            // TODO: convert providers to plugins, allow for async init
            var userAgent = Util.GetUserAgent();

            var certes = new Certify.Providers.Certes.CertesACMEProvider(Management.Util.GetAppDataFolder() + "\\certes", userAgent);

            certes.InitProvider(_serviceLog).Wait();

            _acmeClientProvider = certes;
            _vaultProvider = certes;

            // init remaining utilities and optionally enable telematics
            _challengeDiagnostics = new ChallengeDiagnostics(CoreAppSettings.Current.EnableValidationProxyAPI);

            if (CoreAppSettings.Current.EnableAppTelematics)
            {
                _tc = new Util().InitTelemetry();
            }

            _httpChallengePort = serverConfig.HttpChallengeServerPort;
            _httpChallengeServerClient.Timeout = new TimeSpan(0, 0, 5);

            if (_tc != null) _tc.TrackEvent("ServiceStarted");

            _serviceLog?.Information("Certify Manager Started");

            PerformUpgrades().Wait();

        }

        private void InitLogging(Shared.ServiceConfig serverConfig)
        {
            _loggingLevelSwitch = new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Information);

            SetLoggingLevel(serverConfig?.LogLevel);

            _serviceLog = new Loggy(
                new LoggerConfiguration()
               .MinimumLevel.ControlledBy(_loggingLevelSwitch)
               .WriteTo.Debug()
               .WriteTo.File(Util.GetAppDataFolder("logs") + "\\session.log", shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
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

        public async Task PerformUpgrades()
        {
            // check if there are no registered contacts, if so see if we are upgrading from a vault
            if (GetContactRegistrations().Count == 0)
            {
                var acmeVaultMigration = new Models.Compat.ACMEVaultUpgrader();

                if (acmeVaultMigration.HasACMEVault())
                {
                    var email = acmeVaultMigration.GetContact();

                    if (!string.IsNullOrEmpty(email))
                    {
                        var addedOK = await _acmeClientProvider.AddNewAccountAndAcceptTOS(_serviceLog, email);

                        _serviceLog?.Information("Account upgrade completed (vault)");
                    }
                }
            }
        }

        public void BeginTrackingProgress(RequestProgressState state)
        {
            var existing = _progressResults.FirstOrDefault(p => p.ManagedCertificate.Id == state.ManagedCertificate.Id);
            if (existing != null)
            {
                _progressResults.Remove(existing);
            }
            _progressResults.Add(state);
        }

        public async Task<bool> LoadSettingsAsync(bool skipIfLoaded)
        {
            await _itemManager.LoadAllManagedCertificates(skipIfLoaded);
            return true;
        }

        public void ReportProgress(IProgress<RequestProgressState> progress, RequestProgressState state, bool logThisEvent = true)
        {
            if (progress != null) progress.Report(state);

            // report request state to staus hub clients
            OnRequestProgressStateUpdated?.Invoke(state);

            if (state.ManagedCertificate != null && logThisEvent)
            {
                LogMessage(state.ManagedCertificate.Id, state.Message, LogItemType.GeneralInfo);
            }
        }

        private void LogMessage(string managedItemId, string msg, LogItemType logType = LogItemType.GeneralInfo)
        {
            ManagedCertificateLog.AppendLog(managedItemId, new ManagedCertificateLogItem
            {
                EventDate = DateTime.UtcNow,
                LogItemType = LogItemType.GeneralInfo,
                Message = msg
            }, _loggingLevelSwitch);
        }

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

            if (CoreAppSettings.Current.UseBackgroundServiceAutoRenewal)
            {
                await PerformRenewalAllManagedCertificates(true, null);
            }

            return await Task.FromResult(true);
        }

        public async Task<bool> PerformDailyTasks()
        {
            _serviceLog?.Information($"Checking for daily tasks..");

            // clear old cache of challenge responses
            _currentChallenges = new ConcurrentDictionary<string, SimpleAuthorizationChallengeItem>();

            // use latest settings
            SettingsManager.LoadAppSettings();

            if (_tc != null) _tc.TrackEvent("ServiceDailyTaskCheck");

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
                if (mode == null) mode = CertificateCleanupMode.AfterExpiry;

                if (mode != CertificateCleanupMode.None)
                {
                    var excludedCertThumprints = new List<string>();

                    if (mode == CertificateCleanupMode.FullCleanup)
                    {
                        // excluded thumbprints are all certs currently tracked as managed certs
                        var managedCerts = await GetManagedCertificates();

                        foreach (var c in managedCerts)
                        {
                            if (!string.IsNullOrEmpty(c.CertificateThumbprintHash))
                            {
                                excludedCertThumprints.Add(c.CertificateThumbprintHash.ToLower());
                            }
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

        public void Dispose()
        {
            ManagedCertificateLog.DisposeLoggers();
        }
    }
}
