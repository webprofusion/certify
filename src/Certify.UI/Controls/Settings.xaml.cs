using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        private bool settingsInitialised = false;
        private Models.Preferences _prefs = null;
        private Models.Config.StoredCredential _selectedStoredCredential = null;

        public Settings()
        {
            InitializeComponent();
        }

        private async Task LoadCurrentSettings()
        {
            if (!MainViewModel.IsServiceAvailable)
            {
                return;
            }

            //TODO: we could now bind to Preferences
            _prefs = await MainViewModel.CertifyClient.GetPreferences();

            if (_prefs.UseBackgroundServiceAutoRenewal)
            {
                // if scheduled task not in use, remove legacy option to modify
                ConfigureAutoRenew.Visibility = Visibility.Collapsed;
            }

            MainViewModel.PrimaryContactEmail = await MainViewModel.CertifyClient.GetPrimaryContact();

            EnableTelematicsCheckbox.IsChecked = _prefs.EnableAppTelematics;
            EnableProxyAPICheckbox.IsChecked = _prefs.EnableValidationProxyAPI;

            //if true, EFS will be used for sensitive files such as private key file, does not work in all versions of windows.
            EnableEFS.IsChecked = _prefs.EnableEFS;
            IgnoreStoppedSites.IsChecked = _prefs.IgnoreStoppedSites;

            EnableDNSValidationChecks.IsChecked = _prefs.EnableDNSValidationChecks;
            EnableHttpChallengeServer.IsChecked = _prefs.EnableHttpChallengeServer;

            if (_prefs.CertificateCleanupMode == Models.CertificateCleanupMode.None)
            {
                CertCleanup_None.IsChecked = true;
            }
            else if (_prefs.CertificateCleanupMode == Models.CertificateCleanupMode.AfterExpiry)
            {
                CertCleanup_AfterExpiry.IsChecked = true;
            }
            else if (_prefs.CertificateCleanupMode == Models.CertificateCleanupMode.AfterRenewal)
            {
                CertCleanup_AfterRenewal.IsChecked = true;
            }
            else if (_prefs.CertificateCleanupMode == Models.CertificateCleanupMode.FullCleanup)
            {
                CertCleanup_FullCleanup.IsChecked = true;
            }

            EnableStatusReporting.IsChecked = _prefs.EnableStatusReporting;

            RenewalIntervalDays.Value = _prefs.RenewalIntervalDays;
            RenewalMaxRequests.Value = _prefs.MaxRenewalRequests;

            DataContext = MainViewModel;

            settingsInitialised = true;

            //load stored credentials list
            await MainViewModel.RefreshStoredCredentialsList();
            CredentialsList.ItemsSource = MainViewModel.StoredCredentials;
        }

        private void Button_NewContact(object sender, RoutedEventArgs e)
        {
            //present new contact dialog
            var d = new Windows.EditContactDialog
            {
                Owner = Window.GetWindow(this)
            };
            d.ShowDialog();
        }

        private void Button_ScheduledTaskConfig(object sender, RoutedEventArgs e)
        {
            //show UI to update auto renewal task
            var d = new Windows.ScheduledTaskConfig { Owner = App.Current.MainWindow };

            d.ShowDialog();
        }

        private async void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            if (settingsInitialised)
            {
                ///capture settings
                _prefs.EnableAppTelematics = (EnableTelematicsCheckbox.IsChecked == true);
                _prefs.EnableValidationProxyAPI = (EnableProxyAPICheckbox.IsChecked == true);
                _prefs.EnableDNSValidationChecks = (EnableDNSValidationChecks.IsChecked == true);
                _prefs.EnableHttpChallengeServer = (EnableHttpChallengeServer.IsChecked == true);

                _prefs.EnableStatusReporting = (EnableStatusReporting.IsChecked == true);

                _prefs.EnableEFS = (EnableEFS.IsChecked == true);
                _prefs.IgnoreStoppedSites = (IgnoreStoppedSites.IsChecked == true);

                // force renewal interval days to be between 1 and 60 days
                if (RenewalIntervalDays.Value == null)
                {
                    RenewalIntervalDays.Value = 30;
                }

                if (RenewalIntervalDays.Value > 60)
                {
                    RenewalIntervalDays.Value = 60;
                }

                _prefs.RenewalIntervalDays = (int)RenewalIntervalDays.Value;

                // force max renewal requests to be between 0 and 100 ( 0 = unlimited)
                if (RenewalMaxRequests.Value == null)
                {
                    RenewalMaxRequests.Value = 0;
                }

                if (RenewalMaxRequests.Value > 100)
                {
                    RenewalMaxRequests.Value = 100;
                }

                _prefs.MaxRenewalRequests = (int)RenewalMaxRequests.Value;

                // cert cleanup mode
                if (CertCleanup_None.IsChecked == true)
                {
                    _prefs.CertificateCleanupMode = Models.CertificateCleanupMode.None;
                    _prefs.EnableCertificateCleanup = false;
                }
                else if (CertCleanup_AfterExpiry.IsChecked == true)
                {
                    _prefs.CertificateCleanupMode = Models.CertificateCleanupMode.AfterExpiry;
                    _prefs.EnableCertificateCleanup = true;
                }
                else if (CertCleanup_AfterRenewal.IsChecked == true)
                {
                    _prefs.CertificateCleanupMode = Models.CertificateCleanupMode.AfterRenewal;
                    _prefs.EnableCertificateCleanup = true;
                }
                else if (CertCleanup_FullCleanup.IsChecked == true)
                {
                    _prefs.CertificateCleanupMode = Models.CertificateCleanupMode.FullCleanup;
                    _prefs.EnableCertificateCleanup = true;
                }


                // save settings
                await MainViewModel.CertifyClient.SetPreferences(_prefs);

            }
        }

        private void RenewalIntervalDays_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e) => SettingsUpdated(sender, e);

        private void RenewalMaxRequests_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e) => SettingsUpdated(sender, e);

        private async void UserControl_Loaded(object sender, RoutedEventArgs e) =>
            // reload settings
            await LoadCurrentSettings();

        private void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            var cred = new Windows.EditCredential
            {
                Owner = Window.GetWindow(this)
            };

            cred.ShowDialog();

            UpdateDisplayedCredentialsList();
        }

        private void ModifyStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            //modify the selected credential
            if (_selectedStoredCredential != null)
            {
                var d = new Windows.EditCredential(_selectedStoredCredential)
                {
                    Owner = Window.GetWindow(this)
                };

                d.ShowDialog();

                UpdateDisplayedCredentialsList();
            }
        }

        private void UpdateDisplayedCredentialsList() => App.Current.Dispatcher.Invoke((Action)delegate
                                                       {
                                                           CredentialsList.ItemsSource = MainViewModel.StoredCredentials;
                                                       });

        private async void DeleteStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            //delete the selected credential, if not currently in use
            if (_selectedStoredCredential != null)
            {
                if (MessageBox.Show("Are you sure you wish to delete this stored credential?", "Confirm Delete", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    //confirm item not used then delete
                    var deleted = await MainViewModel.DeleteCredential(_selectedStoredCredential?.StorageKey);
                    if (!deleted)
                    {
                        MessageBox.Show("This stored credential could not be removed. It may still be in use by a managed site.");
                    }
                }

                UpdateDisplayedCredentialsList();
            }
        }

        private async void TestStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStoredCredential != null)

            {
                Mouse.OverrideCursor = Cursors.Wait;

                var result = await MainViewModel.TestCredentials(_selectedStoredCredential.StorageKey);

                Mouse.OverrideCursor = Cursors.Arrow;

                MessageBox.Show(result.Message);
            }
        }

        private void CredentialsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                _selectedStoredCredential = (Models.Config.StoredCredential)e.AddedItems[0];
            }
            else
            {
                _selectedStoredCredential = null;
            }
        }
    }
}
