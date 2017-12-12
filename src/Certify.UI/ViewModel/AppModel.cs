using Certify.Client;
using Certify.Locales;
using Certify.Management;
using Certify.Models;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;

namespace Certify.UI.ViewModel
{
    public class AppModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>
        //public static AppModel AppViewModel = new DesignViewModel(); // for UI testing
        public static AppModel Current = AppModel.GetModel();

        public AppModel()
        {
            /*if (!(this is DesignViewModel))
            {
               // certifyManager = new CertifyManager();
            }*/
            CertifyClient = new CertifyServiceClient();
            ProgressResults = new ObservableCollection<RequestProgressState>();

            this.ImportedManagedSites = new ObservableCollection<ManagedSite>();
            this.ManagedSites = new ObservableCollection<ManagedSite>();
        }

        public const int ProductTypeId = 1;

        //private CertifyManager certifyManager = null;
        internal ICertifyClient CertifyClient = null;

        public PluginManager PluginManager { get; set; }

        public string CurrentError { get; set; }
        public bool IsError { get; set; }
        public bool IsServiceAvailable { get; set; } = false;
        public bool IsLoading { get; set; } = true;

        public void RaiseError(Exception exp)
        {
            this.IsError = true;
            this.CurrentError = exp.Message;

            System.Windows.MessageBox.Show(exp.Message);
        }

        public Preferences Preferences { get; set; } = new Preferences();

        internal async Task SetPreferences(Preferences prefs)
        {
            await CertifyClient.SetPreferences(prefs);
            Preferences = prefs;
        }

        internal async Task SetInstanceRegistered()
        {
            var prefs = await CertifyClient.GetPreferences();
            prefs.IsInstanceRegistered = true;
            await CertifyClient.SetPreferences(prefs);
            this.Preferences = prefs;
        }

        #region properties

        /// <summary>
        /// List of all the sites we currently manage 
        /// </summary>
        public ObservableCollection<ManagedSite> ManagedSites
        {
            get { return managedSites; }
            set
            {
                managedSites = value;
                if (SelectedItem != null)
                {
                    SelectedItem = SelectedItem;
                    RaisePropertyChanged(nameof(SelectedItem));
                }
            }
        }

        private ObservableCollection<ManagedSite> managedSites;

        /// <summary>
        /// If set, there are one or more vault items available to be imported as managed sites 
        /// </summary>
        public ObservableCollection<ManagedSite> ImportedManagedSites { get; set; }

        /// <summary>
        /// If true, import from vault/iis scan will merge multi domain sites into one managed site 
        /// </summary>
        public bool IsImportSANMergeMode { get; set; }

        public virtual bool HasRegisteredContacts
        {
            get
            {
                // FIXME: this property is async, either cache or reduce reliance
                return Task.Run(() => CertifyClient.GetPrimaryContact()).Result != null;
            }
        }

