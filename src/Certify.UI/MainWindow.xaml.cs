using Certify.UI.ViewModel;
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

namespace Certify.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private enum PrimaryTabs
        {
            ManagedItems = 0,
            Vault = 1,
            CurrentProgress = 2
        }

        public MainWindow()
        {
            InitializeComponent();

            var appViewModel = new ViewModelLocator().Main;
            appViewModel.LoadSettings();
        }

        private void Button_NewCertificate(object sender, RoutedEventArgs e)
        {
            //present new managed item (certificate request) UI

            //select tab Managed Items
            this.MainTabControl.TabIndex = (int)PrimaryTabs.ManagedItems;
            AppModel.AppViewModel.SelectedItem = new Certify.Models.ManagedSite { Name = "New Managed Certificate" };
        }

        private void Button_NewContact(object sender, RoutedEventArgs e)
        {
            //present new contact dialog
            var d = new Windows.EditContactDialog();
            d.Owner = this;
            d.ShowDialog();
        }

        private void Button_RenewAll(object sender, RoutedEventArgs e)
        {
            //present new renew all confirmation
            if (MessageBox.Show("This will renew certificates for all auto-renewed items. Proceed?", "Renew All", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                this.MainTabControl.TabIndex = (int)PrimaryTabs.CurrentProgress;
                // TODO: this is a long running process so we need to run renewals process in the background and present UI to (optionally) show progress.
                // We should prevent starting the renewals process if it is currently in progress.
                var results = ViewModel.AppModel.AppViewModel.RenewAll();
            }
        }
    }
}