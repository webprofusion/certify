using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Certify.Locales;
using Certify.Models;
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

        public enum ValidationErrorCodes
        {
            REQUIRED_PRIMARY_IDENTIFIER,
            CHALLENGE_TYPE_INVALID,
            REQUIRED_NAME,
            INVALID_HOSTNAME,
            REQUIRED_CHALLENGE_CONFIG_PARAM,
            SAN_LIMIT,
            NONE
        }

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
                    return ca?.Description ?? "(CA Unknown)";
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

            var list = await _appViewModel.GetServerSiteList(StandardServerTypes.IIS);

            list.Insert(0, new SiteInfo { Name = "(No IIS Site Selected)", Id = "" });

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

        [DependsOn("_appViewModel.CertificateAuthorities")]
        public IEnumerable<CertificateAuthority> CertificateAuthorities
        {
            get
            {
                var list = _appViewModel.CertificateAuthorities.Where(c => c.IsEnabled == true).ToList();
                list.Insert(0, new CertificateAuthority
                {
                    Id = null,
                    Title = "Auto",
                    Description = "The Certificate Authority will be automatically selected based on compatibility and the configured ACME accounts."
                });
                return list;
            }
        }

        [DependsOn("_appViewModel.StoredCredentials")]
        public IEnumerable<Models.Config.StoredCredential> StoredPasswords
        {
            get
            {
                var list = _appViewModel.StoredCredentials?.Where(c => c.ProviderType == StandardAuthTypes.STANDARD_AUTH_PASSWORD).ToList();

                if (list == null)
                {
                    list = new List<Models.Config.StoredCredential>();
                }

                list.Insert(0, new Models.Config.StoredCredential
                {
                    StorageKey = null,
                    Title = "(No Password)",
                    ProviderType = StandardAuthTypes.STANDARD_AUTH_PASSWORD
                });
                return list;
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

        public DomainOption PrimarySubjectDomain
        {
            get => SelectedItem?.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain);
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

        public bool IsAdvancedView { get; set; } = false;

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
        /// Check the currently selected options and auto set where we can, transpose selected identifier options to update the final request configuration
        /// </summary>
        /// <returns></returns>
        public void ApplyAutoConfiguration()
        {
            var item = SelectedItem;
            var config = item.RequestConfig;

            // if no primary identifier is selected then we need to attempt to default to one
            if (!item.DomainOptions.Any(d => d.IsPrimaryDomain) && item.DomainOptions.Any(d => d.IsSelected))
            {
                var o = item.DomainOptions.FirstOrDefault(d => d.IsSelected == true);
                if (o != null)
                {
                    o.IsPrimaryDomain = true;
                }
            }

            // requests with a primary domain need to set the primary domain in the request config
            var primaryDomain = item.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true && d.Type == "dns");

            if (primaryDomain != null)
            {
                // update request config primary identifier
                if (config.PrimaryDomain != primaryDomain.Domain)
                {
                    config.PrimaryDomain = primaryDomain.Domain.Trim();
                }
            }

            //apply remaining selected domains as subject alternative names
            var sanList =
                item.DomainOptions.Where(dm => dm.IsSelected && dm.Type == "dns")
                .Select(i => i.Domain)
                .ToArray();

            if (config.SubjectAlternativeNames == null ||
                !sanList.SequenceEqual(config.SubjectAlternativeNames))
            {
                config.SubjectAlternativeNames = sanList;
            }

            // update our list of selected subject ip addresses, if any
            if (!config.SubjectIPAddresses.SequenceEqual(item.DomainOptions.Where(i => i.IsSelected && i.Type == "ip").Select(s => s.Domain).ToArray()))
            {
                config.SubjectIPAddresses = item.DomainOptions.Where(i => i.IsSelected && i.Type == "ip").Select(s => s.Domain).ToArray();
            }

            //determine if this site has an existing entry in Managed Certificates, if so use that, otherwise start a new one
            if (SelectedItem.Id == null)
            {
                item.Id = Guid.NewGuid().ToString();

                item.ItemType = ManagedCertificateType.SSL_ACME;

                // optionally append webserver site ID (if used)
                if (SelectedWebSite != null && !string.IsNullOrEmpty(SelectedWebSite.Id))
                {
                    item.Id += ":" + SelectedWebSite.Id;
                    item.GroupId = SelectedWebSite.Id;
                }
            }

            if (SelectedItem.RequestConfig.Challenges == null)
            {
                SelectedItem.RequestConfig.Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>();
            }

            if (SelectedItem.RequestConfig.PerformAutomatedCertBinding)
            {
                SelectedItem.RequestConfig.BindingIPAddress = null;
                SelectedItem.RequestConfig.BindingPort = null;
                SelectedItem.RequestConfig.BindingUseSNI = null;
            }
            else
            {
                //always select Use SNI unless it's specifically set to false
                if (SelectedItem.RequestConfig.BindingUseSNI == null)
                {
                    SelectedItem.RequestConfig.BindingUseSNI = true;
                }
            }

        }

        public ValidationResult Validate(bool applyAutoConfiguration)
        {
            try
            {
                if (applyAutoConfiguration)
                {
                    ApplyAutoConfiguration();
                }

                if (string.IsNullOrEmpty(SelectedItem.Name))
                {
                    IsNameEditMode = true;
                    return new ValidationResult(false, SR.ManagedCertificateSettings_NameRequired, ValidationErrorCodes.REQUIRED_NAME.ToString());
                }
                else
                {
                    IsNameEditMode = false;
                }

                // a primary subject domain must be set
                if (PrimarySubjectDomain == null)
                {
                    // if we still can't decide on the primary domain ask user to define it
                    return new ValidationResult(
                        false,
                        SR.ManagedCertificateSettings_NeedPrimaryDomain,
                        ValidationErrorCodes.REQUIRED_PRIMARY_IDENTIFIER.ToString()
                    );
                }

                var caId = Preferences.DefaultCertificateAuthority ?? StandardCertAuthorities.LETS_ENCRYPT;
                if (SelectedItem.CertificateAuthorityId != null)
                {
                    caId = SelectedItem.CertificateAuthorityId;
                }

                var ca = AppViewModel.Current.CertificateAuthorities.FirstOrDefault(c => c.Id == caId);

                if (!(ca != null && ca.AllowInternalHostnames))
                {
                    // validate hostnames
                    if (SelectedItem.DomainOptions.Any(d => d.IsSelected && d.Type == "dns" && (!d.Domain.Contains(".") || d.Domain.ToLower().EndsWith(".local"))))
                    {
                        // one or more selected domains does not include a label seperator (is an internal host name) or end in .local

                        return new ValidationResult(
                            false,
                            "One or more domains specified are internal hostnames. Certificates for internal host names are not supported by the Certificate Authority.",
                            ValidationErrorCodes.INVALID_HOSTNAME.ToString()
                        );
                    }
                }

                // if title still set to the default, automatically use the primary domain instead
                if (SelectedItem.Name == SR.ManagedCertificateSettings_DefaultTitle)
                {
                    SelectedItem.Name = PrimarySubjectDomain.Domain;
                }

                // certificates cannot request wildcards unless they also use DNS validation
                if (
                    SelectedItem.DomainOptions.Any(d => d.IsSelected && d.Domain.StartsWith("*."))
                    &&
                    !SelectedItem.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                    )
                {
                    return new ValidationResult(
                        false,
                        "Wildcard domains cannot use http-01 validation for domain authorization. Use dns-01 instead.",
                        ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString()
                    );
                }

                // TLS-SNI-01 (is now not supported)
                if (SelectedItem.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI))
                {
                    return new ValidationResult(
                        false,
                        "The tls-sni-01 challenge type is no longer available. You need to switch to either http-01 or dns-01.",
                        ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString()
                    );
                }

                if (SelectedItem.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS && string.IsNullOrEmpty(c.ChallengeProvider)))
                {
                    return new ValidationResult(
                        false,
                        "The dns-01 challenge type requires a DNS Update Method selection.",
                        ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString()
                    );
                }

                if (SelectedItem.RequestConfig.Challenges.Count(c => string.IsNullOrEmpty(c.DomainMatch)) > 1)
                {
                    return new ValidationResult(
                       false,
                       "Only one authorization configuration can be used which matches any domain (domain match blank). Specify domain(s) to match or remove additional configuration. ",
                       ValidationErrorCodes.CHALLENGE_TYPE_INVALID.ToString()
                    );

                    // TODO: error if any selected domains will not be matched
                }

                // validate settings for authorization config non-optional parameters
                foreach (var c in SelectedItem.RequestConfig.Challenges)
                {
                    if (c.Parameters != null && c.Parameters.Any())
                    {
                        //validate parameters
                        foreach (var p in c.Parameters)
                        {
                            if (p.IsRequired && string.IsNullOrEmpty(p.Value))
                            {
                                return new ValidationResult(
                                   false,
                                   $"Challenge configuration parameter required: {p.Name}",
                                   ValidationErrorCodes.REQUIRED_CHALLENGE_CONFIG_PARAM.ToString()
                                );
                            }
                        }
                    }
                }

                // check certificate will not exceed 100 name limit. TODO: make this dynamic per selected CA
                var numSelectedDomains = SelectedItem.DomainOptions.Count(d => d.IsSelected);

                if (numSelectedDomains > 100)
                {
                    return new ValidationResult(
                                 false,
                                 $"Certificates cannot include more than 100 names. You will need to remove names or split your certificate into 2 or more managed certificates.",
                                 ValidationErrorCodes.SAN_LIMIT.ToString()
                              );
                }

                // no problems found
                return new ValidationResult(true, "OK", ValidationErrorCodes.NONE.ToString());
            }
            catch (Exception exp)
            {

                // unexpected error while checking
                return new ValidationResult(false, exp.ToString());
            }
        }

        public async Task PopulateManagedCertificateSettings(string siteId)
        {
            ValidationError = null;
            var domainOptions = await GetDomainOptionsFromSite(siteId);
            
            var result = CertificateDomainsService.PopulateFromSiteInfo(SelectedItem, SelectedWebSite, domainOptions);

            if (!result.IsSuccess)
            {
                ValidationError = result.Message;
            } else
            {
                SelectedItem = result.Result;
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
                var invalidDomains = "";
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
                                if (item.DomainOptions.Count == 0)
                                {
                                    option.IsPrimaryDomain = true;
                                }

                                item.DomainOptions.Add(option);

                                if (option.Domain.StartsWith("*."))
                                {
                                    wildcardAdded = true;
                                }
                            }
                            else if (Uri.CheckHostName(domain) == UriHostNameType.IPv4 || Uri.CheckHostName(domain) == UriHostNameType.IPv6)
                            {
                                option.Type = "ip";
                                // add an IP address instead of a domain
                                if (item.DomainOptions.Count == 0)
                                {
                                    option.IsPrimaryDomain = true;
                                }

                                item.DomainOptions.Add(option);
                            }
                            else
                            {
                                invalidDomains += domain + "\n";
                            }
                        }
                    }
                }

                RaiseSelectedItemChanges();

                /*
                if (!string.IsNullOrEmpty(invalidDomains))
                {
                    MessageBox.Show("Invalid domains: " + invalidDomains);
                    return false;
                }*/

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
                        if (MessageBox.Show(msg, "Add non-wildcard equivalent domains?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
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

        protected virtual async Task<IEnumerable<DomainOption>> GetDomainOptionsFromSite(string siteId)
        {
            if (string.IsNullOrEmpty(siteId))
            {
                return new List<DomainOption>();
            }

            var list = await _appViewModel.GetServerSiteDomains(StandardServerTypes.IIS, siteId);

            // discard no-specific host wildcards for cert domain options
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
