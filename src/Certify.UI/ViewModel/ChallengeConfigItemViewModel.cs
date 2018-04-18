using Certify.Models;
using Certify.Models.Config;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Certify.UI.ViewModel
{
    public class ChallengeConfigItemViewModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>
        //public static ChallengeConfigItemViewModel Current = ChallengeConfigItemViewModel.GetModel();

        private Certify.UI.ViewModel.AppViewModel _appViewModel => ViewModel.AppViewModel.Current;

        public ChallengeConfigItemViewModel(CertRequestChallengeConfig item)
        {
            SelectedItem = item;
        }

        /// <summary>
        /// Let's Encrypt - supported challenge types 
        /// </summary>
        public IEnumerable<string> ChallengeTypes { get; set; } = new string[] {
            SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
            SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            //SupportedChallengeTypes.CHALLENGE_TYPE_SNI
        };

        public ObservableCollection<ProviderDefinition> ChallengeProviders
        {
            get
            {
                return new ObservableCollection<ProviderDefinition>(
                    Certify.Models.Config.ChallengeProviders.Providers
                    .Where(p => p.ProviderParameters.Any())
                    .OrderBy(p => p.Title)
                    .ToList());
            }
        }

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

        public CertRequestChallengeConfig SelectedItem
        {
            /* get
             {
                 var managedCertificate = _appViewModel.SelectedItem;

                 if (managedCertificate.RequestConfig.Challenges == null) managedCertificate.RequestConfig.Challenges = new ObservableCollection<CertRequestChallengeConfig> { };

                 if (managedCertificate.RequestConfig.Challenges.Any())
                 {
                     return managedCertificate.RequestConfig.Challenges[0];
                 }
                 else
                 {
                     // no challenge config defined, create a default, migrate settings
                     managedCertificate.RequestConfig.Challenges.Add(new CertRequestChallengeConfig
                     {
                         ChallengeType = managedCertificate.RequestConfig.ChallengeType
                     });
                     managedCertificate.RequestConfig.ChallengeType = null;

                     return managedCertificate.RequestConfig.Challenges[0];
                 }
             }*/
            get; set;
        }

        public ManagedCertificate ParentManagedCertificate
        {
            get
            {
                return _appViewModel.SelectedItem;
            }
        }
    }
}