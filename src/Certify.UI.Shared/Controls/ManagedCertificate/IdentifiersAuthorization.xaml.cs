using System.Linq;
using System.Windows.Controls;
using Certify.Models;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for IdentifierAuthorization.xaml 
    /// </summary>
    public partial class IdentifierAuthorization : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        public IdentifierAuthorization()
        {
            InitializeComponent();
        }

        private void AddAuth_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ItemViewModel.SelectedItem.RequestConfig.Challenges != null && ItemViewModel.SelectedItem.RequestConfig.Challenges.Any())
            {
                var lastItem = ItemViewModel.SelectedItem.RequestConfig.Challenges.Last();

                // begin a new auth item in the collection, copy last settings
                ItemViewModel.SelectedItem.RequestConfig.Challenges.Add(new CertRequestChallengeConfig
                {
                    ChallengeType = lastItem.ChallengeType,
                    ChallengeProvider = lastItem.ChallengeProvider,
                    ChallengeCredentialKey = lastItem.ChallengeCredentialKey
                });
            }
            else
            {
                // begin a new auth item in the collection
                ItemViewModel.SelectedItem.RequestConfig.Challenges.Add(new CertRequestChallengeConfig
                {
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                });
            }

            ItemViewModel.RaisePropertyChangedEvent(nameof(ItemViewModel.ChallengeConfigViewModels));
        }
    }
}
