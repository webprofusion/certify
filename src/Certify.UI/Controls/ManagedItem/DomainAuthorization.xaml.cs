using Certify.Models;
using System;
using System.Collections.ObjectModel;
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

            ChallengeAPIProviderList.ItemsSource = Models.Config.ChallengeProviders.Providers.Where(p => p.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS);
            this.AppViewModel.PropertyChanged += AppViewModel_PropertyChanged;
        }

        private void AppViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedItem")
            {
                RefreshCredentialOptions();
                ItemViewModel.RaisePropertyChanged(nameof(ItemViewModel.PrimaryChallengeConfig));
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
                    ItemViewModel.SelectedItem.RequestConfig.Challenges = new ObservableCollection<CertRequestChallengeConfig> {
                        new CertRequestChallengeConfig {
                                ChallengeCredentialKey= cred.Item.StorageKey,
                                ChallengeProvider=ChallengeAPIProviderList.SelectedValue.ToString(),
                                ChallengeType = ChallengeTypeList.SelectedValue.ToString()
                            }
                    };
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

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ItemViewModel.RefreshPrimaryChallengeConfig();

            ItemViewModel.PrimaryChallengeConfig.ChallengeType = (sender as ComboBox)?.SelectedValue.ToString();

            ItemViewModel.RaisePropertyChanged(nameof(ItemViewModel.PrimaryChallengeConfig));
        }

        private void RefreshCredentialOptions()
        {
            // filter list of matching credentials
            var credentials = AppViewModel.StoredCredentials.Where(s => s.ProviderType == ItemViewModel.PrimaryChallengeConfig.ChallengeProvider);
            StoredCredentialList.ItemsSource = credentials;

            //select first credential by default
            if (credentials.Count() > 0)
            {
                var selectedCredential = credentials.FirstOrDefault(c => c.StorageKey == ItemViewModel.PrimaryChallengeConfig.ChallengeCredentialKey);
                if (selectedCredential != null)
                {
                    // ItemViewModel.PrimaryChallengeConfig.ChallengeCredentialKey = credentials.First().StorageKey;
                }
                else
                {
                    ItemViewModel.PrimaryChallengeConfig.ChallengeCredentialKey = credentials.First().StorageKey;
                }
            }
        }

        private void ChallengeAPIProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string challengeProviderType = (sender as ComboBox)?.SelectedValue?.ToString();

            if (challengeProviderType != null)
            {
                ItemViewModel.PrimaryChallengeConfig.ChallengeProvider = challengeProviderType;

                RefreshCredentialOptions();
            }
            ItemViewModel.RaisePropertyChanged(nameof(ItemViewModel.PrimaryChallengeConfig));
        }
    }
}