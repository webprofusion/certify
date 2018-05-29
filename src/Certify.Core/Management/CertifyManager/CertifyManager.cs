using System;
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
        private bool _httpChallengeServerAvailable = false;
        private List<SimpleAuthorizationChallengeItem> _currentChallenges = new List<SimpleAuthorizationChallengeItem>();

        private ObservableCollection<RequestProgressState> _progressResults { get; set; }

        public event Action<RequestProgressState> OnRequestProgressStateUpdated;

        public CertifyManager()
        {
            _serviceLog = new Loggy(
                new LoggerConfiguration()
               .MinimumLevel.Verbose()
               .WriteTo.Debug()
               .WriteTo.File(Util.GetAppDataFolder("logs") + "\\sessionlog.txt", shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
               .CreateLogger()
               );

            Util.SetSupportedTLSVersions();

            _itemManager = new ItemManager();
            _serverProvider = (ICertifiedServer)new ServerProviderIIS();

            _progressResults = new ObservableCollection<RequestProgressState>();

            _pluginManager = new PluginManager();
            _pluginManager.LoadPlugins();

            // TODO: convert providers to plugins
            var certes = new Certify.Providers.Certes.CertesACMEProvider(Management.Util.GetAppDataFolder() + "\\certes");

            _acmeClientProvider = certes;
            _vaultProvider = certes;

            // init remaining utilities and optionally enable telematics
            _challengeDiagnostics = new ChallengeDiagnostics(CoreAppSettings.Current.EnableValidationProxyAPI);

            if (CoreAppSettings.Current.EnableAppTelematics)
            {
                _tc = new Util().InitTelemetry();
            }

            PerformUpgrades();
        }

        public void PerformUpgrades()
        {
            // check if there are no registered contacts, if so see if we are upgrading from a vault
            if (GetContactRegistrations().Count == 0)
            {
                var acmeVaultMigration = new Models.Compat.ACMEVaultUpgrader();
                if (acmeVaultMigration.HasACMEVault())
                {
                    string email = acmeVaultMigration.GetContact();
                    if (!String.IsNullOrEmpty(email))
                    {
                        var addedOK = _acmeClientProvider.AddNewAccountAndAcceptTOS(_serviceLog, email).Result;
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
            });
        }

        public RequestProgressState GetRequestProgressState(string managedItemId)
        {
            var progress = this._progressResults.FirstOrDefault(p => p.ManagedCertificate.Id == managedItemId);
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
        /// <returns></returns>
        public async Task<bool> PerformPeriodicTasks()
        {
            Debug.WriteLine("Checking for periodic tasks..");

            SettingsManager.LoadAppSettings();

            if (CoreAppSettings.Current.UseBackgroundServiceAutoRenewal)
            {
                await this.PerformRenewalAllManagedCertificates(true, null);
            }

            return await Task.FromResult(true);
        }

        public async Task<bool> PerformDailyTasks()
        {
            Debug.WriteLine("Checking for daily tasks..");

            SettingsManager.LoadAppSettings();

            if (_tc != null) _tc.TrackEvent("ServiceDailyTaskCheck");

            return await Task.FromResult(true);
        }

        public void Dispose()
        {
            ManagedCertificateLog.DisposeLoggers();
        }
    }
}
