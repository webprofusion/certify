using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Shared.Validation;
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

            // check for invalid primary domains (from previous RadioButton in DataGrid UI bug)
            if (SelectedItem?.DomainOptions.Count(d => d.IsPrimaryDomain) > 1)
            {
                HasInvalidPrimaryDomainConfig = true;
            }
            else
            {
                HasInvalidPrimaryDomainConfig = false;
            }

            RaisePropertyChangedEvent(nameof(HasInvalidPrimaryDomainConfig));

            // workaround - these should be happening automatically but we're currently having to
            // force them manually
            RaisePropertyChangedEvent(nameof(ChallengeConfigViewModels));

            RaisePropertyChangedEvent(nameof(DaysRemaining));
            RaisePropertyChangedEvent(nameof(DateNextRenewalDue));

            RaisePropertyChangedEvent(nameof(IsSelectedItemValid));

            RaisePropertyChangedEvent(nameof(SelectedItem));
            RaisePropertyChangedEvent(nameof(HasSelectedItemDomainOptions));
            RaisePropertyChangedEvent(nameof(HasSelectedItemWebsiteSelection));
            RaisePropertyChangedEvent(nameof(CertificateAuthorityDescription));

            RaisePropertyChangedEvent(nameof(StoredPasswords));
            RaisePropertyChangedEvent(nameof(CertificateAuthorities));

            RaisePropertyChangedEvent(nameof(IsEditable));
        }

        public string CertificateAuthorityDescription
        {
            get
            {
                if (SelectedItem != null)
                {
                    if (SelectedItem.CertificateAuthorityId == "")
                    {
                        SelectedItem.CertificateAuthorityId = null;
                    }

                    var ca = CertificateAuthorities.FirstOrDefault(c => c.Id == SelectedItem.CertificateAuthorityId);
                    return ca?.Description.AsNullWhenBlank() ?? "(CA Unknown)";
                }
                else
                {
                    return "None";
                }
            }
        }

        internal async Task RefreshWebsiteList()
        {
            var selectedWebsiteId = SelectedWebSite?.Id;

            IsSiteListQueryProgress = true;

            var list = await _appViewModel.GetServerSiteList(TargetServerType);

            list.Insert(0, new SiteInfo { Name = "(No Site Selected)", Id = "" });

            if (WebSiteList == null)
            {
                WebSiteList = new ObservableCollection<SiteInfo>();
            }

            WebSiteList.Clear();

            list.ForEach(i => WebSiteList.Add(i));

            IsSiteListQueryProgress = false;

            // restore 
            SelectedWebSite = WebSiteList.FirstOrDefault(s => s.Id == selectedWebsiteId);
            RaisePropertyChangedEvent(nameof(WebSiteList));

        }

        /// <summary>
        /// List of websites from the selected web server (if any) 
        /// </summary>
        public ObservableCollection<SiteInfo> WebSiteList { get; set; } = new ObservableCollection<SiteInfo>();

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

        public StandardServerTypes TargetServerType { get; set; } = StandardServerTypes.IIS;  // TODO: should be dynamic based on server we are connected to

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

        public bool IsNameEditMode { get; set; }
        public bool IsTestInProgress { get; set; }
        public bool IsSiteListQueryProgress { get; set; }

        public bool HasInvalidPrimaryDomainConfig { get; set; }

        [DependsOn(nameof(SelectedItem))]
        public bool IsEditable
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedItem?.SourceId))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
        public ManagedCertificate SelectedItem
        {
            get => _appViewModel.SelectedItem;
            set => _appViewModel.SelectedItem = value;
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
#pragma warning disable CS0618 // Type or member is obsolete
                                ChallengeType = SelectedItem.RequestConfig.ChallengeType
#pragma warning restore CS0618 // Type or member is obsolete
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

        private ObservableCollection<CertificateAuthority> _certificateAuthorities = new ObservableCollection<CertificateAuthority>();
        [DependsOn("_appViewModel.CertificateAuthorities")]
        public IEnumerable<CertificateAuthority> CertificateAuthorities
        {
            get
            {
                // binding directly to the main CA list causes combobox selected value binding to reset, so we maintain a copy of the collection
                var caList = _appViewModel.CertificateAuthorities?.Where(c => c.IsEnabled == true).ToList();

                if (_certificateAuthorities.Count == 0)
                {
                    _certificateAuthorities.Insert(0, new CertificateAuthority
                    {
                        Id = "(Empty)",
                        Title = "Auto",
                        Description = "The Certificate Authority will be automatically selected based on compatibility and the configured ACME accounts."
                    });

                    if (caList != null)
                    {
                        foreach (var a in caList)
                        {
                            _certificateAuthorities.Add(a);
                        }
                    }
                }
                else if (caList != null)
                {
                    // add new items
                    foreach (var a in caList)
                    {
                        if (!_certificateAuthorities.Any(c => c.Id == a.Id))
                        {
                            _certificateAuthorities.Add(a);
                        }
                    }
                }

                return _certificateAuthorities;
            }
        }

        private ObservableCollection<StoredCredential> _storedPasswords = new ObservableCollection<StoredCredential>();

        [DependsOn("_appViewModel.StoredCredentials")]
        public IEnumerable<Models.Config.StoredCredential> StoredPasswords
        {
            get
            {
                var list = _appViewModel.StoredCredentials?.Where(c => c.ProviderType == StandardAuthTypes.STANDARD_AUTH_PASSWORD).ToList();

                if (_storedPasswords.Count == 0)
                {
                    _storedPasswords.Insert(0, new Models.Config.StoredCredential
                    {
                        StorageKey = "(Empty)",
                        Title = "(No Password)",
                        ProviderType = StandardAuthTypes.STANDARD_AUTH_PASSWORD
                    });

                    if (list != null)
                    {
                        foreach (var a in list)
                        {
                            _storedPasswords.Add(a);
                        }
                    }
                }
                else if (list != null)
                {
                    // add new items
                    foreach (var p in list)
                    {
                        if (!_storedPasswords.Any(c => c.StorageKey == p.StorageKey))
                        {
                            _storedPasswords.Add(p);
                        }
                    }
                }

                return _storedPasswords;
            }
        }

        internal async Task<bool> SaveManagedCertificateChanges()
        {

            var updatedOK = await _appViewModel.AddOrUpdateManagedCertificate(SelectedItem);

            if (updatedOK)
            {
                SelectedItem.IsChanged = false;
            }

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

        public SiteInfo SelectedWebSite
        {
            get; set;
        }

        public int? DaysRemaining
        {
            get
            {
                if (SelectedItem != null && SelectedItem.DateExpiry.HasValue)
                {
                    return (int)(SelectedItem.DateExpiry - DateTime.Now).Value.TotalDays;
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
                    if (SelectedItem.DateExpiry != null && _appViewModel.Preferences?.RenewalIntervalMode == RenewalIntervalModes.DaysBeforeExpiry)
                    {
                        // Start renewing N days before expiry 
                        return SelectedItem.DateExpiry.Value.AddDays(-Preferences.RenewalIntervalDays);
                    }
                    else
                    {
                        // days since last renewal + preferred interval
                        return SelectedItem.DateRenewed.Value.AddDays(Preferences.RenewalIntervalDays);
                    }
                }

                return null;
            }
        }

        public ObservableCollection<StatusMessage> ConfigCheckResults
        {
            get; set;
        }

        public string ValidationError { get; set; }

        public bool IsAdvancedView { get; set; }

        public bool IsSelectedItemValid => SelectedItem?.Id != null && !SelectedItem.IsChanged;

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
                return new ManagedCertificateViewModelDesign();
            }
        }

        public async Task<bool> ConfirmDiscardUnsavedChanges()
        {
            if (SelectedItem?.IsChanged ?? false)
            {
                if (SelectedItem.SourceId != null)
                {
                    // changes to external items are auto discarded
                    return true;
                }

                //user needs to save or discard changes before changing selection
                if (MessageBox.Show(SR.ManagedCertificates_UnsavedWarning, SR.Alert, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
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

        public void SANSelectAll(object o) => SelectedItem?.DomainOptions.ToList().ForEach(opt => opt.IsSelected = true);

        public void SANSelectNone(object o) => SelectedItem?.DomainOptions.ToList().ForEach(opt => opt.IsSelected = false);

        public async Task<bool> SANRefresh()
        {
            //requery list of domains from IIS and refresh Domain Options in Selected Item, leave existing items checked
            if (SelectedItem != null)
            {
                var opts = await GetDomainOptionsFromSite(SelectedItem.ServerSiteId);

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

        public ValidationResult Validate(bool applyAutoConfiguration)
        {

            var caId = Preferences.DefaultCertificateAuthority.WithDefault(StandardCertAuthorities.LETS_ENCRYPT);
            if (SelectedItem.CertificateAuthorityId != null)
            {
                caId = SelectedItem.CertificateAuthorityId;
            }

            var preferredCA = AppViewModel.Current.CertificateAuthorities.FirstOrDefault(c => c.Id == caId);

            var result = CertificateEditorService.Validate(SelectedItem, SelectedWebSite, preferredCA, applyAutoConfiguration);

            // auto selected name edit mode if vaidation of name fails
            IsNameEditMode = false;

            if (result.ErrorCode == ValidationErrorCodes.REQUIRED_NAME.ToString())
            {
                IsNameEditMode = true;
            }
            else
            {
                IsNameEditMode = false;
            }

            return result;
        }

        public async Task PopulateManagedCertificateSettings(string siteId)
        {
            ValidationError = null;
            var domainOptions = await GetDomainOptionsFromSite(siteId);

            var result = CertificateEditorService.PopulateFromSiteInfo(SelectedItem, SelectedWebSite, domainOptions);

            if (!result.IsSuccess)
            {
                ValidationError = result.Message;
            }
            else
            {
                SelectedItem = result.Result;
            }

            RaiseSelectedItemChanges();
        }

        public bool UpdateDomainOptions(string domains)
        {
            var item = SelectedItem;
            var result = CertificateEditorService.AddDomainOptionsFromString(item, domains);

            RaiseSelectedItemChanges();

            if (result.wildcardAdded && !SelectedItem.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS))
            {
                // wildcard added but no DNS challenges exist yet
                MessageBox.Show("You have added a wildcard domain, you will also need to configure a corresponding DNS challenge under Authorization. ");
            }

            if (result.wildcardAdded)
            {
                //if a wildcard was added but the non-wildcard domain has not yet been added, offer to add it
                var wildcardOnlyDomains = result.domainList.Where(d => d.StartsWith("*.") && !item.DomainOptions.Any(o => o.Domain == d.Replace("*.", "")));
                if (wildcardOnlyDomains.Any())
                {
                    var msg = $"You had added wildcard domains without the corresponding non-wildcard version: {string.Join(",", wildcardOnlyDomains)}. Would you like to add the non-wildcard versions as well?";
                    if (MessageBox.Show(msg, "Add non-wildcard equivalent domains?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {

                        var addedDomains = string.Join(";", wildcardOnlyDomains);
                        addedDomains = addedDomains.Replace("*.", "");
                        UpdateDomainOptions(addedDomains);
                    }
                }
            }

            // all ok or nothing to do
            return true;
        }

        protected virtual async Task<IEnumerable<DomainOption>> GetDomainOptionsFromSite(string siteId)
        {
            if (string.IsNullOrEmpty(siteId))
            {
                return new List<DomainOption>();
            }

            var list = await _appViewModel.GetServerSiteDomains(TargetServerType, siteId);

            // discard non-specific host wildcards for cert domain options
            list.RemoveAll(d => d.Domain?.Trim() == "*");

            return list;
        }

        public async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly, bool includeDeploymentTasks) => await _appViewModel.ReapplyCertificateBindings(managedItemId, isPreviewOnly, includeDeploymentTasks);

        public async Task<CertificateRequestResult> RefetchCertificate(string managedItemId) => await _appViewModel.RefetchCertificate(managedItemId);

        public async Task<List<StatusMessage>> TestChallengeResponse(ManagedCertificate managedCertificate) => await _appViewModel.TestChallengeConfiguration(managedCertificate);

        public async Task<StatusMessage> RevokeSelectedItem()
        {
            var managedCertificate = SelectedItem;
            return await _appViewModel.RevokeManageSiteCertificate(managedCertificate.Id);
        }

        public ICommand SANSelectAllCommand => new RelayCommand<object>(SANSelectAll);
        public ICommand SANSelectNoneCommand => new RelayCommand<object>(SANSelectNone);
    }
}
