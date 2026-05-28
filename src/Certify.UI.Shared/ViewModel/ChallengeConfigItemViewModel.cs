using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Shared.Validation;

namespace Certify.UI.ViewModel
{
    public class ChallengeConfigItemViewModel : BindableBase
    {
        private const string NoCredentialOptionTitle = "(None)";

        private string? _selectedDnsPersistAccountUri;
        private bool _dnsPersistPolicyWildcard;
        private DateTime? _dnsPersistUntilDate;

        /// <summary>
        /// Note: this view model has a complex binding relationship with the parent managed certificate view model. 
        /// Work is done in multiple places to ensure the IsChanged property is appropriately set and preserved.
        /// </summary>
        /// 
        private AppViewModel _appViewModel => AppViewModel.Current;

        public CertRequestChallengeConfig SelectedItem
        {
            get; set;
        }

        public ManagedCertificate ParentManagedCertificate => _appViewModel.SelectedItem;

        public ChallengeConfigItemViewModel(CertRequestChallengeConfig item)
        {
            SelectedItem = item;

            if (SelectedItem != null)
            {
                SelectedItem.AfterPropertyChanged -= SelectedItem_AfterPropertyChanged;
                SelectedItem.AfterPropertyChanged += SelectedItem_AfterPropertyChanged;
            }
        }

        private void SelectedItem_AfterPropertyChanged(object sender, EventArgs e)
        {
            if (e is System.ComponentModel.PropertyChangedEventArgs)
            {
                var args = e as System.ComponentModel.PropertyChangedEventArgs;
                if (args.PropertyName == nameof(SelectedItem.ChallengeType))
                {
                    if (SelectedItem.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                    {
                        if (SelectedItem.ChallengeProvider != null)
                        {
                            SelectedItem.ChallengeProvider = null;
                        }

                        if (SelectedItem.ChallengeCredentialKey != null)
                        {
                            SelectedItem.ChallengeCredentialKey = null;
                        }

                        if (SelectedItem.Parameters?.Count() > 0)
                        {
                            SelectedItem.Parameters = new ObservableCollection<ProviderParameter>();
                        }
                    }

                    if (SelectedItem.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST)
                    {
                        SelectedItem.ChallengeProvider = null;
                        SelectedItem.ChallengeCredentialKey = null;
                        SelectedItem.Parameters = new ObservableCollection<ProviderParameter>();
                        EnsureDefaultDnsPersistAccountSelection();
                    }

                    RaiseDnsPersistStateChanged();
                }

                if (args.PropertyName == nameof(SelectedItem.DomainMatch))
                {
                    RaiseDnsPersistStateChanged();
                }

                if (SelectedItem.IsChanged && !_appViewModel.SelectedItem.IsChanged)
                {
                    ParentManagedCertificate.ResetIsChanged(true);
                }
            }
        }

        public bool HasMultipleChallengeConfigurations
        {
            get
            {
                return ParentManagedCertificate.RequestConfig.Challenges.Count() > 1;
            }
        }

        /// <summary>
        /// ACME - supported challenge types 
        /// </summary>
        public IEnumerable<string> ChallengeTypes
        {
            get
            {
                if (ParentManagedCertificate.RequestConfig.AuthorityTokens?.Any() == true)
                {
                    return new string[] {
                        SupportedChallengeTypes.CHALLENGE_TYPE_TKAUTH
                    };
                }
                else
                {
                    return new string[] {
                        SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
                        SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                        SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST
                    };
                }
            }
        }

        public bool IsDnsPersistSelected => SelectedItem?.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST;

        public ObservableCollection<AccountDetails> DnsPersistAccounts => _appViewModel.AccountDetails;

        public bool HasDnsPersistAccounts => DnsPersistAccounts?.Any() == true;

        public string? SelectedDnsPersistAccountUri
        {
            get => _selectedDnsPersistAccountUri;
            set
            {
                if (_selectedDnsPersistAccountUri != value)
                {
                    _selectedDnsPersistAccountUri = value;
                    RaiseDnsPersistStateChanged();
                }
            }
        }

        public bool DnsPersistPolicyWildcard
        {
            get => _dnsPersistPolicyWildcard;
            set
            {
                if (_dnsPersistPolicyWildcard != value)
                {
                    _dnsPersistPolicyWildcard = value;
                    RaiseDnsPersistStateChanged();
                }
            }
        }

        public DateTime? DnsPersistUntilDate
        {
            get => _dnsPersistUntilDate;
            set
            {
                if (_dnsPersistUntilDate != value)
                {
                    _dnsPersistUntilDate = value;
                    RaiseDnsPersistStateChanged();
                }
            }
        }

        public string DnsPersistUntil => DnsPersistUntilDate.HasValue
            ? new DateTimeOffset(DnsPersistUntilDate.Value.Date, TimeSpan.Zero).ToUnixTimeSeconds().ToString()
            : string.Empty;

        public string DnsPersistRecordExamplesText => string.Join(Environment.NewLine, GetDnsPersistRecordExamples());

        public IReadOnlyList<string> GetDnsPersistRecordExamples()
        {
            return CertificateEditorService.GetDnsPersistExampleRecords(
                ParentManagedCertificate,
                SelectedDnsPersistAccountUri,
                includePolicyWildcard: DnsPersistPolicyWildcard,
                persistUntil: DnsPersistUntil,
                issuerDomainName: SelectedDnsPersistAccountUri?.Contains("letsencrypt.org") == true ? "letsencrypt.org" : "ca.example",
                domainMatchRule: SelectedItem?.DomainMatch);
        }

        public bool UsesCredentials { get; set; }
        public bool ShowZoneLookup { get; set; }
        public bool IsZoneLookupInProgress { get; set; }

        public ChallengeProviderDefinition SelectedChallengeProvider
        {
            get
            {
                if (SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.ChallengeProvider))
                {
                    return ChallengeProviders.FirstOrDefault(i => i.Id == SelectedItem.ChallengeProvider);
                }
                else { return null; }
            }
        }

