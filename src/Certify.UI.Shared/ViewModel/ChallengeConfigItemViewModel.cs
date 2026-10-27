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
        private string? _selectedDnsPersistIssuer;
        private bool _dnsPersistPolicyWildcard;
        private bool _dnsPersistUseDomainOptOut;
        private long? _dnsPersistUntil;
        private List<DnsPersistDirectoryInfo>? _dnsPersistDirectories;
        private bool _isDnsPersistDirectoryLoading;

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

        /// <summary>
        /// The item this challenge config belongs to. Not the current selection: a view model for a copy of the item which has
        /// since been replaced (saved, refreshed) must not change the item now being edited.
        /// </summary>
        public ManagedCertificate ParentManagedCertificate { get; }

        public ChallengeConfigItemViewModel(ManagedCertificate parentManagedCertificate, CertRequestChallengeConfig item)
        {
            ParentManagedCertificate = parentManagedCertificate;
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
                        _ = InitDnsPersistGuidance();
                    }

                    RaiseDnsPersistStateChanged();
                }

                if (args.PropertyName == nameof(SelectedItem.DomainMatch))
                {
                    RaiseDnsPersistStateChanged();
                }

                if (SelectedItem.IsChanged && !ParentManagedCertificate.IsChanged)
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
                        SupportedChallengeTypes.CHALLENGE_TYPE_DNS_ACCOUNT,
                        SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST
                    };
                }
            }
        }

        public bool IsDnsPersistSelected => SelectedItem?.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST;

        /// <summary>
        /// One account per CA (production and staging) with an account: the one a request for that CA uses, which the
        /// dns-persist-01 records are built for
        /// </summary>
        public List<AccountDetails> DnsPersistAccounts => AccountDetails.GetRequestAccounts(_appViewModel.AccountDetails, _appViewModel.CertificateAuthorities.Select(c => c.Id));

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

        /// <summary>
        /// The date picked for persistUntil. The record stops being used from 00:00 UTC on this date.
        /// </summary>
        public DateTime? DnsPersistUntilDate
        {
            get => DnsPersistRecordBuilder.GetPersistUntilDate(_dnsPersistUntil);
            set
            {
                var persistUntil = DnsPersistRecordBuilder.GetPersistUntil(value);
                if (_dnsPersistUntil != persistUntil)
                {
                    _dnsPersistUntil = persistUntil;
                    RaiseDnsPersistStateChanged();
                }
            }
        }

        public bool IsDnsPersistUntilPassed => _dnsPersistUntil.HasValue && _dnsPersistUntil.Value <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public string? SelectedDnsPersistIssuer
        {
            get => _selectedDnsPersistIssuer;
            set
            {
                if (_selectedDnsPersistIssuer != value)
                {
                    _selectedDnsPersistIssuer = value;
                    RaiseDnsPersistStateChanged();
                }
            }
        }

        public bool DnsPersistUseDomainOptOut
        {
            get => _dnsPersistUseDomainOptOut;
            set
            {
                if (_dnsPersistUseDomainOptOut != value)
                {
                    _dnsPersistUseDomainOptOut = value;
                    RaiseDnsPersistStateChanged();
                }
            }
        }

        private DnsPersistDirectoryInfo? SelectedDnsPersistDirectory => _dnsPersistDirectories?.FirstOrDefault(d => d.AccountUri == SelectedDnsPersistAccountUri);

        public bool IsDnsPersistHashed => SelectedDnsPersistDirectory?.GetVariant() == DnsPersistVariant.HashedAccountUri;

        public List<string> DnsPersistIssuerOptions => SelectedDnsPersistDirectory?.GetKnownIssuerDomainNames() ?? new List<string>();

        public bool HasMultipleDnsPersistIssuers => DnsPersistIssuerOptions.Count > 1;

        public bool HasNoDnsPersistIssuers => !_isDnsPersistDirectoryLoading && !DnsPersistIssuerOptions.Any();

        public string DnsPersistIssuerPlaceholderWarning => $"This CA does not publish its issuer domain names. Replace {DnsPersistRecordBuilder.IssuerDomainNamePlaceholder} with the name given in the CA's documentation.";

        private List<string> DnsPersistIdentifiers => DnsPersistRecordBuilder.GetGuidanceIdentifiers(ParentManagedCertificate, SelectedItem?.DomainMatch);

        public bool DnsPersistHasWildcardIdentifiers => DnsPersistIdentifiers.Any(DnsPersistRecordBuilder.IsWildcard);

        public string DnsPersistFormatDescription => DnsPersistRecordBuilder.GetGuidanceDescription(SelectedDnsPersistDirectory, _isDnsPersistDirectoryLoading);

        /// <summary>
        /// One record per _validation-persist name, for the selected account and the CA's published values
        /// </summary>
        public List<DnsPersistRecord> DnsPersistRecords => DnsPersistRecordBuilder.BuildGuidanceRecords(
            DnsPersistIdentifiers,
            SelectedDnsPersistDirectory,
            SelectedDnsPersistAccountUri,
            SelectedDnsPersistIssuer,
            DnsPersistPolicyWildcard,
            _dnsPersistUntil,
            DnsPersistUseDomainOptOut);

        public string DnsPersistZoneFileText => string.Join(Environment.NewLine, DnsPersistRecords.Select(r => r.ZoneFileEntry));

        private bool _isDnsPersistGuidanceInitialised;

        /// <summary>
        /// Once dns-persist-01 is selected, tick policy=wildcard if any identifier is a wildcard and read what each
        /// account's CA publishes about dns-persist-01
        /// </summary>
        private async Task InitDnsPersistGuidance()
        {
            if (!_isDnsPersistGuidanceInitialised)
            {
                _isDnsPersistGuidanceInitialised = true;
                DnsPersistPolicyWildcard = DnsPersistHasWildcardIdentifiers;
            }

            await LoadDnsPersistDirectories();
        }

        /// <summary>
        /// Read what each account's CA publishes. If that fails the directories stay unknown, and are read again the next
        /// time dns-persist-01 guidance is shown.
        /// </summary>
        private async Task LoadDnsPersistDirectories()
        {
            if (_dnsPersistDirectories != null || _isDnsPersistDirectoryLoading)
            {
                return;
            }

            _isDnsPersistDirectoryLoading = true;
            RaiseDnsPersistStateChanged();

            try
            {
                _dnsPersistDirectories = await _appViewModel.GetDnsPersistDirectoryInfo();
            }
            finally
            {
                _isDnsPersistDirectoryLoading = false;
                RaiseDnsPersistStateChanged();
            }
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
            // re-syncing parameters with the provider definition is not an edit in itself. The flag is restored before awaiting
            // anything, so edits the user makes meanwhile are kept
            var currentIsChanged = ParentManagedCertificate.IsChanged;

            RefreshParameters(preserveExistingParameterValues);

            ParentManagedCertificate.ResetIsChanged(currentIsChanged);

            // refreshing credential options preserves the changed flag itself
            await RefreshCredentialOptions(storedCredentialsList);

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

            if (IsDnsPersistSelected)
            {
                await InitDnsPersistGuidance();
            }

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
            // fetch the latest credentials first. The changed flag is only saved and restored around the updates below, which
            // run without awaiting anything, so edits the user makes while the list is fetched are kept
            await _appViewModel.RefreshStoredCredentialsList();

            var currentIsChanged = ParentManagedCertificate.IsChanged;
            PauseChangeEvents();

            var currentSelectedValue = SelectedItem.ChallengeCredentialKey;

            // filter list of matching credentials
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
            if (SupportedChallengeTypes.GetProviderChallengeType(SelectedItem.ChallengeType) != definition?.ChallengeType)
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
            RaisePropertyChangedEvent(nameof(IsDnsPersistUntilPassed));
            RaisePropertyChangedEvent(nameof(SelectedDnsPersistIssuer));
            RaisePropertyChangedEvent(nameof(DnsPersistUseDomainOptOut));
            RaisePropertyChangedEvent(nameof(IsDnsPersistHashed));
            RaisePropertyChangedEvent(nameof(DnsPersistIssuerOptions));
            RaisePropertyChangedEvent(nameof(HasMultipleDnsPersistIssuers));
            RaisePropertyChangedEvent(nameof(HasNoDnsPersistIssuers));
            RaisePropertyChangedEvent(nameof(DnsPersistHasWildcardIdentifiers));
            RaisePropertyChangedEvent(nameof(DnsPersistFormatDescription));
            RaisePropertyChangedEvent(nameof(DnsPersistRecords));
            RaisePropertyChangedEvent(nameof(DnsPersistZoneFileText));
        }
    }
}
