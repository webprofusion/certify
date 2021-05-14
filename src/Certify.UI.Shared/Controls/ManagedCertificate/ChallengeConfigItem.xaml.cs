using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Certify.Models;
using Certify.Models.Config;
using Certify.UI.ViewModel;
using WinForms = System.Windows.Forms;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Handles UI interaction for defining Challenge Configuration 
    /// </summary>
    public partial class ChallengeConfigItem : UserControl
    {
        protected AppViewModel AppViewModel => AppViewModel.Current;
        protected ManagedCertificateViewModel ManagedCertificateViewModel => ManagedCertificateViewModel.Current;

        public ChallengeConfigItem()
        {
            InitializeComponent();

            DataContextChanged -= ChallengeConfigItem_DataContextChanged;
            DataContextChanged += ChallengeConfigItem_DataContextChanged;
        }

        private async void ChallengeConfigItem_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => await EditModel.RefreshAllOptions(StoredCredentialList);

        private ChallengeConfigItemViewModel EditModel => (ChallengeConfigItemViewModel)DataContext;

        private async void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            var cred = new Windows.EditCredential
            {
                Owner = Window.GetWindow(this)
            };
            cred.Item.ProviderType = EditModel.SelectedItem.ChallengeProvider;

            cred.ShowDialog();

            //refresh credentials list on complete

            await EditModel.RefreshCredentialOptions(StoredCredentialList);

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
            // Website root path (if required) is shared across all challenge configs

            var config = EditModel.ParentManagedCertificate.RequestConfig;

            if (config.WebsiteRootPath != null && EditModel.SelectedItem.ChallengeRootPath == null)
            {
                EditModel.SelectedItem.ChallengeRootPath = config.WebsiteRootPath;
            }

            var dialog = new WinForms.FolderBrowserDialog()
            {
                SelectedPath = EditModel.SelectedItem.ChallengeRootPath
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                EditModel.SelectedItem.ChallengeRootPath = dialog.SelectedPath;

                // remove deprecated config setting
                config.WebsiteRootPath = null;
            }
        }

        private async void ChallengeAPIProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var challengeProviderType = (sender as ComboBox)?.SelectedValue?.ToString();

            if (challengeProviderType != null)
            {
                await SetChallengeProvider(challengeProviderType);
            }
        }

        private async Task SetChallengeProvider(string challengeProviderType)
        {
            var previousSelection = EditModel.SelectedItem.ChallengeProvider;
            if (previousSelection != null && previousSelection != challengeProviderType)
            {
                EditModel.SelectedItem.Parameters.Clear();
            }

            EditModel.SelectedItem.ChallengeProvider = challengeProviderType;

            EditModel.SelectedItem.ChallengeCredentialKey = null;

            await EditModel.RefreshAllOptions(StoredCredentialList);

            if (challengeProviderType == "DNS01.Manual")
            {
                MessageBox.Show(
                    "You have selected the Manual DNS method. This option is provided for testing and is often the most difficult to get working. Do not rely on Manual DNS for normal certificate renewal as the manual process must be repeated for every renewal. Consider other automated options such as acme-dns: https://docs.certifytheweb.com/docs/dns-validation",
                    "Warning", MessageBoxButton.OK, MessageBoxImage.Exclamation
                    );
            }
        }

        private void ParameterInput_KeyUp(object sender, System.Windows.Input.KeyEventArgs e) => EditModel.SelectedItem.IsChanged = true;

        private void DeleteAuth_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to delete this configuration?", "Confirm Delete", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
            {
                // delete 
                if (sender is Button)
                {
                    var config = (sender as Button).Tag;
                    if (config != null && AppViewModel.SelectedItem.RequestConfig.Challenges.Count > 1)
                    {
                        Application.Current.Dispatcher.Invoke(delegate
                        {
                            AppViewModel.SelectedItem.RequestConfig.Challenges.Remove((Models.CertRequestChallengeConfig)config);
                            ManagedCertificateViewModel.RaisePropertyChangedEvent(nameof(ManagedCertificateViewModel.ChallengeConfigViewModels));
                        });
                    }
                    else
                    {
                        MessageBox.Show("At least one authorization configuration is required.");
                    }
                }
            }
        }

        private async Task RefreshDnsZoneLookup()
        {
            EditModel.IsZoneLookupInProgress = true;
            try
            {
                EditModel.DnsZones = new ObservableCollection<Models.Providers.DnsZone>(new List<Models.Providers.DnsZone> {
                    new Models.Providers.DnsZone {
                        ZoneId="",
                        Name ="(Fetching..)"
                    }
                });

                // fetch dns zone list from api 
                var zones = await AppViewModel.GetDnsProviderZones(EditModel.SelectedItem.ChallengeProvider, EditModel.SelectedItem.ChallengeCredentialKey);

                // populate dropdown, default to no selection
                zones.Insert(0, new Models.Providers.DnsZone { ZoneId = "", Name = "(Select Zone)" });
                EditModel.DnsZones = new ObservableCollection<Models.Providers.DnsZone>(zones);
                DnsZoneList.SelectedValue = "";
            }
            catch (Exception)
            {
                MessageBox.Show("Dns Zone Lookup could not be completed. Check credentials are correctly set.");
            }
            finally
            {
                EditModel.IsZoneLookupInProgress = false;
            }
        }

        private void DnsZoneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DnsZoneList.SelectedValue != null && DnsZoneList.SelectedValue.ToString() != "")
            {
                var param = EditModel.SelectedItem.Parameters.Where(pa => pa.Key == "zoneid").FirstOrDefault();
                if (param != null)
                {
                    param.Value = DnsZoneList.SelectedValue.ToString();
                    EditModel.SelectedItem.Parameters = new ObservableCollection<ProviderParameter>(EditModel.SelectedItem.Parameters);
                    EditModel.ShowZoneLookup = false;
                }
            }
        }

        private async void DnsZoneList_DropDownOpened(object sender, EventArgs e)
        {
            if (EditModel.DnsZones == null || !EditModel.DnsZones.Any())
            {
                await RefreshDnsZoneLookup();
            }
        }

        private async void ShowParamLookup_Click(object sender, RoutedEventArgs e)
        {
            EditModel.ShowZoneLookup = true;
            await RefreshDnsZoneLookup();
        }

        private void HelpUrl_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            e.Handled = true;

            Utils.Helpers.LaunchBrowser(e.Uri.AbsoluteUri);
        }

        private async void ChallengeTypeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            if ((string)ChallengeTypeList.SelectedValue == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
            {
                if (string.IsNullOrEmpty(EditModel.SelectedItem.ChallengeProvider))
                {
                    // default dns challenges to acme-dns
                    await SetChallengeProvider("DNS01.API.AcmeDns");

                }
            }

        }
    }
}
