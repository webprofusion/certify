using Certify.Management;
using Certify.Models;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Certify.UI.ViewModel
{
    [ImplementPropertyChanged]
    public class AppModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers
        /// </summary>
        public static AppModel AppViewModel = new AppModel();

        private CertifyManager certifyManager = null;

        #region properties

        /// <summary>
        /// List of all the sites we currently manage
        /// </summary>
        public ObservableCollection<Certify.Models.ManagedSite> ManagedSites { get; set; }

        public Certify.Models.ManagedSite SelectedItem { get; set; }

        public bool SelectedItemHasChanges
        {
            get
            {
                if (this.SelectedItem != null)
                {
                    if (this.SelectedItem.IsChanged || (this.SelectedItem.RequestConfig != null && this.SelectedItem.RequestConfig.IsChanged) || (this.SelectedItem.DomainOptions != null && this.SelectedItem.DomainOptions.Any(d => d.IsChanged)))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool ShowOnlyStartedSites { get; set; } = false;

        public List<SiteBindingItem> WebSiteList
        {
            get
            {
                //get list of sites from IIS
                var iisManager = new IISManager();
                return iisManager.GetPrimarySites(ShowOnlyStartedSites);
            }
        }

        internal void SaveManagedItemChanges()
        {
            SelectedItem = GetUpdatedManagedSiteSettings();
            AddOrUpdateManagedSite(SelectedItem);

            MarkAllChangesCompleted();
        }

        public List<IPAddress> HostIPAddresses
        {
            get
            {
                try
                {
                    //return list of ipv4 network IPs
                    IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                    return hostEntry.AddressList.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList();
                }
                catch (Exception)
                {
                    //return empty list
                    return new List<IPAddress>();
                }
            }
        }

        /// <summary>
        /// Reset all IsChanged flags for the Selected Item
        /// </summary>
        internal void MarkAllChangesCompleted()
        {
            SelectedItem.IsChanged = false;
            SelectedItem.RequestConfig.IsChanged = false;
            SelectedItem.DomainOptions.ForEach(d => d.IsChanged = false);

            RaisePropertyChanged(nameof(SelectedItemHasChanges));
        }

        internal void SelectFirstOrDefaultItem()
        {
            SelectedItem = ManagedSites.FirstOrDefault();
        }

        public SiteBindingItem SelectedWebSite
        {
            get; set;
        }

        public DomainOption PrimarySubjectDomain
        {
            get
            {
                if (SelectedItem != null)
                {
                    var primary = SelectedItem.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true);
                    if (primary != null)
                    {
                        return primary;
                    }
                }

                return null;
            }

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

        /// <summary>
        /// Determine if user should be able to choose/change the Website for the current SelectedItem
        /// </summary>
        public bool IsWebsiteSelectable
        {
            get
            {
                if (SelectedItem != null && SelectedItem.Id == null)
                {
                    return true;
                }
                return false;
            }
        }

        public bool IsItemSelected
        {
            get
            {
                return (this.SelectedItem != null);
            }
        }

        public bool IsNoItemSelected
        {
            get
            {
                return (this.SelectedItem == null);
            }
        }

        public bool IsSelectedItemValid
        {
            get
            {
                if (this.SelectedItem != null && this.SelectedItem.Id != null && this.SelectedItem.IsChanged == false)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
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

        #endregion properties

        #region methods

        public AppModel()
        {
            certifyManager = new CertifyManager();

            ProgressResults = new ObservableCollection<RequestProgressState>();
        }

        public void LoadSettings()
        {
            this.ManagedSites = new ObservableCollection<ManagedSite>(certifyManager.GetManagedSites());

            /*if (this.ManagedSites.Any())
            {
                //preselect the first managed site
                //  this.SelectedItem = this.ManagedSites[0];

                //test state
                BeginTrackingProgress(new RequestProgressState { CurrentState = RequestState.InProgress, IsStarted = true, Message = "Registering Domain Identifier", ManagedItem = ManagedSites[0] });
                BeginTrackingProgress(new RequestProgressState { CurrentState = RequestState.Error, IsStarted = true, Message = "Rate Limited", ManagedItem = ManagedSites[0] });
            }*/
        }

        public void SaveSettings(object param)
        {
            certifyManager.SaveManagedSites(this.ManagedSites.ToList());
        }

        public async void RenewAll(bool autoRenewalsOnly)
        {
            Dictionary<string, Progress<RequestProgressState>> itemTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            foreach (var s in ManagedSites)
            {
                if ((autoRenewalsOnly && s.IncludeInAutoRenew) || !autoRenewalsOnly)
                {
                    var progressState = new RequestProgressState { ManagedItem = s };
                    itemTrackers.Add(s.Id, new Progress<RequestProgressState>(progressState.ProgressReport));

                    //begin monitoring progress
                    BeginTrackingProgress(progressState);
                }
            }

            var results = await certifyManager.PerformRenewalAllManagedSites(autoRenewalsOnly, itemTrackers);
            //TODO: store results in log
            //return results;
        }

        public ManagedItem AddOrUpdateManagedSite(ManagedSite item)
        {
            var existing = this.ManagedSites.FirstOrDefault(s => s.Id == item.Id);

            //add new or replace existing

            if (existing != null)
            {
                this.ManagedSites.Remove(existing);
            }

            this.ManagedSites.Add(item);

            //save settings
            certifyManager.SaveManagedSites(this.ManagedSites.ToList());

            return item;
        }

        internal void DeleteManagedSite(ManagedSite selectedItem)
        {
            var existing = this.ManagedSites.FirstOrDefault(s => s.Id == selectedItem.Id);

            //remove existing

            if (existing != null)
            {
                this.ManagedSites.Remove(existing);
            }

            //save settings
            certifyManager.SaveManagedSites(this.ManagedSites.ToList());
        }

        public void SANSelectAll(object o)
        {
            if (this.SelectedItem != null && this.SelectedItem.DomainOptions != null)
            {
                this.SelectedItem.DomainOptions.ForEach(d => d.IsSelected = true);
            }
        }

        public void SANSelectNone(object o)
        {
            if (this.SelectedItem != null && this.SelectedItem.DomainOptions != null)
            {
                this.SelectedItem.DomainOptions.ForEach(d => d.IsSelected = false);
            }
        }

        /// <summary>
        /// For the given set of options get a new CertRequestConfig to store
        /// </summary>
        /// <returns></returns>
        private ManagedSite GetUpdatedManagedSiteSettings()
        {
            var item = SelectedItem;
            var config = item.RequestConfig;

            // RefreshDomainOptionSettingsFromUI();
            var primaryDomain = item.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true);

            //if no primary domain need to go back and select one
            if (primaryDomain == null) throw new ArgumentException("Primary subject domain must be set.");

            var _idnMapping = new System.Globalization.IdnMapping();
            config.PrimaryDomain = _idnMapping.GetAscii(primaryDomain.Domain); // ACME service requires international domain names in ascii mode

            //apply remaining selected domains as subject alternative names
            config.SubjectAlternativeNames =
                item.DomainOptions.Where(dm => dm.IsSelected == true)
                .Select(i => i.Domain)
                .ToArray();

            //config.PerformChallengeFileCopy = true;
            //config.PerformExtensionlessConfigChecks = !chkSkipConfigCheck.Checked;
            config.PerformAutoConfig = true;

            // config.EnableFailureNotifications = chkEnableNotifications.Checked;

            //determine if this site has an existing entry in Managed Sites, if so use that, otherwise start a new one

            if (SelectedItem.Id == null)
            {
                var siteInfo = SelectedWebSite;
                //if siteInfo null we need to go back and select a site

                item.Id = Guid.NewGuid().ToString() + ":" + siteInfo.SiteId;
                item.GroupId = siteInfo.SiteId;

                config.WebsiteRootPath = Environment.ExpandEnvironmentVariables(siteInfo.PhysicalPath);
            }

            item.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;

            //store domain options settings and request config for this site so we can replay for automated renewal

            //managedSite.RequestConfig = config;

            return item;
        }

        private void PopulateManagedSiteSettings(string siteId)
        {
            ValidationError = null;
            var managedSite = SelectedItem;
            managedSite.Name = SelectedWebSite.SiteName;

            //TODO: if this site would be a duplicate need to increment the site name

            //set defaults first
            managedSite.RequestConfig.PerformExtensionlessConfigChecks = true;
            managedSite.RequestConfig.PerformAutomatedCertBinding = true;
            managedSite.RequestConfig.PerformAutoConfig = true;
            managedSite.RequestConfig.EnableFailureNotifications = true;
            managedSite.RequestConfig.ChallengeType = "http-01";
            managedSite.IncludeInAutoRenew = true;
            managedSite.DomainOptions = new List<DomainOption>();

            //for the given selected web site, allow the user to choose which domains to combine into one certificate
            var allSites = new IISManager().GetSiteBindingList(false);
            var domains = new List<DomainOption>();
            foreach (var d in allSites)
            {
                if (d.SiteId == siteId)
                {
                    DomainOption opt = new DomainOption { Domain = d.Host, IsPrimaryDomain = false, IsSelected = true };
                    domains.Add(opt);
                }
            }

            if (domains.Any())
            {
                //mark first domain as primary, if we have no other settings
                if (!domains.Any(d => d.IsPrimaryDomain == true))
                {
                    domains[0].IsPrimaryDomain = true;
                }

                managedSite.DomainOptions = domains;

                //MainViewModel.EnsureNotifyPropertyChange(nameof(MainViewModel.PrimarySubjectDomain));
            }
            else
            {
                //TODO: command to show error in UI
                ValidationError = "The selected site has no domain bindings setup. Configure the domains first using Edit Bindings in IIS.";
            }

            //TODO: load settings from previously saved managed site?
        }

        public async void BeginCertificateRequest(string managedItemId)
        {
            //begin request process
            var managedSite = ManagedSites.FirstOrDefault(s => s.Id == managedItemId);

            if (managedSite != null)
            {
                MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                //add request to observable list of progress state
                RequestProgressState progressState = new RequestProgressState();
                progressState.ManagedItem = managedSite;

                //begin monitoring progress
                BeginTrackingProgress(progressState);

                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);
                await certifyManager.PerformCertificateRequest(null, managedSite, progressIndicator);
            }
        }

        private void BeginTrackingProgress(RequestProgressState state)
        {
            var existing = ProgressResults.FirstOrDefault(p => p.ManagedItem.Id == state.ManagedItem.Id);
            if (existing != null)
            {
                ProgressResults.Remove(existing);
            }
            ProgressResults.Add(state);

            RaisePropertyChanged(nameof(HasRequestsInProgress));
        }

        #endregion methods

        #region commands

        public ICommand SANSelectAllCommand => new RelayCommand<object>(SANSelectAll);
        public ICommand SANSelectNoneCommand => new RelayCommand<object>(SANSelectNone);
        public ICommand PopulateManagedSiteSettingsCommand => new RelayCommand<string>(PopulateManagedSiteSettings);
        public ICommand BeginCertificateRequestCommand => new RelayCommand<string>(BeginCertificateRequest);
        public ICommand RenewAllCommand => new RelayCommand<bool>(RenewAll);

        #endregion commands
    }
}