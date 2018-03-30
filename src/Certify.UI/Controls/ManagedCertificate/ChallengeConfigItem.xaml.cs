using Certify.UI.ViewModel;
using System;
using System.Linq;
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

        private void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            var cred = new Windows.EditCredential
            {
                Owner = Window.GetWindow(this)
            };
            cred.ShowDialog();

            //refresh credentials list on complete
            cred.Closed += async (object s, System.EventArgs ev) =>
            {
                var credential = cred.Item;
                await AppViewModel.RefreshStoredCredentialsList();

                if (cred.Item != null)
                {
                    // create a new challenge config based on new credentialsSelectedItem
                    EditModel.SelectedItem.ChallengeCredentialKey = cred.Item.StorageKey;
                }
            };
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

        private void RefreshCredentialOptions()
        {
            // filter list of matching credentials
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

        private void ChallengeAPIProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string challengeProviderType = (sender as ComboBox)?.SelectedValue?.ToString();

            if (challengeProviderType != null)
            {
                EditModel.SelectedItem.ChallengeProvider = challengeProviderType;

                RefreshCredentialOptions();
            }
            // EditModel.RaisePropertyChanged(nameof(ItemViewModel.PrimaryChallengeConfig));
        }
    }
}