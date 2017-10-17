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
        private bool hasChanges = false;

        public Settings()
        {
            InitializeComponent();
        }

        private void LoadCurrentSettings()
        {
            MainViewModel.LoadVaultTree();
            SettingsManager.LoadAppSettings();

            //MainViewModel.LoadVaultTree();
            this.CheckForUpdatesCheckbox.IsChecked = CoreAppSettings.Current.CheckForUpdatesAtStartup;
            this.EnableTelematicsCheckbox.IsChecked = CoreAppSettings.Current.EnableAppTelematics;
            this.EnableProxyAPICheckbox.IsChecked = CoreAppSettings.Current.EnableValidationProxyAPI;

            //if true, EFS will be used for sensitive files such as private key file, does not work in all versions of windows.
            this.EnableEFS.IsChecked = CoreAppSettings.Current.EnableEFS;
            this.IgnoreStoppedSites.IsChecked = CoreAppSettings.Current.IgnoreStoppedSites;

            this.EnableDNSValidationChecks.IsChecked = CoreAppSettings.Current.EnableDNSValidationChecks;

            this.RenewalIntervalDays.Value = CoreAppSettings.Current.RenewalIntervalDays;
            this.RenewalMaxRequests.Value = CoreAppSettings.Current.MaxRenewalRequests;

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

            //refresh
            MainViewModel.LoadVaultTree();
        }

        private void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            if (settingsInitialised)
            {
                ///capture settings
                CoreAppSettings.Current.CheckForUpdatesAtStartup = (this.CheckForUpdatesCheckbox.IsChecked == true);
                CoreAppSettings.Current.EnableAppTelematics = (this.EnableTelematicsCheckbox.IsChecked == true);
                CoreAppSettings.Current.EnableValidationProxyAPI = (this.EnableProxyAPICheckbox.IsChecked == true);
                CoreAppSettings.Current.EnableDNSValidationChecks = (this.EnableDNSValidationChecks.IsChecked == true);

                CoreAppSettings.Current.EnableEFS = (this.EnableEFS.IsChecked == true);
                CoreAppSettings.Current.IgnoreStoppedSites = (this.IgnoreStoppedSites.IsChecked == true);

                // force renewal interval days to be between 1 and 60 days
                if (this.RenewalIntervalDays.Value == null) this.RenewalIntervalDays.Value = 14;
                if (this.RenewalIntervalDays.Value > 60) this.RenewalIntervalDays.Value = 60;
                CoreAppSettings.Current.RenewalIntervalDays = (int)this.RenewalIntervalDays.Value;

                // force max renewal requests to be between 0 and 100 ( 0 = unlimited)
                if (this.RenewalMaxRequests.Value == null) this.RenewalMaxRequests.Value = 0;
                if (this.RenewalMaxRequests.Value > 100) this.RenewalMaxRequests.Value = 100;
                CoreAppSettings.Current.MaxRenewalRequests = (int)this.RenewalMaxRequests.Value;
                hasChanges = true;
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

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // reload settings
            LoadCurrentSettings();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveAppSettings();
            hasChanges = false;
            Save.IsEnabled = false;
        }
    }
}