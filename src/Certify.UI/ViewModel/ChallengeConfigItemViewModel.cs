using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.UI.ViewModel
{
    public class ChallengeConfigItemViewModel : BindableBase
    {

        private AppViewModel _appViewModel => AppViewModel.Current;

        public CertRequestChallengeConfig SelectedItem
        {
            get; set;
        }

        public ManagedCertificate ParentManagedCertificate => _appViewModel.SelectedItem;

        public ChallengeConfigItemViewModel(CertRequestChallengeConfig item)
        {
            SelectedItem = item;
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
        public IEnumerable<string> ChallengeTypes { get; set; } = new string[] {
            SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
            SupportedChallengeTypes.CHALLENGE_TYPE_DNS
        };

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
                if (SelectedItem != null)
                {
                    return new ObservableCollection<StoredCredential>(
                  _appViewModel.StoredCredentials.Where(s => s.ProviderType == SelectedItem.ChallengeProvider)
                  );
                }
                else
                {
                    return new ObservableCollection<StoredCredential>();
                }
            }
        }

        internal async Task RefreshAllOptions(ComboBox storedCredentialsList)
        {
            RefreshParameters();

            await RefreshCredentialOptions(storedCredentialsList);

            // if we need to migrate WebsiteRootPath, apply it here
            var config = ParentManagedCertificate.RequestConfig;

            if (config.WebsiteRootPath != null && SelectedItem.ChallengeRootPath == null && SelectedItem.ChallengeType == Models.SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
            {
                SelectedItem.ChallengeRootPath = config.WebsiteRootPath;
                config.WebsiteRootPath = null;
            }

            RaisePropertyChangedEvent(nameof(SelectedChallengeProvider));
        }

        public async Task RefreshCredentialOptions(ComboBox storedCredentialsList)
        {
            PauseChangeEvents();
            SelectedItem.PauseChangeEvents();

            // filter list of matching credentials
            await _appViewModel.RefreshStoredCredentialsList();

            var credentials = _appViewModel.StoredCredentials.Where(s => s.ProviderType == SelectedItem.ChallengeProvider);
            var currentSelectedValue = SelectedItem.ChallengeCredentialKey;

            // updating item source also clears selected value, so this workaround sets it back
            // this is only an issue when you have two or more credentials for one provider
            // this will in turn cause our model to be marked as changed even if it wasn't before (this is why we pause and resume change events in this method)         
            storedCredentialsList.ItemsSource = credentials;

            if (currentSelectedValue != null)
            {
                SelectedItem.ChallengeCredentialKey = currentSelectedValue;
            }

            //select first credential by default
            if (credentials.Count() > 0)
            {
                var selectedCredential = credentials.FirstOrDefault(c => c.StorageKey == SelectedItem.ChallengeCredentialKey);
                if (selectedCredential == null)
                {
                    SelectedItem.ChallengeCredentialKey = credentials.First().StorageKey;
                }
            }

            storedCredentialsList.SelectedValue = SelectedItem.ChallengeCredentialKey;

            ResumeChangeEvents();
            SelectedItem.ResumeChangeEvents();

        }

        private void RefreshParameters()
        {
            if (SelectedItem.Parameters == null)
            {
                SelectedItem.Parameters = new ObservableCollection<ProviderParameter>();
            }

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
                var providerParams = definition.ProviderParameters.Where(p => p.IsCredential == false);
                foreach (var pa in providerParams)
                {
                    // if zoneid previously stored, migrate to provider param
                    if (pa.Key == "zoneid")
                    {
                        if (!string.IsNullOrEmpty(SelectedItem.ZoneId))
                        {
                            pa.Value = SelectedItem.ZoneId;
                            SelectedItem.ZoneId = null;
                        }
                    }

                    if (!SelectedItem.Parameters.Any(p => p.Key == pa.Key))
                    {
                        SelectedItem.Parameters.Add(pa);
                    }
                }

                var toRemove = new List<ProviderParameter>();

                toRemove.AddRange(SelectedItem.Parameters.Where(p => !providerParams.Any(pp => pp.Key == p.Key)));
                foreach (var r in toRemove)
                {
                    SelectedItem.Parameters.Remove(r);
                }
            }
            else
            {
                SelectedItem.Parameters = new ObservableCollection<ProviderParameter>();
            }
        }
    }
}
