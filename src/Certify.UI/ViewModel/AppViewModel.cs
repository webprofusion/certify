using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using Certify.Client;
using Certify.Locales;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Models.Utils;
using Certify.Shared;
using Certify.UI.Settings;
using PropertyChanged;
using Serilog;

namespace Certify.UI.ViewModel
{
    public class AppViewModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>
        //public static AppModel AppViewModel = new DesignViewModel(); // for UI testing
        public static AppViewModel Current = GetModel();

        public AppViewModel()
        {
            CertifyClient = new CertifyServiceClient(GetDefaultServerConnection());

            Init();
        }

        public AppViewModel(ICertifyClient certifyClient)
        {
            CertifyClient = certifyClient;

            Init();
        }

        private void Init()
        {
            Log = new Loggy(
                new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Debug()
                .WriteTo.File(Path.Combine(Management.Util.GetAppDataFolder("logs"), "ui.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
                .CreateLogger()
                );

            ProgressResults = new ObservableCollection<RequestProgressState>();

            ImportedManagedCertificates = new ObservableCollection<ManagedCertificate>();
            ManagedCertificates = new ObservableCollection<ManagedCertificate>();

        }

        public ILog Log { get; private set; } = null;

        internal async Task<Preferences> GetPreferences()
        {
            return await CertifyClient.GetPreferences();
        }

        public const int ProductTypeId = 1;

        //private CertifyManager certifyManager = null;
        internal ICertifyClient CertifyClient = null;

        public PluginManager PluginManager { get; set; }

        public string CurrentError { get; set; }
        public bool IsError { get; set; }
        public bool IsServiceAvailable { get; set; } = false;
        public bool IsLoading { get; set; } = true;
        public bool IsUpdateInProgress { get; set; } = false;

        public double UIScaleFactor { get; set; } = 1;

        public Dictionary<string, string> UIThemes { get; } = new Dictionary<string, string>
        {
              {"Light","Light Theme"},
              {"Dark","Dark Theme" }
        };

        public string DefaultUITheme = "Dark";

        public UISettings UISettings { get; set; }

        public void RaiseError(Exception exp)
        {
            IsError = true;
            CurrentError = exp.Message;

            System.Windows.MessageBox.Show(exp.Message);
        }

        public Preferences Preferences { get; set; } = new Preferences();

        internal async Task SetPreferences(Preferences prefs)
        {
            await CertifyClient.SetPreferences(prefs);
            Preferences = prefs;
        }

        internal async Task<List<BindingInfo>> GetServerSiteList(StandardServerTypes serverType)
        {
            return await CertifyClient.GetServerSiteList(serverType);
        }

        internal async Task SetInstanceRegistered()
        {
            var prefs = await CertifyClient.GetPreferences();
            prefs.IsInstanceRegistered = true;
            await CertifyClient.SetPreferences(prefs);
            Preferences = prefs;
        }

        /// <summary>
        /// List of all the sites we currently manage 
        /// </summary>
        public ObservableCollection<ManagedCertificate> ManagedCertificates
        {
            get => managedCertificates;
            set
            {
                managedCertificates = value;
                if (SelectedItem != null)
                {
                    SelectedItem = SelectedItem;
                    RaisePropertyChangedEvent(nameof(SelectedItem));
                }
            }
        }

        private ObservableCollection<ManagedCertificate> managedCertificates;

        /// <summary>
        /// If set, there are one or more vault items available to be imported as managed sites 
        /// </summary>
        public ObservableCollection<ManagedCertificate> ImportedManagedCertificates { get; set; }

        /// <summary>
        /// If true, import from vault/iis scan will merge multi domain sites into one managed site 
        /// </summary>
        public bool IsImportSANMergeMode { get; set; }

        public bool HasDeprecatedChallengeTypes { get; set; } = false;

        public ObservableCollection<AccountDetails> AccountDetails = new ObservableCollection<AccountDetails>();

        public ObservableCollection<CertificateAuthority> CertificateAuthorities = new ObservableCollection<CertificateAuthority>();

        public async Task RefreshCertificateAuthorityList()
        {
            var list = await CertifyClient.GetCertificateAuthorities();

            CertificateAuthorities.Clear();

            foreach (var a in list)
            {
                CertificateAuthorities.Add(a);
            }
        }

        public async Task RefreshAccountsList()
        {
            var list = await CertifyClient.GetAccounts();

            AccountDetails.Clear();

            foreach (var a in list)
            {
                var ca = CertificateAuthorities.FirstOrDefault(c => c.Id == a.CertificateAuthorityId);
                a.Title = $"{ca?.Title ?? "[Unknown CA]"} [{(a.IsStagingAccount ? "Staging" : "Production")}]";
                AccountDetails.Add(a);
            }
        }

        public virtual bool HasRegisteredContacts => AccountDetails.Any();

        public async Task<List<ActionStep>> PerformDeployment(string managedCertificateId, string taskId, bool isPreviewOnly)
        {
            var results = await CertifyClient.PerformDeployment(managedCertificateId, taskId, isPreviewOnly);
            return results;
        }

        public ManagedCertificate SelectedItem
        {
            get => selectedItem;
            set
            {
                if (value?.Id != null && !ManagedCertificates.Contains(value))
                {
                    value = ManagedCertificates.FirstOrDefault(s => s.Id == value.Id);
                }
                selectedItem = value;
            }
        }

        private ManagedCertificate selectedItem;

        public bool IsRegisteredVersion { get; set; }

        internal async Task<ActionResult> AddContactRegistration(ContactRegistration reg)
        {
            var result = await CertifyClient.AddAccount(reg);

            RaisePropertyChangedEvent(nameof(HasRegisteredContacts));
            return result;
        }
        internal async Task<ActionResult> RemoveAccount(string storageKey)
        {
            var result = await CertifyClient.RemoveAccount(storageKey);

            await RefreshAccountsList();
            RaisePropertyChangedEvent(nameof(HasRegisteredContacts));

            return result;
        }

        public ServiceConfig GetAppServiceConfig()
        {
            return CertifyClient.GetAppServiceConfig();
        }

        public int MainUITabIndex { get; set; }

        [DependsOn(nameof(ProgressResults))]
        public bool HasRequestsInProgress => (ProgressResults != null && ProgressResults.Any());

        public ObservableCollection<RequestProgressState> ProgressResults { get; set; }

        public List<VaultItem> VaultTree { get; set; }

        [DependsOn(nameof(VaultTree))]
        public string ACMESummary { get; set; }

        [DependsOn(nameof(VaultTree))]
        public string VaultSummary { get; set; }

        public string PrimaryContactEmail { get; set; }

        public bool IsUpdateAvailable { get; set; }

        /// <summary>
        /// If an update is available this will contain more info about the new update 
        /// </summary>
        public UpdateCheck UpdateCheckResult { get; set; }

        public static AppViewModel GetModel()
        {
            var stack = new StackTrace();
            if (stack.GetFrames().Last().GetMethod().Name == "Main")
            {
                return new AppViewModel();
            }
            else
            {
                return new AppDesignViewModel();
            }
        }

        // FIXME: async blocking
        public virtual bool IsIISAvailable { get; set; }

        public virtual Version IISVersion { get; set; }

        /// <summary>
        /// check if Server type (e.g. IIS) is available, if so also populates IISVersion 
        /// </summary>
        /// <param name="serverType"></param>
        /// <returns></returns>
        public async Task<bool> CheckServerAvailability(StandardServerTypes serverType)
        {
            IsIISAvailable = await CertifyClient.IsServerAvailable(serverType);

            if (IsIISAvailable)
            {
                IISVersion = await CertifyClient.GetServerVersion(serverType);
            }

            RaisePropertyChangedEvent(nameof(IISVersion));
            RaisePropertyChangedEvent(nameof(ShowIISWarning));

            return IsIISAvailable;
        }

        public async Task<UpdateCheck> CheckForUpdates()
        {
            return await CertifyClient.CheckForUpdates();
        }

        /// <summary>
        /// If an IIS Version is present and it is lower than v8.0 the SNI is not supported and
        /// limitations apply
        /// </summary>
        public bool ShowIISWarning
        {
            get
            {
                if (IsIISAvailable && IISVersion?.Major < 8)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public async Task<List<ActionResult>> ValidateDeploymentTask(DeploymentTaskValidationInfo deploymentTaskValidationInfo)
        {
            return await CertifyClient.ValidateDeploymentTask(deploymentTaskValidationInfo);
        }

        public Shared.ServerConnection GetDefaultServerConnection()
        {
            var defaultConfig = new ServerConnection(SharedUtils.ServiceConfigManager.GetAppServiceConfig());

            var connections = Shared.Core.Management.ServerConnectionManager.GetServerConnections(Log, defaultConfig);

            if (connections.Any() && connections.Count() == 1)
            {
                Shared.Core.Management.ServerConnectionManager.Save(Log, connections);
            }

            return connections.FirstOrDefault(c => c.IsDefault = true);
        }

        public async Task InitServiceConnections()
        {
            var connectionConfig = GetDefaultServerConnection();

            //check service connection
            IsServiceAvailable = await CheckServiceAvailable();

            var attemptsRemaining = 5;
            while (!IsServiceAvailable && attemptsRemaining > 0)
            {
                Debug.WriteLine("Service not yet available. Waiting a few seconds..");

                // the service could still be starting up or port may be reallocated
                var waitMS = (6 - attemptsRemaining) * 1000;
                await Task.Delay(waitMS);

                // restart client in case port has reallocated
                CertifyClient = new CertifyServiceClient(connectionConfig);

                IsServiceAvailable = await CheckServiceAvailable();

                if (!IsServiceAvailable)
                {
                    attemptsRemaining--;

                    // give up
                    if (attemptsRemaining == 0)
                    {
                        return;
                    }
                }
            }

            // wire up stream events
            CertifyClient.OnMessageFromService += CertifyClient_SendMessage;
            CertifyClient.OnRequestProgressStateUpdated += UpdateRequestTrackingProgress;
            CertifyClient.OnManagedCertificateUpdated += CertifyClient_OnManagedCertificateUpdated;

            // connect to status api stream & handle events
            try
            {
                await CertifyClient.ConnectStatusStreamAsync();

            }
            catch (Exception exp)
            {
                // failed to connect to status signalr hub
                Log?.Error($"Failed to connect to status hub: {exp}");
            }
        }

        public async Task<List<DnsZone>> GetDnsProviderZones(string challengeProvider, string challengeCredentialKey)
        {
            return await CertifyClient.GetDnsProviderZones(challengeProvider, challengeCredentialKey);
        }

        private async void CertifyClient_OnManagedCertificateUpdated(ManagedCertificate obj) => await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                                                                                                {
                                                                                                    // a managed site has been updated, update it in our view
                                                                                                    await UpdatedCachedManagedCertificate(obj);
                                                                                                });

        /// <summary>
        /// Checks the service availability by fetching the version. If the service is available but the version is wrong an exception will be raised.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> CheckServiceAvailable()
        {
            string version = null;
            try
            {
                version = await CertifyClient.GetAppVersion();

                IsServiceAvailable = true;
            }
            catch (Exception)
            {
                //service not available
                IsServiceAvailable = false;
            }

            if (version != null)
            {

                // ensure service is correct version
                var v = Version.Parse(version.Replace("\"", ""));

                var assemblyVersion = typeof(ServiceConfig).Assembly.GetName().Version;

                if (v.Major != assemblyVersion.Major)
                {
                    throw new Exception($"Invalid service version ({v}). Please ensure the old version of the app has been fully uninstalled, then re-install the latest version.");
                }
                else
                {
                    return IsServiceAvailable;
                }
            }
            else
            {
                return IsServiceAvailable;
            }
        }

        static SemaphoreSlim _prefLock = new SemaphoreSlim(1, 1);
        public virtual async Task SavePreferences()
        {
            // we use a semaphore to lock the save to preferences to stop multiple callers saves prefs at the same time (unlikely)
            await _prefLock.WaitAsync(500);
            try
            {
                await this.CertifyClient.SetPreferences(Preferences);
            }
            catch
            {
                Debug.WriteLine("Pref wait lock exceeded");
            }
            finally
            {
                _prefLock.Release();
            }
        }

        /// <summary>
        /// Load initial settings including preferences, list of managed sites, primary contact 
        /// </summary>
        /// <returns></returns>
        public virtual async Task LoadSettingsAsync()
        {
            Preferences = await CertifyClient.GetPreferences();

            await RefreshManagedCertificates();

            await RefreshCertificateAuthorityList();

            await RefreshAccountsList();

            await RefreshChallengeAPIList();

            await RefreshStoredCredentialsList();

            await RefreshDeploymentTaskProviderList();

        }

        public virtual async Task RefreshManagedCertificates()
        {
            var filter = new ManagedCertificateFilter();

            // include external managed certs
            filter.IncludeExternal = Preferences.IncludeExternalCertManagers;

            var list = await CertifyClient.GetManagedCertificates(filter);

            foreach (var i in list)
            {
                i.IsChanged = false;

                if (!HasDeprecatedChallengeTypes && i.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI))
                {
                    HasDeprecatedChallengeTypes = true;
                }
            }

            ManagedCertificates = new ObservableCollection<ManagedCertificate>(list);
        }

        private void CertifyClient_SendMessage(string arg1, string arg2) => MessageBox.Show($"Received: {arg1} {arg2}");

        public async Task<bool> AddOrUpdateManagedCertificate(ManagedCertificate item)
        {
            var updatedManagedCertificate = await CertifyClient.UpdateManagedCertificate(item);
            updatedManagedCertificate.IsChanged = false;

            // add/update site in our local cache
            await UpdatedCachedManagedCertificate(updatedManagedCertificate);

            return true;
        }

        public async Task<bool> DeleteManagedCertificate(ManagedCertificate selectedItem)
        {
            var existing = ManagedCertificates.FirstOrDefault(s => s.Id == selectedItem.Id);
            if (existing != null)
            {
                if (MessageBox.Show(SR.ManagedCertificateSettings_ConfirmDelete, SR.ConfirmDelete, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                {
                    existing.Deleted = true;
                    var deletedOK = await CertifyClient.DeleteManagedCertificate(selectedItem.Id);
                    if (deletedOK)
                    {
                        ManagedCertificates.Remove(existing);
                    }
                    return deletedOK;
                }
            }
            return false;
        }

        public async Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId, bool resumePaused = true)
        {
            //begin request process
            var managedCertificate = ManagedCertificates.FirstOrDefault(s => s.Id == managedItemId);

            if (managedCertificate != null)
            {
                MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                TrackProgress(managedCertificate);

                // start request
                return await CertifyClient.BeginCertificateRequest(managedCertificate.Id, resumePaused);
            }
            else
            {
                return null;
            }
        }

        public void TrackProgress(ManagedCertificate managedCertificate)
        {
            //add request to observable list of progress state
            var progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCertificate);

            //begin monitoring progress
            UpdateRequestTrackingProgress(progressState);

            //var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);
        }

        public async void RenewAll(RenewalSettings settings)
        {
            //FIXME: currently user can run renew all again while renewals are still in progress

            var itemTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            foreach (var s in ManagedCertificates)
            {
                if (string.IsNullOrEmpty(s.SourceId))
                {
                    if ((settings.Mode == RenewalMode.Auto && s.IncludeInAutoRenew) || settings.Mode != RenewalMode.Auto)
                    {
                        var progressState = new RequestProgressState(RequestState.Running, "Starting..", s);
                        if (!itemTrackers.ContainsKey(s.Id))
                        {
                            itemTrackers.Add(s.Id, new Progress<RequestProgressState>(progressState.ProgressReport));

                            //begin monitoring progress
                            UpdateRequestTrackingProgress(progressState);
                        }
                    }
                }
            }

            try
            {
                await CertifyClient.BeginAutoRenewal(settings);
            }
            catch (TaskCanceledException exp)
            {
                // very long running renewal may time out on task await
                Log?.Warning("Auto Renewal UI task cancelled (timeout) " + exp.ToString());
            }

            // now continue to poll status of current request. should this just be a query for all
            // current requests?
        }

        public async Task UpdatedCachedManagedCertificate(ManagedCertificate managedCertificate, bool reload = false)
        {
            var existing = ManagedCertificates.FirstOrDefault(i => i.Id == managedCertificate.Id);
            var newItem = managedCertificate;

            // optional reload managed site details (for refresh)
            if (reload)
            {
                newItem = await CertifyClient.GetManagedCertificate(managedCertificate.Id);
            }

            if (newItem != null)
            {
                newItem.IsChanged = false;

                // update our cached copy of the managed site details
                if (existing != null)
                {
                    var index = ManagedCertificates.IndexOf(existing);
                    ManagedCertificates[index] = newItem;
                }
                else
                {
                    ManagedCertificates.Add(newItem);
                }
            }
        }

        public async Task<List<ActionStep>> GetPreviewActions(ManagedCertificate item) => await CertifyClient.PreviewActions(item);

        private void UpdateRequestTrackingProgress(RequestProgressState state)
        {
            System.Windows.Application.Current.Dispatcher.Invoke((Action)delegate
                                        {
                                            var existing = ProgressResults.FirstOrDefault(p => p.ManagedCertificate.Id == state.ManagedCertificate.Id);

                                            if (existing != null)
                                            {
                                                //replace state of progress request
                                                var index = ProgressResults.IndexOf(existing);
                                                ProgressResults[index] = state;
                                            }
                                            else
                                            {
                                                ProgressResults.Add(state);
                                            }

                                            RaisePropertyChangedEvent(nameof(HasRequestsInProgress));
                                            RaisePropertyChangedEvent(nameof(ProgressResults));
                                        });
        }

        internal async Task<List<DomainOption>> GetServerSiteDomains(StandardServerTypes serverType, string siteId)
        {
            return await CertifyClient.GetServerSiteDomains(serverType, siteId);
        }

        public void ClearRequestProgressResults()
        {
            ProgressResults = new ObservableCollection<RequestProgressState>();
            RaisePropertyChangedEvent(nameof(HasRequestsInProgress));
            RaisePropertyChangedEvent(nameof(ProgressResults));
        }

        internal async Task<CertificateRequestResult> RefetchCertificate(string managedItemId)
        {
            return await CertifyClient.RefetchCertificate(managedItemId);
        }

        internal async Task<List<StatusMessage>> TestChallengeConfiguration(ManagedCertificate managedCertificate)
        {
            return await CertifyClient.TestChallengeConfiguration(managedCertificate);
        }

        internal async Task<StatusMessage> RevokeManageSiteCertificate(string id)
        {
            var result = await CertifyClient.RevokeManageSiteCertificate(id);

            if (result.IsOK)
            {
                // refresh managed cert in UI
                var updatedManagedCertificate = await CertifyClient.GetManagedCertificate(id);
                updatedManagedCertificate.IsChanged = false;

                // add/update site in our local cache
                await UpdatedCachedManagedCertificate(updatedManagedCertificate);
            }

            return result;
        }

        internal async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly)
        {
            return await CertifyClient.ReapplyCertificateBindings(managedItemId, isPreviewOnly);
        }

        /* Stored Credentials */

        private object _storedCredentialsLock = new object();
        public ObservableCollection<StoredCredential> StoredCredentials { get; set; }

        public async Task<StoredCredential> UpdateCredential(StoredCredential credential)
        {
            var result = await CertifyClient.UpdateCredentials(credential);
            await RefreshStoredCredentialsList();

            return result;
        }

        public async Task<bool> DeleteCredential(string credentialKey)
        {
            if (credentialKey == null)
            {
                return false;
            }

            var result = await CertifyClient.DeleteCredential(credentialKey);
            await RefreshStoredCredentialsList();

            return result;
        }

        public async Task<ActionResult> TestCredentials(string credentialKey)
        {
            var result = await CertifyClient.TestCredentials(credentialKey);

            return result;
        }

        public async Task RefreshStoredCredentialsList()
        {
            var list = await CertifyClient.GetCredentials();

            if (StoredCredentials == null)
            {
                StoredCredentials = new ObservableCollection<StoredCredential>();
                System.Windows.Data.BindingOperations.EnableCollectionSynchronization(StoredCredentials, _storedCredentialsLock);
            }


            StoredCredentials.Clear();
            foreach (var c in list)
            {
                StoredCredentials.Add(c);
            }

            RaisePropertyChangedEvent(nameof(StoredCredentials));
        }

        public ObservableCollection<ChallengeProviderDefinition> ChallengeAPIProviders { get; set; } = new ObservableCollection<ChallengeProviderDefinition> { };

        public async Task RefreshChallengeAPIList()
        {
            var list = await CertifyClient.GetChallengeAPIList();
            System.Windows.Application.Current.Dispatcher.Invoke((Action)delegate
            {
                ChallengeAPIProviders = new ObservableCollection<ChallengeProviderDefinition>(list);
            });
        }

        public ICommand RenewAllCommand => new RelayCommand<RenewalSettings>(RenewAll);
        public ObservableCollection<DeploymentProviderDefinition> DeploymentTaskProviders { get; set; } = new ObservableCollection<DeploymentProviderDefinition> { };

        public async Task RefreshDeploymentTaskProviderList()
        {
            var list = await CertifyClient.GetDeploymentProviderList();
            System.Windows.Application.Current.Dispatcher.Invoke((Action)delegate
            {
                DeploymentTaskProviders = new ObservableCollection<DeploymentProviderDefinition>(list.OrderBy(l => l.Title));
            });
        }

        /// <summary>
        /// Get a specific deployment task provider definition dynamically
        /// </summary>
        /// <returns></returns>
        public async Task<DeploymentProviderDefinition> GetDeploymentTaskProviderDefinition(string id, Config.DeploymentTaskConfig config = null)
        {
            var definition = await CertifyClient.GetDeploymentProviderDefinition(id, config);
            if (definition != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke((Action)delegate
                {

                    var orig = DeploymentTaskProviders.FirstOrDefault(i => i.Id == definition.Id);
                    var index = DeploymentTaskProviders.IndexOf(orig);

                    if (orig != null)
                    {
                        DeploymentTaskProviders.Remove(orig);
                    }

                    // replace definition in list
                    DeploymentTaskProviders.Insert(index >= 0 ? index : 0, definition);
                });
            }

            return definition;
        }
    }
}