        public ObservableCollection<ChallengeProviderDefinition> ChallengeProviders => new ObservableCollection<ChallengeProviderDefinition>(
                    _appViewModel.ChallengeAPIProviders
                    .Where(p => p.ProviderParameters.Any() && p.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                    .OrderBy(p => p.Title)
                    .ToList());

        public ObservableCollection<Models.Providers.DnsZone> DnsZones { get; set; } = new ObservableCollection<Models.Providers.DnsZone>();

        public ObservableCollection<StoredCredential> FilteredCredentials
        {
            get
            {
                if (SelectedItem != null && _appViewModel?.StoredCredentials != null)
                {
                    return new ObservableCollection<StoredCredential>(GetCredentialOptions(
                        _appViewModel.StoredCredentials.Where(s => s.ProviderType == SelectedItem.ChallengeProvider)
                    ));
                }
                else
                {
                    return new ObservableCollection<StoredCredential>(GetCredentialOptions([]));
                }
            }
        }

        private IEnumerable<StoredCredential> GetCredentialOptions(IEnumerable<StoredCredential> credentials)
        {
            if (IsCredentialOptional())
            {
                yield return new StoredCredential
                {
                    Title = NoCredentialOptionTitle,
                    StorageKey = null,
                    ProviderType = SelectedItem?.ChallengeProvider
                };
            }

            foreach (var credential in credentials)
            {
                yield return credential;
            }
        }

        private bool IsCredentialOptional()
        {
            return SelectedChallengeProvider?.IsCredentialOptional == true;
        }

        internal async Task RefreshAllOptions(ComboBox storedCredentialsList, bool preserveExistingParameterValues = true)
        {

            RefreshParameters(preserveExistingParameterValues);

            var currentIsChanged = ParentManagedCertificate.IsChanged;
            await RefreshCredentialOptions(storedCredentialsList);
            ParentManagedCertificate.ResetIsChanged(currentIsChanged);

            // if we need to migrate WebsiteRootPath, apply it here
            if (ParentManagedCertificate != null)
            {
                var config = ParentManagedCertificate.RequestConfig;

                if (config.WebsiteRootPath != null && SelectedItem.ChallengeRootPath == null && SelectedItem.ChallengeType == Models.SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                {
                    SelectedItem.ChallengeRootPath = config.WebsiteRootPath;
                    config.WebsiteRootPath = null;
                }
            }

            EnsureDefaultDnsPersistAccountSelection();

            RaisePropertyChangedEvent(nameof(SelectedChallengeProvider));
            RaisePropertyChangedEvent(nameof(ProviderParameters));
            RaiseDnsPersistStateChanged();

        }

        public ObservableCollection<ProviderParameter> ProviderParameters
        {
            get
            {
                return SelectedItem?.Parameters;
            }
        }

        public async Task RefreshCredentialOptions(ComboBox storedCredentialsList)
        {
            var currentIsChanged = _appViewModel.SelectedItem.IsChanged;
            PauseChangeEvents();

            var currentSelectedValue = SelectedItem.ChallengeCredentialKey;

            // filter list of matching credentials
            await _appViewModel.RefreshStoredCredentialsList();

            var credentials = _appViewModel.StoredCredentials.Where(s => s.ProviderType == SelectedItem.ChallengeProvider).ToList();

            // updating item source also clears selected value, so this workaround sets it back
            // this is only an issue when you have two or more credentials for one provider
            // this will in turn cause our model to be marked as changed even if it wasn't before (this is why we pause and resume change events in this method)         
            storedCredentialsList.ItemsSource = GetCredentialOptions(credentials).ToList();

            if (currentSelectedValue != null && SelectedItem.ChallengeCredentialKey != currentSelectedValue)
            {
                SelectedItem.ChallengeCredentialKey = currentSelectedValue;
            }

            if (!string.IsNullOrEmpty(SelectedItem.ChallengeCredentialKey))
            {
                var selectedCredential = credentials.FirstOrDefault(c => c.StorageKey == SelectedItem.ChallengeCredentialKey);
                if (selectedCredential == null)
                {
                    SelectedItem.ChallengeCredentialKey = null;
                }
            }

            if (string.IsNullOrEmpty(SelectedItem.ChallengeCredentialKey) && credentials.Count > 0 && !IsCredentialOptional())
            {
                SelectedItem.ChallengeCredentialKey = credentials.First().StorageKey;
            }

            if (string.IsNullOrEmpty(SelectedItem.ChallengeCredentialKey))
            {
                storedCredentialsList.SelectedIndex = IsCredentialOptional() ? 0 : -1;
            }
            else if (storedCredentialsList.SelectedValue?.ToString() != SelectedItem.ChallengeCredentialKey)
            {
                storedCredentialsList.SelectedValue = SelectedItem.ChallengeCredentialKey;
            }

            ResumeChangeEvents();
            ParentManagedCertificate.ResetIsChanged(currentIsChanged);
        }

        private void RefreshParameters(bool preserveExistingValues = true)
        {
            if (SelectedItem.Parameters == null)
            {
                SelectedItem.Parameters = new ObservableCollection<ProviderParameter>();
            }

            var existingValues = preserveExistingValues
                ? SelectedItem.Parameters.ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, string>();

            var legacyZoneId = preserveExistingValues ? SelectedItem.ZoneId : null;

            var definition = _appViewModel.ChallengeAPIProviders.FirstOrDefault(p => p.Id == SelectedItem.ChallengeProvider);

            // challenge provider has changed, by way of change to overall challenge type
            if (SelectedItem.ChallengeType != definition?.ChallengeType)
            {
                definition = null;
            }

            if (definition != null)
            {
                if (definition.ProviderParameters.Any(p => p.IsCredential))
                {
                    UsesCredentials = true;
                }
                else
                {
                    UsesCredentials = false;
                }

                // add or update provider parameters (if any) TODO: remove unused params
                var providerParams = definition.ProviderParameters
                    .Where(p => p.IsCredential == false)
                    .Select(p => p.Clone() as ProviderParameter)
                    .Where(p => p != null)
                    .Cast<ProviderParameter>()
                    .ToList();

                if (providerParams.Any(p => p.Key == "zoneid"))
                {
                    // move zone id to first param in list for benefit of UI layout
                    var z = providerParams.Find(p => p.Key == "zoneid");
                    if (z != null)
                    {
                        providerParams.Remove(z);
                        providerParams.Insert(0, z);
                    }
                }

                foreach (var pa in providerParams)
                {
                    if (existingValues.TryGetValue(pa.Key, out var existingValue))
                    {
                        pa.Value = existingValue;
                    }
                    else if (pa.Key == "zoneid" && !string.IsNullOrEmpty(legacyZoneId))
                    {
                        pa.Value = legacyZoneId;
                    }
                }

                SelectedItem.Parameters = new ObservableCollection<ProviderParameter>(providerParams);
                SelectedItem.ZoneId = null;
            }
            else
            {
                //if definition has changed to a type with no parameters, reset the parameters collection.
                if (SelectedItem.Parameters?.Any() == true)
                {
                    SelectedItem.Parameters = new ObservableCollection<ProviderParameter>();
                }
            }
        }

        public void EnsureDefaultDnsPersistAccountSelection()
        {
            if (!HasDnsPersistAccounts)
            {
                SelectedDnsPersistAccountUri = null;
                return;
            }

            if (!string.IsNullOrWhiteSpace(SelectedDnsPersistAccountUri)
                && DnsPersistAccounts.Any(a => a.AccountURI == SelectedDnsPersistAccountUri))
            {
                return;
            }

            var defaultCaId = ParentManagedCertificate?.CertificateAuthorityId;

            if (string.IsNullOrWhiteSpace(defaultCaId))
            {
                defaultCaId = _appViewModel.Preferences?.DefaultCertificateAuthority;
            }

            if (string.IsNullOrWhiteSpace(defaultCaId))
            {
                defaultCaId = StandardCertAuthorities.LETS_ENCRYPT;
            }

            var useStagingMode = ParentManagedCertificate?.UseStagingMode ?? false;

            var defaultAccount = DnsPersistAccounts.FirstOrDefault(a =>
                string.Equals(a.CertificateAuthorityId, defaultCaId, StringComparison.OrdinalIgnoreCase)
                && a.IsStagingAccount == useStagingMode);

            SelectedDnsPersistAccountUri = defaultAccount?.AccountURI ?? DnsPersistAccounts.FirstOrDefault()?.AccountURI;
        }

        public void RaiseDnsPersistStateChanged()
        {
            RaisePropertyChangedEvent(nameof(IsDnsPersistSelected));
            RaisePropertyChangedEvent(nameof(DnsPersistAccounts));
            RaisePropertyChangedEvent(nameof(HasDnsPersistAccounts));
            RaisePropertyChangedEvent(nameof(SelectedDnsPersistAccountUri));
            RaisePropertyChangedEvent(nameof(DnsPersistPolicyWildcard));
            RaisePropertyChangedEvent(nameof(DnsPersistUntilDate));
            RaisePropertyChangedEvent(nameof(DnsPersistUntil));
            RaisePropertyChangedEvent(nameof(DnsPersistRecordExamplesText));
        }
    }
}
