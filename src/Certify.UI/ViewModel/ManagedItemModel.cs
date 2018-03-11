using Certify.Locales;
using Certify.Models;
using Certify.Models.Config;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;

namespace Certify.UI.ViewModel
{
    public class ManagedItemModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>
        //public static AppModel AppViewModel = new DesignViewModel(); // for UI testing
        public static ManagedItemModel Current = ManagedItemModel.GetModel();

        private Certify.UI.ViewModel.AppModel _appViewModel => ViewModel.AppModel.Current;

        public ManagedItemModel()
        {
        }

        internal async Task RefreshWebsiteList()
        {
            this.WebSiteList = new ObservableCollection<SiteBindingItem>(await _appViewModel.CertifyClient.GetServerSiteList(StandardServerTypes.IIS));
        }

        public ObservableCollection<StoredCredential> StoredCredentials { get; set; }

        /// <summary>
        /// List of websites from the selected web server (if any) 
        /// </summary>
        public ObservableCollection<SiteBindingItem> WebSiteList { get; set; } = new ObservableCollection<SiteBindingItem>();

        public bool HasSelectedItemWebsiteSelection
        {
            get
            {
                if (SelectedItem != null && SelectedItem.GroupId != null)
                {
                    return true;
                }
                else
                {
                    return false;
                }
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

        public bool IsTestInProgress { get; set; }

        public ManagedSite SelectedItem
        {
            get
            {
                return _appViewModel.SelectedItem;
            }
            set { _appViewModel.SelectedItem = value; }
        }

        internal async Task<bool> SaveManagedItemChanges()
        {
            UpdateManagedSiteSettings();

            var updatedOK = await _appViewModel.AddOrUpdateManagedSite(SelectedItem);

            if (updatedOK) SelectedItem.IsChanged = false;

            RaisePropertyChanged(nameof(IsSelectedItemValid));
            RaisePropertyChanged(nameof(SelectedItem));

            return updatedOK;
        }

        // Certify-supported challenge types
        public IEnumerable<string> ChallengeTypes { get; set; } = new string[] {
            SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
            SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
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
            get { return SelectedItem?.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain); }
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

        public string ValidationError { get; set; }

        public bool IsAdvancedView { get; set; } = false;

        public bool IsSelectedItemValid
        {
            get => SelectedItem?.Id != null && !SelectedItem.IsChanged;
        }

        public Preferences Preferences => _appViewModel.Preferences;

        public static ManagedItemModel GetModel()
        {
            var stack = new System.Diagnostics.StackTrace();
            if (stack.GetFrames().Last().GetMethod().Name == "Main")
            {
                return new ManagedItemModel();
            }
            else
            {
                //TODO: design view model
                return new DesignItemViewModel();
            }
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
                    await _appViewModel.UpdatedCachedManagedSite(SelectedItem, reload: true);
                }
            }
        }

        public void SANSelectAll(object o)
        {
            SelectedItem?.DomainOptions.ToList().ForEach(opt => opt.IsSelected = true);
        }

        public void SANSelectNone(object o)
        {
            SelectedItem?.DomainOptions.ToList().ForEach(opt => opt.IsSelected = false);
        }

