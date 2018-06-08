using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Certify.Models.Config;
using Certify.UI.ViewModel;
using WinForms = System.Windows.Forms;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Handles UI interaction for defining Challenge Configuration 
    /// </summary>
    public partial class ChallengeConfigItem : System.Windows.Controls.UserControl
    {
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

        private void RefreshParameters()
        {
            EditModel.SelectedItem.Parameters = new ObservableCollection<ProviderParameter>();
            var definition = AppViewModel.ChallengeAPIProviders.FirstOrDefault(p => p.Id == EditModel.SelectedItem.ChallengeProvider);

            if (definition != null)
            {
                if (definition.ProviderParameters.Any(p=>p.IsCredential))
                {
                    EditModel.UsesCredentials = true;
                    EditModel.RaisePropertyChangedEvent(nameof(EditModel.UsesCredentials));
                } else
                {
                    EditModel.UsesCredentials = false;
                    EditModel.RaisePropertyChangedEvent(nameof(EditModel.UsesCredentials));
                }

                foreach (var pa in definition.ProviderParameters.Where(p => p.IsCredential == false))
                {
                    // if zoneid previously stored, migrate to provider param
                    if (pa.Key == "zoneid" && !String.IsNullOrEmpty(EditModel.SelectedItem.ZoneId))
                    {
                        pa.Value = EditModel.SelectedItem.ZoneId;
                    }

                    EditModel.SelectedItem.Parameters.Add(pa);
                }
            }
        }

        private async void ChallengeAPIProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string challengeProviderType = (sender as ComboBox)?.SelectedValue?.ToString();

            if (challengeProviderType != null)
            {
                EditModel.SelectedItem.ChallengeProvider = challengeProviderType;

                RefreshParameters();

                await RefreshCredentialOptions();

               
            }
        }

        private void ParameterInput_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            EditModel.SelectedItem.IsChanged = true;
        }

        private void DeleteAuth_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to delete this configuration?", "Confirm Delete", MessageBoxButton.YesNoCancel)== MessageBoxResult.Yes)
            {
                // delete 
                if (sender is Button)
                {
                    var config = (sender as Button).Tag;
                    if (AppViewModel.SelectedItem.RequestConfig.Challenges.Count>1)
                    {
                        AppViewModel.SelectedItem.RequestConfig.Challenges.Remove((Models.CertRequestChallengeConfig)config);
                    } else
                    {
                        MessageBox.Show("At least one authorization configuration is required.");
                    }
                }
            }
        }
    }
}
