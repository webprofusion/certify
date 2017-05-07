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

        public Settings()
        {
            InitializeComponent();
            this.DataContext = MainViewModel;

            //MainViewModel.LoadVaultTree();
            this.CheckForUpdatesCheckbox.IsChecked = Certify.Properties.Settings.Default.CheckForUpdatesAtStartup;
            this.EnableTelematicsCheckbox.IsChecked = Certify.Properties.Settings.Default.EnableAppTelematics;
            this.EnableProxyAPICheckbox.IsChecked = Certify.Properties.Settings.Default.EnableValidationProxyAPI;
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

        private void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            ///capture settings
            Certify.Properties.Settings.Default.CheckForUpdatesAtStartup = (this.CheckForUpdatesCheckbox.IsChecked == true);
            Certify.Properties.Settings.Default.EnableAppTelematics = (this.EnableTelematicsCheckbox.IsChecked == true);
            Certify.Properties.Settings.Default.EnableValidationProxyAPI = (this.EnableProxyAPICheckbox.IsChecked == true);
            ///
            //save
            Certify.Properties.Settings.Default.Save();
        }
    }
}