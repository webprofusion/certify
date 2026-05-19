using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Shared.Validation;
using Certify.Models.Utils;
using PropertyChanged;

namespace Certify.UI.ViewModel
{
    public class ManagedCertificateViewModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>
        public static ManagedCertificateViewModel Current = ManagedCertificateViewModel.GetModel();

        private Certify.UI.ViewModel.AppViewModel _appViewModel => ViewModel.AppViewModel.Current;

        public ManagedCertificateViewModel()
        {
        }

        public void RaiseSelectedItemChanges()
        {

            EnsureExternalSourceConfiguration();
            SyncSelectedExternalSourceDisplayInfo();

            // check for invalid primary domains (from previous RadioButton in DataGrid UI bug)
            if (SelectedItem?.DomainOptions.Count(d => d.IsPrimaryDomain) > 1)
            {
                HasInvalidPrimaryDomainConfig = true;
            }
            else
            {
                HasInvalidPrimaryDomainConfig = false;
            }

            // start a new cache of challenge config models when item changes
            challengeConfigViewModelCacheId = "none";
            _challengeConfigViewModels.Clear();

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
            RaisePropertyChangedEvent(nameof(IsExternalManagedCertificateItem));
            RaisePropertyChangedEvent(nameof(IsExternalSubscriptionMode));
            RaisePropertyChangedEvent(nameof(ShowStandardIdentifiersEditor));
            RaisePropertyChangedEvent(nameof(ShowAuthorityTokenEditor));
            RaisePropertyChangedEvent(nameof(ExternalSourceTypes));
            RaisePropertyChangedEvent(nameof(ExternalRetrievalModes));
            RaisePropertyChangedEvent(nameof(ExternalPollingIntervals));
            RaisePropertyChangedEvent(nameof(ExternalSourceCredentials));
            RaisePropertyChangedEvent(nameof(SelectedExternalCredential));

            RaisePropertyChangedEvent(nameof(IsEditable));

            RaisePropertyChangedEvent(nameof(ParsedTokenList));

            RaisePropertyChangedEvent(nameof(CertificateAuthorityTitle));
            RaisePropertyChangedEvent(nameof(CertificateAuthorityDescription));
            RaisePropertyChangedEvent(nameof(LastAttemptedCertificateAuthority));
            RaisePropertyChangedEvent(nameof(PercentageLifetimeElapsed));
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

        public string CertificateAuthorityTitle
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
                    return ca?.Title.AsNullWhenBlank() ?? "(Default)";
                }
                else
                {
                    return "None";
                }
            }
        }
        public string LastAttemptedCertificateAuthority
        {
            get
            {
                if (!string.IsNullOrEmpty(SelectedItem?.LastAttemptedCA))
                {
                    var ca = CertificateAuthorities.FirstOrDefault(c => c.Id == SelectedItem.LastAttemptedCA);
                    return ca?.Title.AsNullWhenBlank() ?? "(Not Attempted)";
                }
                else
                {
                    return CertificateAuthorityTitle;
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
            set
            {
                _appViewModel.SelectedItem = value;
            }
        }

        private ObservableCollection<ChallengeConfigItemViewModel> _challengeConfigViewModels = new ObservableCollection<ChallengeConfigItemViewModel>();
        private string challengeConfigViewModelCacheId = "none";
        public ObservableCollection<ChallengeConfigItemViewModel> ChallengeConfigViewModels
        {
            get
            {
                if (SelectedItem != null)
                {
                    // setup default challenge type
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

                    if (challengeConfigViewModelCacheId != SelectedItem.Id || _challengeConfigViewModels.Count != SelectedItem.RequestConfig.Challenges.Count())
                    {
                        challengeConfigViewModelCacheId = SelectedItem.Id;
                        _challengeConfigViewModels.Clear();

                        // setup view models for each existing challenge config
                        foreach (var conf in SelectedItem.RequestConfig.Challenges)
                        {
                            _challengeConfigViewModels.Add(new ChallengeConfigItemViewModel(conf));
                        }
                    }
                }
                else
                {
                    challengeConfigViewModelCacheId = "none";
                    _challengeConfigViewModels.Clear();
                }

                return _challengeConfigViewModels;
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

        private ObservableCollection<StoredCredential> _externalSourceCredentials = new ObservableCollection<StoredCredential>();
        private string? _externalSourceCredentialFilter = null;

        [DependsOn("_appViewModel.StoredCredentials")]
        public IEnumerable<Models.Config.StoredCredential> ExternalSourceCredentials
        {
            get
            {
                // When source type is AzureKeyVault, filter to matching Azure AD credential type
                var sourceType = SelectedItem?.ExternalSource?.SourceType;
                string? providerFilter = null;

                var list = _appViewModel.StoredCredentials?.ToList();

                if (_externalSourceCredentialFilter != providerFilter)
                {
                    // Source type changed — rebuild the collection with the new filter
                    _externalSourceCredentialFilter = providerFilter;
                    _externalSourceCredentials.Clear();
                }

                if (_externalSourceCredentials.Count == 0)
                {
                    _externalSourceCredentials.Add(new Models.Config.StoredCredential
                    {
                        StorageKey = null,
                        Title = "(None)"
                    });

                    if (list != null)
                    {
                        foreach (var a in list)
                        {
                            _externalSourceCredentials.Add(a);
                        }
                    }
                }
                else if (list != null)
                {
                    foreach (var p in list)
                    {
                        if (!_externalSourceCredentials.Any(c => c.StorageKey == p.StorageKey))
                        {
                            _externalSourceCredentials.Add(p);
                        }
                    }
                }

                return _externalSourceCredentials;
            }
        }

        public StoredCredential? SelectedExternalCredential
        {
            get
            {
                var key = SelectedItem?.ExternalSource?.CredentialKey;
                return ExternalSourceCredentials.FirstOrDefault(c => c.StorageKey == key);
            }
            set
            {
                if (SelectedItem?.ExternalSource != null)
                {
                    SelectedItem.ExternalSource.CredentialKey = value?.StorageKey;
                    SelectedItem.IsChanged = true;
                }
            }
        }

        public ObservableCollection<Models.Hub.ManagedCertificateSummary> SubscribableManagedCertificates { get; } = new();
        public bool IsLoadingSubscribableCertificates { get; private set; }
        public bool HasAttemptedLoadSubscribableCertificates { get; private set; }

        public bool ShowNoSubscribableManagedCertificatesIndicator
        {
            get
            {
                var isHubSource = string.Equals(
                    SelectedItem?.ExternalSource?.SourceType,
                    ExternalCertificateSourceTypes.ManagementHub,
                    StringComparison.OrdinalIgnoreCase);

                return isHubSource
                    && HasAttemptedLoadSubscribableCertificates
                    && !IsLoadingSubscribableCertificates
                    && SubscribableManagedCertificates.Count == 0;
            }
        }

        private Models.Hub.ManagedCertificateSummary? _selectedSubscribableCertificate;
        public Models.Hub.ManagedCertificateSummary? SelectedSubscribableCertificate
        {
            get => _selectedSubscribableCertificate;
            set
            {
                _selectedSubscribableCertificate = value;
                if (value != null && SelectedItem?.ExternalSource != null)
                {
                    SelectedItem.ExternalSource.ExternalReference = $"{value.InstanceId}/{value.Id}";
                    SelectedItem.ExternalSource.SourceItemName = value.Title;
                    SelectedItem.IsChanged = true;
                    RaisePropertyChangedEvent(nameof(SelectedItem));
                }
            }
        }

        public async Task LoadSubscribableManagedCertificates()
        {
            HasAttemptedLoadSubscribableCertificates = true;

            IsLoadingSubscribableCertificates = true;
            RaisePropertyChangedEvent(nameof(IsLoadingSubscribableCertificates));
            RaisePropertyChangedEvent(nameof(HasAttemptedLoadSubscribableCertificates));
            RaisePropertyChangedEvent(nameof(ShowNoSubscribableManagedCertificatesIndicator));

            SubscribableManagedCertificates.Clear();

            var results = await _appViewModel.GetHubSubscribableManagedCertificates();
            foreach (var item in results)
            {
                SubscribableManagedCertificates.Add(item);
            }

            SyncSelectedExternalSourceDisplayInfo();

            IsLoadingSubscribableCertificates = false;
            RaisePropertyChangedEvent(nameof(IsLoadingSubscribableCertificates));
            RaisePropertyChangedEvent(nameof(SubscribableManagedCertificates));
            RaisePropertyChangedEvent(nameof(ShowNoSubscribableManagedCertificatesIndicator));
        }

        private void SyncSelectedExternalSourceDisplayInfo()
        {
            if (SelectedItem?.ExternalSource == null)
            {
                _selectedSubscribableCertificate = null;
                return;
            }

            if (!string.Equals(SelectedItem.ExternalSource.SourceType, ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase))
            {
                _selectedSubscribableCertificate = null;
                return;
            }

            var externalReference = SelectedItem.ExternalSource.ExternalReference;
            if (string.IsNullOrWhiteSpace(externalReference))
            {
                _selectedSubscribableCertificate = null;
                return;
            }

            _selectedSubscribableCertificate = SubscribableManagedCertificates.FirstOrDefault(i =>
                string.Equals($"{i.InstanceId}/{i.Id}", externalReference, StringComparison.OrdinalIgnoreCase));

            if (_selectedSubscribableCertificate != null)
            {
                SelectedItem.ExternalSource.SourceItemName = _selectedSubscribableCertificate.Title;
                RaisePropertyChangedEvent(nameof(SelectedItem));
            }
        }

        public void ClearSubscribableManagedCertificates(bool resetLoadAttemptState = true)
        {
            SubscribableManagedCertificates.Clear();

            if (resetLoadAttemptState)
            {
                HasAttemptedLoadSubscribableCertificates = false;
                RaisePropertyChangedEvent(nameof(HasAttemptedLoadSubscribableCertificates));
            }

            RaisePropertyChangedEvent(nameof(SubscribableManagedCertificates));
            RaisePropertyChangedEvent(nameof(ShowNoSubscribableManagedCertificatesIndicator));
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
            if (SelectedItem != null)
            {
                var update = await _appViewModel.AddOrUpdateManagedCertificate(SelectedItem);

                if (update != null && SelectedItem != null)
                {
                    SelectedItem = update;
                }

                RaiseSelectedItemChanges();

                return update != null;
            }
            else
            {
                return false;
            }
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
                    return (int)(SelectedItem.DateExpiry - DateTimeOffset.UtcNow).Value.TotalDays;
                }

                return null;
            }
        }

        public DateTimeOffset? DateNextRenewalDue
        {
            get
            {
                return ManagedCertificate
                    .CalculateNextRenewalAttempt(SelectedItem, Preferences.RenewalIntervalDays, _appViewModel.Preferences?.RenewalIntervalMode)?.DateNextRenewalAttempt;
            }
        }

        public ObservableCollection<StatusMessage> ConfigCheckResults
        {
            get; set;
        }

        public string ValidationError { get; set; }

        public int? PercentageLifetimeElapsed
        {
            get
            {
                return SelectedItem?.GetPercentageLifetimeElapsed(DateTimeOffset.UtcNow);
            }
        }
        /// <summary>
        /// If true, the UI will show the TnAuth list view, otherwise the standard domain list view
        /// </summary>
        public bool UseAuthorityTokenListView { get; set; }
        public bool IsSelectedItemValid => SelectedItem?.Id != null && !SelectedItem.IsChanged;

        [DependsOn(nameof(SelectedItem))]
        public bool IsExternalManagedCertificateItem => SelectedItem?.ItemType == ManagedCertificateType.SSL_ExternallyManaged;

        [DependsOn(nameof(SelectedItem))]
        public bool IsExternalSubscriptionMode
        {
            get => IsExternalManagedCertificateItem;
            set
            {
                if (SelectedItem == null)
                {
                    return;
                }

                var targetType = value
                    ? ManagedCertificateType.SSL_ExternallyManaged
                    : ManagedCertificateType.SSL_ACME;

                if (SelectedItem.ItemType == targetType)
                {
                    return;
                }

                SelectedItem.ItemType = targetType;

                if (value)
                {
                    EnsureExternalSourceConfiguration();
                    UseAuthorityTokenListView = false;
                }

                SelectedItem.IsChanged = true;

                RaisePropertyChangedEvent(nameof(IsExternalManagedCertificateItem));
                RaisePropertyChangedEvent(nameof(IsExternalSubscriptionMode));
                RaisePropertyChangedEvent(nameof(ShowStandardIdentifiersEditor));
                RaisePropertyChangedEvent(nameof(ShowAuthorityTokenEditor));
            }
        }

        [DependsOn(nameof(SelectedItem), nameof(UseAuthorityTokenListView))]
        public bool ShowStandardIdentifiersEditor => !IsExternalManagedCertificateItem && !UseAuthorityTokenListView;

        [DependsOn(nameof(SelectedItem), nameof(UseAuthorityTokenListView))]
        public bool ShowAuthorityTokenEditor => !IsExternalManagedCertificateItem && UseAuthorityTokenListView;

        public IEnumerable<KeyValuePair<string, string>> ExternalSourceTypes => new[]
        {
            new KeyValuePair<string, string>(ExternalCertificateSourceTypes.ManagementHub, "Management Hub")
            //new KeyValuePair<string, string>(ExternalCertificateSourceTypes.SecretsStore, "Secrets Store")
        };

        public IEnumerable<string> ExternalRetrievalModes => new[]
        {
            ExternalCertificateRetrievalModes.Pull,
            ExternalCertificateRetrievalModes.Push,
            ExternalCertificateRetrievalModes.Auto
        };

        public IEnumerable<int> ExternalPollingIntervals => new[] { 5, 15, 30, 60, 120, 240, 720, 1440 };

        public Preferences Preferences => _appViewModel.Preferences;

        public static ManagedCertificateViewModel GetModel()
        {
            if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                return new ManagedCertificateViewModelDesign();
            }
            else
            {
                return new ManagedCertificateViewModel();
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

            if (SelectedItem == null)
            {
                return new ValidationResult(false, "No item selected", ValidationErrorCodes.ITEM_NOT_FOUND.ToString());
            }

            if (IsExternalManagedCertificateItem)
            {
                return ValidateExternalSource();
            }

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

        public async Task<List<StatusMessage>> TestChallengeResponse(ManagedCertificate managedCertificate) => await _appViewModel.TestChallengeConfiguration(managedCertificate);

        public async Task<StatusMessage> RevokeSelectedItem()
        {
            var managedCertificate = SelectedItem;
            return await _appViewModel.RevokeManageSiteCertificate(managedCertificate.Id);
        }

        public class AuthorityToken
        {
            public string Token { get; set; }
            public string Crl { get; set; }
            public string Title { get; set; }
        }

        private ObservableCollection<AuthorityToken> _parsedTokenList = new ObservableCollection<AuthorityToken>();
        public ObservableCollection<AuthorityToken> ParsedTokenList
        {
            get
            {
                _parsedTokenList.Clear();

                if (SelectedItem?.RequestConfig?.AuthorityTokens != null)
                {
                    foreach (var token in SelectedItem.RequestConfig.AuthorityTokens)
                    {
                        var parsedAtc = CertRequestConfig.GetParsedAtc(token.Token);

                        if (parsedAtc != null)
                        {
                            var authToken = new AuthorityToken
                            {
                                Token = token.Token,
                                Crl = token.Crl,
                                Title = $"{parsedAtc.TkValue} [{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Decode(parsedAtc.TkValue)}]"
                            };

                            _parsedTokenList.Add(authToken);
                        }
                    }
                }

                return _parsedTokenList;
            }
        }

        /// <summary>
        /// Used to temporarily hold PFX password when unlocking a PFX for view in the UI
        /// </summary>
        public string PfxUnlockPassword { get; set; }

        public ICommand SANSelectAllCommand => new RelayCommand<object>(SANSelectAll);
        public ICommand SANSelectNoneCommand => new RelayCommand<object>(SANSelectNone);

        public void EnsureExternalSourceConfiguration()
        {
            if (!IsExternalManagedCertificateItem || SelectedItem == null)
            {
                return;
            }

            SelectedItem.ExternalSource ??= new ExternalCertificateSubscription();

            if (string.IsNullOrWhiteSpace(SelectedItem.ExternalSource.RetrievalMode))
            {
                SelectedItem.ExternalSource.RetrievalMode = ExternalCertificateRetrievalModes.Auto;
            }

            if (string.IsNullOrWhiteSpace(SelectedItem.ExternalSource.SourceType))
            {
                SelectedItem.ExternalSource.SourceType = ExternalCertificateSourceTypes.ManagementHub;
            }

            if (SelectedItem.ExternalSource.PollIntervalMinutes <= 0)
            {
                SelectedItem.ExternalSource.PollIntervalMinutes = 30;
            }
        }

        private ValidationResult ValidateExternalSource()
        {
            EnsureExternalSourceConfiguration();

            var source = SelectedItem.ExternalSource;

            if (source == null)
            {
                return new ValidationResult(false, "External source settings are not available.", "EXTERNAL_SOURCE_MISSING");
            }

            if (string.IsNullOrWhiteSpace(source.SourceType))
            {
                return new ValidationResult(false, "Source Type is required when external subscription is configured.", "EXTERNAL_SOURCE_TYPE_REQUIRED");
            }

            if (string.IsNullOrWhiteSpace(source.ExternalReference))
            {
                return new ValidationResult(false, "Source Certificate is required when external subscription is configured.", "EXTERNAL_SOURCE_REFERENCE_REQUIRED");
            }

            if (source.PollIntervalMinutes <= 0)
            {
                source.PollIntervalMinutes = 30;
            }

            return new ValidationResult(true, string.Empty, string.Empty);
        }
    }
}
