using Certify.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace Certify.UI.Controls.ManagedItem
{
    /// <summary>
    /// Interaction logic for DomainAuthorization.xaml 
    /// </summary>
    public partial class DomainAuthorization : UserControl
    {
        protected Certify.UI.ViewModel.ManagedItemModel ItemViewModel => UI.ViewModel.ManagedItemModel.Current;
        protected Certify.UI.ViewModel.AppModel AppViewModel => UI.ViewModel.AppModel.Current;

        public DomainAuthorization()
        {
            InitializeComponent();

            ChallengeProviderList.ItemsSource = Models.Config.ChallengeProviders.Providers.Where(p => p.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS);
            StoredCredentialList.DataContext = AppViewModel;
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
                    ItemViewModel.SelectedItem.RequestConfig.ChallengeCredentialKey = cred.Item.StorageKey;
                }
            };
        }

        private void DirectoryBrowse_Click(object sender, EventArgs e)
        {
            var config = ItemViewModel.SelectedItem.RequestConfig;
            var dialog = new WinForms.FolderBrowserDialog()
            {
                SelectedPath = config.WebsiteRootPath
            };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                config.WebsiteRootPath = dialog.SelectedPath;
            }
        }
    }
}