        public bool HasSelectedItemDomainOptions
        {
            get
            {
                if (SelectedItem != null && SelectedItem.DomainOptions != null && SelectedItem.DomainOptions.Any())
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public ManagedSite SelectedItem
        {
            get { return selectedItem; }
            set
            {
                if (value?.Id != null && !ManagedSites.Contains(value))
                {
                    value = ManagedSites.FirstOrDefault(s => s.Id == value.Id);
                }
                selectedItem = value;
            }
        }

        private ManagedSite selectedItem;

        public bool IsRegisteredVersion { get; set; }

        internal async Task<bool> SaveManagedItemChanges()
        {
            UpdateManagedSiteSettings();

            var updatedOK = await AddOrUpdateManagedSite(SelectedItem);

            if (updatedOK) SelectedItem.IsChanged = false;

            RaisePropertyChanged(nameof(IsSelectedItemValid));
            RaisePropertyChanged(nameof(SelectedItem));

            return updatedOK;
        }

        internal async Task<bool> AddContactRegistration(ContactRegistration reg)
        {
            var addedOk = await CertifyClient.SetPrimaryContact(reg);

            // TODO: report errors
            RaisePropertyChanged(nameof(HasRegisteredContacts));
            return addedOk;
        }

        // Certify-supported challenge types
        public IEnumerable<string> ChallengeTypes { get; set; } = new string[] {
            SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
            SupportedChallengeTypes.CHALLENGE_TYPE_SNI
        };

        public IEnumerable<string> WebhookTriggerTypes => Webhook.TriggerTypes;

        public List<IPAddressOption> HostIPAddresses
        {
            get
            {
                try
                {
                    var ipAddressOptions = Certify.Utils.Networking.GetIPAddresses();

                    ipAddressOptions.Insert(0, new IPAddressOption { Description = "* (All Unassigned)", IPAddress = "*", IsIPv6 = false }); //add wildcard option

                    return ipAddressOptions;
                }
                catch (Exception)
                {
                    //return empty list
                    return new List<IPAddressOption>();
                }
            }
        }

        public SiteBindingItem SelectedWebSite
        {
            get; set;
        }

        public DomainOption PrimarySubjectDomain
        {
            get { return SelectedItem?.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain && d.IsSelected); }
            set
            {
                foreach (var d in SelectedItem.DomainOptions)
                {
                    if (d.Domain == value.Domain)
                    {
                        d.IsPrimaryDomain = true;
                        d.IsSelected = true;
                    }
                    else
                    {
                        d.IsPrimaryDomain = false;
                    }
                }
                SelectedItem.IsChanged = true;
            }
        }

        public bool IsSelectedItemValid
        {
            get => SelectedItem?.Id != null && !SelectedItem.IsChanged;
        }

        public string ValidationError { get; set; }

        public int MainUITabIndex { get; set; }

        [DependsOn(nameof(ProgressResults))]
        public bool HasRequestsInProgress
        {
            get
            {
                return (ProgressResults != null && ProgressResults.Any());
            }
        }

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

        #endregion properties

        #region methods

        public static AppModel GetModel()
        {
            var stack = new System.Diagnostics.StackTrace();
            if (stack.GetFrames().Last().GetMethod().Name == "Main")
            {
                return new AppModel();
            }
            else
            {
                return new DesignViewModel();
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
            IsIISAvailable = await CertifyClient.IsServerAvailable(StandardServerTypes.IIS);

            if (IsIISAvailable)
            {
                IISVersion = await CertifyClient.GetServerVersion(StandardServerTypes.IIS);
            }
            return IsIISAvailable;
        }

        public void PreviewImport(bool sanMergeMode)
        {
            /* AppViewModel.IsImportSANMergeMode = sanMergeMode;
             //we have no managed sites, offer to import them from vault if we have one
             var importedSites = certifyManager.ImportManagedSitesFromVault(sanMergeMode);
             ImportedManagedSites = new ObservableCollection<ManagedSite>(importedSites);*/
        }

        public async Task InitServiceConnections()
        {
            // wire up stream events
            CertifyClient.OnMessageFromService += CertifyClient_SendMessage;
            CertifyClient.OnRequestProgressStateUpdated += UpdateRequestTrackingProgress;
            CertifyClient.OnManagedSiteUpdated += CertifyClient_OnManagedSiteUpdated;

            //check service connection
            IsServiceAvailable = await CheckServiceAvailable();

            if (!IsServiceAvailable)
            {
                Debug.WriteLine("Service not yet available. Waiting a few seconds..");

                // the service could still be starting up
                await Task.Delay(5000);
                IsServiceAvailable = await CheckServiceAvailable();
                if (!IsServiceAvailable)
                {
                    // give up
                    return;
                }
            }

            // connect to status api stream & handle events
            await CertifyClient.ConnectStatusStreamAsync();
        }

        private async void CertifyClient_OnManagedSiteUpdated(ManagedSite obj)
        {
            await App.Current.Dispatcher.InvokeAsync(async () =>
              {
                  // a managed site has been updated, update it in our view
                  await UpdatedCachedManagedSite(obj);
              });
        }

        public async Task<bool> CheckServiceAvailable()
        {
            try
            {
                await CertifyClient.GetAppVersion();
                IsServiceAvailable = true;
            }
            catch (Exception)
            {
                //service not available
                IsServiceAvailable = false;
            }

            return IsServiceAvailable;
        }

        /// <summary>
        /// Load initial settings including preferences, list of managed sites, primary contact 
        /// </summary>
        /// <returns></returns>
        public async virtual Task LoadSettingsAsync()
        {
            this.Preferences = await CertifyClient.GetPreferences();

            var list = await CertifyClient.GetManagedSites(new Models.ManagedSiteFilter());

            foreach (var i in list) i.IsChanged = false;

            ManagedSites = new System.Collections.ObjectModel.ObservableCollection<Models.ManagedSite>(list);

            PrimaryContactEmail = await CertifyClient.GetPrimaryContact();
        }

        private void CertifyClient_SendMessage(string arg1, string arg2)
        {
            MessageBox.Show($"Received: {arg1} {arg2}");
        }

        public virtual void SaveSettings()
        {
            /*
            // CertifyClient..SaveManagedSites(ManagedSites.ToList());
            foreach (var d in ManagedSites.Where(s => s.Deleted))
            {
                if (d.Id != null) CertifyClient.DeleteManagedSite(d.Id);
            }
            // TODO: Identify updated sites and save them?
            foreach (var u in ManagedSites.Where(s => s.Updated))
             {
                 CertifyClient.UpdateManagedSiteu);
             }

            // remove deleted managed sites from view model
            foreach (var site in ManagedSites.Where(s => s.Deleted).ToList())
            {
                ManagedSites.Remove(site);
            }

            // refresh observable
            ManagedSites = new ObservableCollection<ManagedSite>(ManagedSites);
            */
        }

        public async Task<bool> ConfirmDiscardUnsavedChanges()
        {
            if (SelectedItem?.IsChanged ?? false)
            {
                //user needs to save or discard changes before changing selection
                if (MessageBox.Show(SR.ManagedSites_UnsavedWarning, SR.Alert, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                {
                    await DiscardChanges();
                }
                else
                {
                    // user cancelled out of dialog
                    return false;
                }
            }
            return true;
        }

        public async Task DiscardChanges()
        {
            if (SelectedItem?.IsChanged ?? false)
            {
                if (SelectedItem.Id == null)
                {
                    SelectedItem = null;
                }
                else
                {
                    // add/update site in our local cache
                    await UpdatedCachedManagedSite(SelectedItem, reload: true);
                }
            }
        }

        public async Task<bool> AddOrUpdateManagedSite(ManagedSite item)
        {
            var updatedManagedSite = await CertifyClient.UpdateManagedSite(item);
            updatedManagedSite.IsChanged = false;

            // add/update site in our local cache
            await UpdatedCachedManagedSite(updatedManagedSite);

            return true;
        }

        public async Task<bool> DeleteManagedSite(ManagedSite selectedItem)
        {
            var existing = ManagedSites.FirstOrDefault(s => s.Id == selectedItem.Id);
            if (existing != null)
            {
                if (MessageBox.Show(SR.ManagedItemSettings_ConfirmDelete, SR.ConfirmDelete, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                {
                    existing.Deleted = true;
                    var deletedOK = await CertifyClient.DeleteManagedSite(selectedItem.Id);
                    if (deletedOK)
                    {
                        ManagedSites.Remove(existing);
                    }
                    return deletedOK;
                }
            }
            return false;
        }

        public void SANSelectAll(object o)
        {
            SelectedItem?.DomainOptions.ToList().ForEach(opt => opt.IsSelected = true);
        }

        public void SANSelectNone(object o)
        {
            SelectedItem?.DomainOptions.ToList().ForEach(opt => opt.IsSelected = false);
        }

        /// <summary>
        /// For the given set of options get a new CertRequestConfig to store 
        /// </summary>
        /// <returns></returns>
        public void UpdateManagedSiteSettings()
        {
            var item = SelectedItem;
            var config = item.RequestConfig;
            var primaryDomain = item.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true);

            //if no primary domain need to go back and select one
            if (primaryDomain == null) throw new ArgumentException("Primary subject domain must be set.");

            config.PrimaryDomain = primaryDomain.Domain;

            //apply remaining selected domains as subject alternative names
            config.SubjectAlternativeNames =
                item.DomainOptions.Where(dm => dm.IsSelected == true)
                .Select(i => i.Domain)
                .ToArray();

            // TODO: config.EnableFailureNotifications = chkEnableNotifications.Checked;

            //determine if this site has an existing entry in Managed Sites, if so use that, otherwise start a new one
            if (SelectedItem.Id == null)
            {
                var siteInfo = SelectedWebSite;
                //if siteInfo null we need to go back and select a site

                item.Id = Guid.NewGuid().ToString() + ":" + siteInfo.SiteId;
                item.GroupId = siteInfo.SiteId;
            }

            item.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;
        }

        public async Task PopulateManagedSiteSettings(string siteId)
        {
            ValidationError = null;
            var managedSite = SelectedItem;
            managedSite.Name = SelectedWebSite.SiteName;
            managedSite.GroupId = SelectedWebSite.SiteId;

            //TODO: if this site would be a duplicate need to increment the site name

            //set defaults first
            managedSite.RequestConfig.WebsiteRootPath = Environment.ExpandEnvironmentVariables(SelectedWebSite.PhysicalPath);
            managedSite.RequestConfig.PerformExtensionlessConfigChecks = true;
            managedSite.RequestConfig.PerformTlsSniBindingConfigChecks = true;
            managedSite.RequestConfig.PerformChallengeFileCopy = true;
            managedSite.RequestConfig.PerformAutomatedCertBinding = true;
            managedSite.RequestConfig.PerformAutoConfig = true;
            managedSite.RequestConfig.EnableFailureNotifications = true;
            managedSite.RequestConfig.ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP;
            managedSite.IncludeInAutoRenew = true;
            managedSite.DomainOptions.Clear();

            var domainOptions = await GetDomainOptionsFromSite(siteId);
            foreach (var option in domainOptions)
            {
                managedSite.DomainOptions.Add(option);
            }

            if (!managedSite.DomainOptions.Any())
            {
                ValidationError = "The selected site has no domain bindings setup. Configure the domains first using Edit Bindings in IIS.";
            }

            //TODO: load settings from previously saved managed site?
            RaisePropertyChanged(nameof(PrimarySubjectDomain));
            RaisePropertyChanged(nameof(HasSelectedItemDomainOptions));
        }

        protected async virtual Task<IEnumerable<DomainOption>> GetDomainOptionsFromSite(string siteId)
        {
            return await CertifyClient.GetServerSiteDomains(StandardServerTypes.IIS, siteId);
        }

        public async Task BeginCertificateRequest(string managedItemId)
        {
            //begin request process
            var managedSite = ManagedSites.FirstOrDefault(s => s.Id == managedItemId);

            if (managedSite != null)
            {
                MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                //add request to observable list of progress state
                RequestProgressState progressState = new RequestProgressState(RequestState.Running, "Starting..", managedSite);

                //begin monitoring progress
                UpdateRequestTrackingProgress(progressState);

                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

                // start request
                var result = await CertifyClient.BeginCertificateRequest(managedSite.Id);
            }
        }

        public async void RenewAll(bool autoRenewalsOnly)
        {
            //FIXME: currently user can run renew all again while renewals are still in progress

            Dictionary<string, Progress<RequestProgressState>> itemTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            foreach (var s in ManagedSites)
            {
                if ((autoRenewalsOnly && s.IncludeInAutoRenew) || !autoRenewalsOnly)
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

            await CertifyClient.BeginAutoRenewal();

            // now continue to poll status of current request. should this just be a query for all
            // current requests?
        }

        public async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId)
        {
            return await CertifyClient.ReapplyCertificateBindings(managedItemId);
        }

        private async Task UpdatedCachedManagedSite(ManagedSite managedSite, bool reload = false)
        {
            var existing = ManagedSites.FirstOrDefault(i => i.Id == managedSite.Id);
            var newItem = managedSite;

            // optional reload managed site details (for refresh)
            if (reload) newItem = await CertifyClient.GetManagedSite(managedSite.Id);
            newItem.IsChanged = false;

            // update our cached copy of the managed site details
            if (existing != null)
            {
                var index = ManagedSites.IndexOf(existing);
                ManagedSites[index] = newItem;
            }
            else
            {
                ManagedSites.Add(newItem);
            }
        }

        public async Task<APIResult> TestChallengeResponse(ManagedSite managedSite)
        {
            return await CertifyClient.TestChallengeConfiguration(managedSite);
        }

        public async Task<APIResult> RevokeSelectedItem()
        {
            var managedSite = SelectedItem;
            return await CertifyClient.RevokeManageSiteCertificate(managedSite.Id);
        }

        private void UpdateRequestTrackingProgress(RequestProgressState state)
        {
            App.Current.Dispatcher.Invoke((Action)delegate
            {
                var existing = ProgressResults.FirstOrDefault(p => p.ManagedItem.Id == state.ManagedItem.Id);

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

                RaisePropertyChanged(nameof(HasRequestsInProgress));
                RaisePropertyChanged(nameof(ProgressResults));
            });
        }

        #endregion methods

        #region commands

        public ICommand SANSelectAllCommand => new RelayCommand<object>(SANSelectAll);
        public ICommand SANSelectNoneCommand => new RelayCommand<object>(SANSelectNone);
        public ICommand RenewAllCommand => new RelayCommand<bool>(RenewAll);

        #endregion commands
    }
}