using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.UI.ViewModel
{
    public class ChallengeConfigItemViewModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>
        //public static ChallengeConfigItemViewModel Current = ChallengeConfigItemViewModel.GetModel();

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

        /// <summary>
        /// Let's Encrypt - supported challenge types 
        /// </summary>
        public IEnumerable<string> ChallengeTypes { get; set; } = new string[] {
            SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
            SupportedChallengeTypes.CHALLENGE_TYPE_DNS
        };

        public bool UsesCredentials { get; set; }
        public bool ShowZoneLookup { get; set; }
        public bool IsZoneLookupInProgress { get; set; }

        public ObservableCollection<ChallengeProviderDefinition> ChallengeProviders => new ObservableCollection<ChallengeProviderDefinition>(
                    _appViewModel.ChallengeAPIProviders
                    .Where(p => p.ProviderParameters.Any())
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
    }
}
