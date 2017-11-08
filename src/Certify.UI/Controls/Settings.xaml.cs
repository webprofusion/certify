using Certify.Management;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for Settings.xaml 
    /// </summary>
    public partial class Settings : UserControl
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return ViewModel.AppModel.AppViewModel;
            }
        }

        private bool settingsInitialised = false;
        private Models.Preferences _prefs = null;

        public Settings()
        {
            InitializeComponent();
        }

        private async Task LoadCurrentSettings()
        {
            //TODO: we could now bind to Preferences
            _prefs = await MainViewModel.CertifyClient.GetPreferences();

            //MainViewModel.LoadVaultTree();
            this.CheckForUpdatesCheckbox.IsChecked = _prefs.CheckForUpdatesAtStartup;
            this.EnableTelematicsCheckbox.IsChecked = _prefs.EnableAppTelematics;
            this.EnableProxyAPICheckbox.IsChecked = _prefs.EnableValidationProxyAPI;

            //if true, EFS will be used for sensitive files such as private key file, does not work in all versions of windows.
            this.EnableEFS.IsChecked = _prefs.EnableEFS;
            this.IgnoreStoppedSites.IsChecked = _prefs.IgnoreStoppedSites;

            this.EnableDNSValidationChecks.IsChecked = _prefs.EnableDNSValidationChecks;

            this.RenewalIntervalDays.Value = _prefs.RenewalIntervalDays;
            this.RenewalMaxRequests.Value = _prefs.MaxRenewalRequests;

            this.DataContext = MainViewModel;

            settingsInitialised = true;
            Save.IsEnabled = false;
        }

        private void Button_NewContact(object sender, RoutedEventArgs e)
        {
            //present new contact dialog
            var d = new Windows.EditContactDialog
            {
                Owner = Window.GetWindow(this)
            };
            d.ShowDialog();

            //refresh primary contact
            MainViewModel.LoadVaultTree();
        }

        private void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            if (settingsInitialised)
            {
                ///capture settings
                _prefs.CheckForUpdatesAtStartup = (this.CheckForUpdatesCheckbox.IsChecked == true);
                _prefs.EnableAppTelematics = (this.EnableTelematicsCheckbox.IsChecked == true);
                _prefs.EnableValidationProxyAPI = (this.EnableProxyAPICheckbox.IsChecked == true);
                _prefs.EnableDNSValidationChecks = (this.EnableDNSValidationChecks.IsChecked == true);

                _prefs.EnableEFS = (this.EnableEFS.IsChecked == true);
                _prefs.IgnoreStoppedSites = (this.IgnoreStoppedSites.IsChecked == true);

                // force renewal interval days to be between 1 and 60 days
                if (this.RenewalIntervalDays.Value == null) this.RenewalIntervalDays.Value = 14;
                if (this.RenewalIntervalDays.Value > 60) this.RenewalIntervalDays.Value = 60;
                _prefs.RenewalIntervalDays = (int)this.RenewalIntervalDays.Value;

                // force max renewal requests to be between 0 and 100 ( 0 = unlimited)
                if (this.RenewalMaxRequests.Value == null) this.RenewalMaxRequests.Value = 0;
                if (this.RenewalMaxRequests.Value > 100) this.RenewalMaxRequests.Value = 100;
                _prefs.MaxRenewalRequests = (int)this.RenewalMaxRequests.Value;
                Save.IsEnabled = true;
            }
        }

        private void RenewalIntervalDays_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            this.SettingsUpdated(sender, e);
        }

        private void RenewalMaxRequests_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            this.SettingsUpdated(sender, e);
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // reload settings
            await LoadCurrentSettings();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel.CertifyClient.SetPreferences(_prefs);
            Save.IsEnabled = false;
        }
    }
}