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

        public Settings()
        {
            InitializeComponent();
            this.DataContext = MainViewModel;

            //MainViewModel.LoadVaultTree();
            this.CheckForUpdatesCheckbox.IsChecked = Certify.Properties.Settings.Default.CheckForUpdatesAtStartup;
            this.EnableTelematicsCheckbox.IsChecked = Certify.Properties.Settings.Default.EnableAppTelematics;
            this.EnableProxyAPICheckbox.IsChecked = Certify.Properties.Settings.Default.EnableValidationProxyAPI;

            //if true, EFS will be used for sensitive files such as private key file, does not work in all versions of windows.
            this.EnableEFS.IsChecked = Certify.Properties.Settings.Default.EnableEFS;
            this.IgnoreStoppedSites.IsChecked = Certify.Properties.Settings.Default.IgnoreStoppedSites;

            this.RenewalIntervalDays.Value = Certify.Properties.Settings.Default.RenewalIntervalDays;

            MainViewModel.LoadVaultTree();
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

            //refresh
            MainViewModel.LoadVaultTree();
        }

        private void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            if (settingsInitialised)
            {
                ///capture settings
                Certify.Properties.Settings.Default.CheckForUpdatesAtStartup = (this.CheckForUpdatesCheckbox.IsChecked == true);
                Certify.Properties.Settings.Default.EnableAppTelematics = (this.EnableTelematicsCheckbox.IsChecked == true);
                Certify.Properties.Settings.Default.EnableValidationProxyAPI = (this.EnableProxyAPICheckbox.IsChecked == true);

                Certify.Properties.Settings.Default.EnableEFS = (this.EnableEFS.IsChecked == true);
                Certify.Properties.Settings.Default.IgnoreStoppedSites = (this.IgnoreStoppedSites.IsChecked == true);

                // force renewal interval days to be between 1 and 60 days
                if (this.RenewalIntervalDays.Value == null) this.RenewalIntervalDays.Value = 7;
                if (this.RenewalIntervalDays.Value > 60) this.RenewalIntervalDays.Value = 60;
                Certify.Properties.Settings.Default.RenewalIntervalDays = (int)this.RenewalIntervalDays.Value;
                ///
                //save
                Certify.Properties.Settings.Default.Save();
            }
        }

        private void RenewalIntervalDays_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            this.SettingsUpdated(sender, e);
        }
    }
}