        public async Task<bool> SANRefresh()
        {
            //requery list of domains from IIS and refresh Domain Options in Selected Item, leave existing items checked
            if (SelectedItem != null)
            {
                var opts = await GetDomainOptionsFromSite(SelectedItem.GroupId);
                if (opts != null && opts.Any())
                {
                    //reselect options
                    foreach (var currentOpt in SelectedItem?.DomainOptions)
                    {
                        opts.Where(opt => opt.Domain == currentOpt.Domain).ToList().ForEach(opt =>
                        {
                            if (currentOpt.IsPrimaryDomain)
                            {
                                opt.IsPrimaryDomain = currentOpt.IsPrimaryDomain;
                                opt.IsSelected = true;
                            }
                            else
                            {
                                opt.IsSelected = currentOpt.IsSelected;
                            }
                        });
                    }

                    SelectedItem.DomainOptions = new ObservableCollection<DomainOption>(opts);
                }
            }
            return true;
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

            config.PrimaryDomain = primaryDomain.Domain.Trim();

            //apply remaining selected domains as subject alternative names
            config.SubjectAlternativeNames =
                item.DomainOptions.Where(dm => dm.IsSelected == true)
                .Select(i => i.Domain)
                .ToArray();

            // TODO: config.EnableFailureNotifications = chkEnableNotifications.Checked;

            //determine if this site has an existing entry in Managed Sites, if so use that, otherwise start a new one
            if (SelectedItem.Id == null)
            {
                item.Id = Guid.NewGuid().ToString();

                // optionally append webserver site ID (if used)
                if (SelectedWebSite != null)
                {
                    item.Id += ":" + SelectedWebSite.SiteId;
                    item.GroupId = SelectedWebSite.SiteId;
                    item.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;
                }
                else
                {
                    item.ItemType = ManagedItemType.SSL_LetsEncrypt_Manual;
                }
            }
        }

        public async Task PopulateManagedSiteSettings(string siteId)
        {
            ValidationError = null;
            var managedSite = SelectedItem;

            if (SelectedWebSite != null)
            {
                // update website association
                managedSite.GroupId = SelectedWebSite.SiteId;

                // if not already set, use website name as default name
                if (managedSite.Id == null || String.IsNullOrEmpty(managedSite.Name))
                {
                    managedSite.Name = SelectedWebSite.SiteName;

                    //set defaults first
                    managedSite.RequestConfig.WebsiteRootPath = Environment.ExpandEnvironmentVariables(SelectedWebSite.PhysicalPath);
                }
            }

            //TODO: if this site would be a duplicate need to increment the site name

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

        public bool UpdateDomainOptions(string domains)
        {
            var item = SelectedItem;

            // parse text input to add as manual domain options

            if (!string.IsNullOrEmpty(domains))
            {
                var domainList = domains.Split(",; ".ToCharArray());
                string invalidDomains = "";
                foreach (var d in domainList)
                {
                    if (!string.IsNullOrEmpty(d.Trim()))
                    {
                        var domain = d.ToLower().Trim();
                        if (!item.DomainOptions.Any(o => o.Domain == domain))
                        {
                            var option = new DomainOption
                            {
                                Domain = domain,
                                IsManualEntry = true,
                                IsSelected = true
                            };

                            if (Uri.CheckHostName(domain) == UriHostNameType.Dns || (domain.StartsWith("*.") && Uri.CheckHostName(domain.Replace("*.", "")) == UriHostNameType.Dns))
                            {
                                // preselect first item as primary domain
                                if (item.DomainOptions.Count == 0) option.IsPrimaryDomain = true;

                                item.DomainOptions.Add(option);
                            }
                            else
                            {
                                invalidDomains += domain + "\n";
                            }
                        }
                    }
                }

                RaisePropertyChanged(nameof(HasSelectedItemDomainOptions));

                if (!String.IsNullOrEmpty(invalidDomains))
                {
                    MessageBox.Show("Invalid domains: " + invalidDomains);
                    return false;
                }
            }

            // all ok or nothing to do
            return true;
        }

        protected async virtual Task<IEnumerable<DomainOption>> GetDomainOptionsFromSite(string siteId)
        {
            return await _appViewModel.CertifyClient.GetServerSiteDomains(StandardServerTypes.IIS, siteId);
        }

        public async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly)
        {
            return await _appViewModel.CertifyClient.ReapplyCertificateBindings(managedItemId, isPreviewOnly);
        }

        public async Task<StatusMessage> TestChallengeResponse(ManagedSite managedSite)
        {
            return await _appViewModel.CertifyClient.TestChallengeConfiguration(managedSite);
        }

        public async Task<StatusMessage> RevokeSelectedItem()
        {
            var managedSite = SelectedItem;
            return await _appViewModel.CertifyClient.RevokeManageSiteCertificate(managedSite.Id);
        }

        public ICommand SANSelectAllCommand => new RelayCommand<object>(SANSelectAll);
        public ICommand SANSelectNoneCommand => new RelayCommand<object>(SANSelectNone);
    }
}