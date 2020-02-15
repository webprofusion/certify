using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Certify.Models;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class SettingsGeneral : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        private bool settingsInitialised = false;
        private Models.Preferences _prefs = null;
        private Models.Config.StoredCredential _selectedStoredCredential = null;

        private List<AccountItem> Accounts;

        internal class AccountItem
        {
            public string ID { get; set; }
            public string Title { get; set; }

            public string CertificateAuthorityId { get; set; }
            public bool IsStagingAccount { get; set; } = false;

            public string Email { get; set; }
            public string AccountURI { get; set; }
            public string AccountKey { get; set; }
        }

        public SettingsGeneral()
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

            this.Accounts = new List<AccountItem>();
            foreach (var ca in CertificateAuthority.CertificateAuthorities)
            {
                this.Accounts.Add(new AccountItem { ID = ca.Id + "_production", CertificateAuthorityId = ca.Id, Title = $"{ca.Title} ({ca.WebsiteUrl}, Default)", IsStagingAccount = false, Email = MainViewModel.PrimaryContactEmail });
                this.Accounts.Add(new AccountItem { ID = ca.Id + "_staging", CertificateAuthorityId = ca.Id, Title = $"{ca.Title} ({ca.WebsiteUrl}, Staging)", IsStagingAccount = true, Email = MainViewModel.PrimaryContactEmail });
            }
            this.AccountList.ItemsSource = Accounts;



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
            EnableAutomaticCAFailover.IsChecked = _prefs.EnabledAutomaticCAFailover;

            RenewalIntervalDays.Value = _prefs.RenewalIntervalDays;
            RenewalMaxRequests.Value = _prefs.MaxRenewalRequests;

            DataContext = MainViewModel;

            this.CertificateAuthorityList.ItemsSource = CertificateAuthority.CertificateAuthorities.Where(c => c.IsEnabled == true);

            CertificateAuthorityList.SelectedValue = _prefs.DefaultCertificateAuthority;

            settingsInitialised = true;


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
                _prefs.EnabledAutomaticCAFailover = (EnableAutomaticCAFailover.IsChecked == true);

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


                _prefs.DefaultCertificateAuthority = CertificateAuthorityList.SelectedValue.ToString();

                // save settings
                await MainViewModel.CertifyClient.SetPreferences(_prefs);

            }
        }

        private void RenewalIntervalDays_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e) => SettingsUpdated(sender, e);

        private void RenewalMaxRequests_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e) => SettingsUpdated(sender, e);

        private async void UserControl_Loaded(object sender, RoutedEventArgs e) =>
            // reload settings
            await LoadCurrentSettings();


        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {

            var uiTheme = ((Certify.UI.App)App.Current).ToggleTheme();

            var uiSettings = UI.Settings.UISettings.Load();

            if (uiSettings == null)
            {
                uiSettings = new UI.Settings.UISettings();
            }

            uiSettings.UITheme = uiTheme;
            UI.Settings.UISettings.Save(uiSettings);

        }

        private void CertificateAuthorityList_SelectionChanged(object sender, SelectionChangedEventArgs e) => SettingsUpdated(sender, e);


    }
}
