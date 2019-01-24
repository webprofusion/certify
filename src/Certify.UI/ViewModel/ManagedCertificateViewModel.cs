using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using Certify.Locales;
using Certify.Models;
using Certify.Shared.Utils;
using PropertyChanged;

namespace Certify.UI.ViewModel
{
    public class ManagedCertificateViewModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>
        //public static AppModel AppViewModel = new DesignViewModel(); // for UI testing
        public static ManagedCertificateViewModel Current = ManagedCertificateViewModel.GetModel();

        private Certify.UI.ViewModel.AppViewModel _appViewModel => ViewModel.AppViewModel.Current;

        public ManagedCertificateViewModel()
        {
            _appViewModel.PropertyChanged += _appViewModel_PropertyChanged;
        }

        private void _appViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_appViewModel.SelectedItem))
            {
                RaiseSelectedItemChanges();
            }
        }

        public void RaiseSelectedItemChanges()
        {
            // workaround - these should be happening automatically but we're currently having to
            // force them manually
            RaisePropertyChangedEvent(nameof(ChallengeConfigViewModels));
            RaisePropertyChangedEvent(nameof(SelectedItemLogEntries));

            RaisePropertyChangedEvent(nameof(DaysRemaining));
            RaisePropertyChangedEvent(nameof(DateNextRenewalDue));

            RaisePropertyChangedEvent(nameof(IsSelectedItemValid));
            RaisePropertyChangedEvent(nameof(SelectedItem));
            RaisePropertyChangedEvent(nameof(HasSelectedItemDomainOptions));
            RaisePropertyChangedEvent(nameof(HasSelectedItemWebsiteSelection));
        }



        internal async Task RefreshWebsiteList()
        {
            var list = await _appViewModel.CertifyClient.GetServerSiteList(StandardServerTypes.IIS);
            list.Insert(0, new BindingInfo { SiteName = "(No IIS Website Selected)", SiteId = "" });
            this.WebSiteList = new ObservableCollection<BindingInfo>(list);
        }

        /// <summary>
        /// List of websites from the selected web server (if any) 
        /// </summary>
        public ObservableCollection<BindingInfo> WebSiteList { get; set; } = new ObservableCollection<BindingInfo>();

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

        [DependsOn(nameof(SelectedItem))]
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

        public ManagedCertificate SelectedItem
        {
            get
            {
                return _appViewModel.SelectedItem;
            }
            set { _appViewModel.SelectedItem = value; }
        }

        public ObservableCollection<ChallengeConfigItemViewModel> ChallengeConfigViewModels
        {
            get
            {
                if (SelectedItem != null)
                {
                    if (SelectedItem.RequestConfig.Challenges == null || !SelectedItem.RequestConfig.Challenges.Any())
                    {
                        // populate challenge config info
                        SelectedItem.RequestConfig.Challenges = new ObservableCollection<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig
                            {
                                ChallengeType = SelectedItem.RequestConfig.ChallengeType
                            }
                        };
                    }

                    return new ObservableCollection<ChallengeConfigItemViewModel>(

                      SelectedItem.RequestConfig.Challenges.Select(c => new ChallengeConfigItemViewModel(c))

                      );
                }
                else
                {
                    return new ObservableCollection<ChallengeConfigItemViewModel> { };
                }
            }
        }

        internal async Task<bool> SaveManagedCertificateChanges()
        {
            UpdateManagedCertificateSettings();

            var updatedOK = await _appViewModel.AddOrUpdateManagedCertificate(SelectedItem);

            if (updatedOK) SelectedItem.IsChanged = false;

            RaiseSelectedItemChanges();

            return updatedOK;
        }

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

        public BindingInfo SelectedWebSite
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

        [DependsOn(nameof(SelectedItem))]
        public List<string> SelectedItemLogEntries
        {
            get
            {
                if (SelectedItem != null && SelectedItem.Id != null)
                {
                    try
                    {
                        var logPath = ManagedCertificateLog.GetLogPath(SelectedItem.Id);
                        var logEntries = System.IO.File.ReadAllLines(logPath);
                        return logEntries.Reverse().Take(50).ToList();
                    }
                    catch
                    {
                    }
                }
                return new List<string> { "Could not retrieve log entries." };
            }
        }

        public int? DaysRemaining
        {
            get
            {
                if (SelectedItem != null && SelectedItem.DateExpiry.HasValue)
                {
                    return (int)Math.Abs((DateTime.Now - SelectedItem.DateExpiry).Value.TotalDays);
                }

                return null;
            }
        }

        public DateTime? DateNextRenewalDue
        {
            get
            {
                // for the simplest version based on preference for renewal interval this is
                // DateRenewed + Interval more complicated would be based on last renewal attempt and
                // number of attempts so far etc
                if (SelectedItem != null && SelectedItem.DateRenewed.HasValue)
                {
                    return SelectedItem.DateRenewed.Value.AddDays(Preferences.RenewalIntervalDays);
                }
                return null;
            }
        }

        public ObservableCollection<StatusMessage> ConfigCheckResults
        {
            get; set;
        }

        public string ValidationError { get; set; }

        public bool IsAdvancedView { get; set; } = false;

        public bool IsSelectedItemValid
        {
            get => SelectedItem?.Id != null && !SelectedItem.IsChanged;
        }

        public Preferences Preferences => _appViewModel.Preferences;

        public static ManagedCertificateViewModel GetModel()
        {
            var stack = new System.Diagnostics.StackTrace();
            if (stack.GetFrames().Last().GetMethod().Name == "Main")
            {
                return new ManagedCertificateViewModel();
            }
            else
            {
                //TODO: design view model
                return new ManagedCertificateDesignViewModel();
            }
        }

        public async Task<bool> ConfirmDiscardUnsavedChanges()
        {
            if (SelectedItem?.IsChanged ?? false)
            {
                //user needs to save or discard changes before changing selection
                if (MessageBox.Show(SR.ManagedCertificates_UnsavedWarning, SR.Alert, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
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
                    await _appViewModel.UpdatedCachedManagedCertificate(SelectedItem, reload: true);
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
        public void UpdateManagedCertificateSettings(bool throwOnInvalidSettings = true)
        {
            var item = SelectedItem;
            var config = item.RequestConfig;
            var primaryDomain = item.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true);

            if (primaryDomain == null)
            {
                if (item.DomainOptions.Any()) item.DomainOptions[0].IsPrimaryDomain = true;
            }

            //if no primary domain need to go back and select one
            if (primaryDomain == null && throwOnInvalidSettings) throw new ArgumentException("Primary subject domain must be set.");

            if (primaryDomain != null)
            {
                if (config.PrimaryDomain != primaryDomain.Domain)
                {
                    config.PrimaryDomain = primaryDomain.Domain.Trim();
                }
            }

            //apply remaining selected domains as subject alternative names
            var sanList =
                item.DomainOptions.Where(dm => dm.IsSelected == true)
                .Select(i => i.Domain)
                .ToArray();

            if (config.SubjectAlternativeNames == null ||
                !sanList.SequenceEqual(config.SubjectAlternativeNames))
            {
                config.SubjectAlternativeNames = sanList;
            }

            //determine if this site has an existing entry in  Managed Certificates, if so use that, otherwise start a new one
            if (SelectedItem.Id == null)
            {
                item.Id = Guid.NewGuid().ToString();

                // optionally append webserver site ID (if used)
                if (SelectedWebSite != null)
                {
                    item.Id += ":" + SelectedWebSite.SiteId;
                    item.GroupId = SelectedWebSite.SiteId;
                    item.ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS;
                }
                else
                {
                    item.ItemType = ManagedCertificateType.SSL_LetsEncrypt_Manual;
                }
            }
        }

        public async Task PopulateManagedCertificateSettings(string siteId)
        {
            ValidationError = null;
            var managedCertificate = SelectedItem;

            if (SelectedWebSite != null)
            {
                if (managedCertificate.GroupId != SelectedWebSite.SiteId)
                {
                    // update website association
                    managedCertificate.GroupId = SelectedWebSite.SiteId;

                    // if not already set, use website name as default name
                    if (managedCertificate.Id == null || String.IsNullOrEmpty(managedCertificate.Name))
                    {
                        if (!String.IsNullOrEmpty(SelectedWebSite.SiteName))
                        {
                            managedCertificate.Name = SelectedWebSite.SiteName;
                        }
                    }

                    // remove domain options not manually added
                    foreach (var d in managedCertificate.DomainOptions.ToList())
                    {
                        if (!d.IsManualEntry)
                        {
                            managedCertificate.DomainOptions.Remove(d);
                        }
                    }

                    var domainOptions = await GetDomainOptionsFromSite(siteId);
                    foreach (var option in domainOptions)
                    {
                        managedCertificate.DomainOptions.Add(option);
                    }

                    if (!managedCertificate.DomainOptions.Any())
                    {
                        ValidationError = "The selected site has no domain bindings setup. Configure the domains first using Edit Bindings in IIS.";
                    }

                    RaiseSelectedItemChanges();
                }
                else
                {
                    RaiseSelectedItemChanges();
                    // same website selection RaisePropertyChanged(nameof(PrimarySubjectDomain)); RaisePropertyChanged(nameof(HasSelectedItemDomainOptions));
                }
            }
        }

        public bool UpdateDomainOptions(string domains)
        {
            var item = SelectedItem;
            var wildcardAdded = false;

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

                                if (option.Domain.StartsWith("*."))
                                {
                                    wildcardAdded = true;
                                }
                            }
                            else
                            {
                                invalidDomains += domain + "\n";
                            }
                        }
                    }
                }

                RaiseSelectedItemChanges();

                if (!String.IsNullOrEmpty(invalidDomains))
                {
                    MessageBox.Show("Invalid domains: " + invalidDomains);
                    return false;
                }

                if (wildcardAdded && !SelectedItem.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS))
                {
                    // wildcard added but no DNS challenges exist yet
                    MessageBox.Show("You have added a wildcard domain, you will also need to configure a corresponding DNS challenge under Authorization. ");
                }

                if (wildcardAdded)
                {
                    //if a wildcard was added but the non-wildcard domain has not yet been added, offer to add it
                    var wildcardOnlyDomains = domainList.Where(d => d.StartsWith("*.") && !item.DomainOptions.Any(o => o.Domain == d.Replace("*.", "")));
                    if (wildcardOnlyDomains.Any())
                    {
                        var msg = $"You had added wildcard domains without the corresponding non-wildcard version: {string.Join(",", wildcardOnlyDomains)}. Would you like to add the non-wildcard versions as well?";
                        if (MessageBox.Show(msg,"Add non-wildcard equivalent domains?", MessageBoxButtons.YesNo)== DialogResult.Yes)
                        {

                            var addedDomains = string.Join(";", wildcardOnlyDomains);
                            addedDomains = addedDomains.Replace("*.", "");
                            UpdateDomainOptions(addedDomains);
                        }
                    }
                }
            }

            // all ok or nothing to do
            return true;
        }

        protected async virtual Task<IEnumerable<DomainOption>> GetDomainOptionsFromSite(string siteId)
        {
            if (String.IsNullOrEmpty(siteId))
            {
                return new List<DomainOption>();
            }

            return await _appViewModel.CertifyClient.GetServerSiteDomains(StandardServerTypes.IIS, siteId);
        }

        public async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly)
        {
            return await _appViewModel.CertifyClient.ReapplyCertificateBindings(managedItemId, isPreviewOnly);
        }

        public async Task<List<StatusMessage>> TestChallengeResponse(ManagedCertificate managedCertificate)
        {
            return await _appViewModel.CertifyClient.TestChallengeConfiguration(managedCertificate);
        }

        public async Task<StatusMessage> RevokeSelectedItem()
        {
            var managedCertificate = SelectedItem;
            return await _appViewModel.CertifyClient.RevokeManageSiteCertificate(managedCertificate.Id);
        }

        public ICommand SANSelectAllCommand => new RelayCommand<object>(SANSelectAll);
        public ICommand SANSelectNoneCommand => new RelayCommand<object>(SANSelectNone);
    }
}
