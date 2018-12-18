using System;
using System.Collections.Generic;
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
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ManagedCertificateViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;

        public ChallengeConfigItem()
        {
            InitializeComponent();

            DataContextChanged += ChallengeConfigItem_DataContextChanged;
        }

        private async void ChallengeConfigItem_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            await RefreshAllOptions();
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

        private void ChallengeTypeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private async Task RefreshCredentialOptions()
        {
            // filter list of matching credentials
            await AppViewModel.RefreshStoredCredentialsList();
            var credentials = AppViewModel.StoredCredentials.Where(s => s.ProviderType == EditModel.SelectedItem.ChallengeProvider);
            var currentSelectedValue = EditModel.SelectedItem.ChallengeCredentialKey;

            // updating item source also clears selected value, so this workaround sets it back
            // this is only an issue when you have two or more credentials for one provider
            StoredCredentialList.ItemsSource = credentials;

            if (currentSelectedValue != null)
            {
                EditModel.SelectedItem.ChallengeCredentialKey = currentSelectedValue;
            }

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
            if (EditModel.SelectedItem.Parameters == null) EditModel.SelectedItem.Parameters = new ObservableCollection<ProviderParameter>();

            var definition = AppViewModel.ChallengeAPIProviders.FirstOrDefault(p => p.Id == EditModel.SelectedItem.ChallengeProvider);

            if (definition != null)
            {
                if (definition.ProviderParameters.Any(p => p.IsCredential))
                {
                    EditModel.UsesCredentials = true;
                }
                else
                {
                    EditModel.UsesCredentials = false;
                }

                // add or update provider parameters (if any) TODO: remove unused params
                var providerParams = definition.ProviderParameters.Where(p => p.IsCredential == false);
                foreach (var pa in providerParams)
                {
                    // if zoneid previously stored, migrate to provider param
                    if (pa.Key == "zoneid")
                    {
                        if (!String.IsNullOrEmpty(EditModel.SelectedItem.ZoneId))
                        {
                            pa.Value = EditModel.SelectedItem.ZoneId;
                            EditModel.SelectedItem.ZoneId = null;
                        }
                    }

                    if (!EditModel.SelectedItem.Parameters.Any(p => p.Key == pa.Key))
                    {
                        EditModel.SelectedItem.Parameters.Add(pa);
                    }
                }

                var toRemove = new List<ProviderParameter>();

                toRemove.AddRange(EditModel.SelectedItem.Parameters.Where(p => !providerParams.Any(pp => pp.Key == p.Key)));
                foreach (var r in toRemove)
                {
                    EditModel.SelectedItem.Parameters.Remove(r);
                }
            }
        }

        private async Task RefreshAllOptions()
        {
            RefreshParameters();
            await RefreshCredentialOptions();

            // if we need to migrate WebsiteRootPath, apply it here
            var config = EditModel.ParentManagedCertificate.RequestConfig;

            if (config.WebsiteRootPath != null && EditModel.SelectedItem.ChallengeRootPath == null && EditModel.SelectedItem.ChallengeType == Models.SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
            {
                EditModel.SelectedItem.ChallengeRootPath = config.WebsiteRootPath;
                config.WebsiteRootPath = null;
            }
        }

        private async void ChallengeAPIProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string challengeProviderType = (sender as ComboBox)?.SelectedValue?.ToString();

            if (challengeProviderType != null)
            {
                EditModel.SelectedItem.ChallengeProvider = challengeProviderType;

                await RefreshAllOptions();
            }
        }

        private void ParameterInput_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            EditModel.SelectedItem.IsChanged = true;
        }

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
                        App.Current.Dispatcher.Invoke((Action)delegate
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
                this.EditModel.DnsZones = new ObservableCollection<Models.Providers.DnsZone>(new System.Collections.Generic.List<Models.Providers.DnsZone> {
                    new Models.Providers.DnsZone {
                        ZoneId="",
                        Name ="(Fetching..)"
                    }
                });

                // fetch dns zone list from api 
                var zones = await AppViewModel.CertifyClient.GetDnsProviderZones(EditModel.SelectedItem.ChallengeProvider, EditModel.SelectedItem.ChallengeCredentialKey);

                // populate dropdown, default to no selection
                zones.Insert(0, new Models.Providers.DnsZone { ZoneId = "", Name = "(Select Zone)" });
                this.EditModel.DnsZones = new ObservableCollection<Models.Providers.DnsZone>(zones);
                this.DnsZoneList.SelectedValue = "";
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

        private async void PerformDnsZoneLookup_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDnsZoneLookup();
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
    }
}
