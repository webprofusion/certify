using Certify.UI.ViewModel;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for ChallengeConfigItem.xaml 
    /// </summary>
    public partial class ChallengeConfigItem : System.Windows.Controls.UserControl
    {
        //ChallengeAPIProviderList.ItemsSource = Models.Config.ChallengeProviders.Providers.Where(p => p.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS);

        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        public ChallengeConfigItem()
        {
            InitializeComponent();
        }

        private ChallengeConfigItemViewModel EditModel
        {
            get
            {
                return (ChallengeConfigItemViewModel)DataContext;
            }
        }

        private async void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            var cred = new Windows.EditCredential
            {
                Owner = Window.GetWindow(this)
            };
            cred.Item.ProviderType = EditModel.SelectedItem.ChallengeProvider;

            cred.ShowDialog();

            //refresh credentials list on complete

            await RefreshCredentialOptions();

            var credential = cred.Item;

            if (cred.Item != null && cred.Item.StorageKey != null)
            {
                // create a new challenge config based on new credentialsSelectedItem
                EditModel.SelectedItem.ChallengeProvider = credential.ProviderType;
                EditModel.SelectedItem.ChallengeCredentialKey = credential.StorageKey;
            }
        }

        private void DirectoryBrowse_Click(object sender, EventArgs e)
        {
            // Website root path (fi required) is shared across all challenge configs

            var config = EditModel.ParentManagedCertificate.RequestConfig;

            var dialog = new WinForms.FolderBrowserDialog()
            {
                SelectedPath = config.WebsiteRootPath
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                config.WebsiteRootPath = dialog.SelectedPath;
            }
        }

        private void ChallengeTypeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            /* ItemViewModel.RefreshPrimaryChallengeConfig();

             ItemViewModel.PrimaryChallengeConfig.ChallengeType = (sender as System.Windows.Controls.ComboBox)?.SelectedValue.ToString();

             ItemViewModel.RaisePropertyChanged(nameof(ItemViewModel.PrimaryChallengeConfig));
         */
        }

        private async Task RefreshCredentialOptions()
        {
            // filter list of matching credentials
            await AppViewModel.RefreshStoredCredentialsList();
            var credentials = AppViewModel.StoredCredentials.Where(s => s.ProviderType == EditModel.SelectedItem.ChallengeProvider);
            StoredCredentialList.ItemsSource = credentials;

            //select first credential by default
            if (credentials.Count() > 0)
            {
                var selectedCredential = credentials.FirstOrDefault(c => c.StorageKey == EditModel.SelectedItem.ChallengeCredentialKey);
                if (selectedCredential != null)
                {
                    // ItemViewModel.PrimaryChallengeConfig.ChallengeCredentialKey = credentials.First().StorageKey;
                }
                else
                {
                    EditModel.SelectedItem.ChallengeCredentialKey = credentials.First().StorageKey;
                }
            }
        }

        private async void ChallengeAPIProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string challengeProviderType = (sender as ComboBox)?.SelectedValue?.ToString();

            if (challengeProviderType != null)
            {
                EditModel.SelectedItem.ChallengeProvider = challengeProviderType;

                await RefreshCredentialOptions();
            }

            //EditModel.RaisePropertyChangedEvent(nameof(EditModel.PrimaryChallengeConfig));
        }
    }
}