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
        public enum PrimaryUITabs
        {
            ManagedItems = 0,

            CurrentProgress = 1
        }

        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return UI.ViewModel.AppModel.AppViewModel;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                this.DataContext = MainViewModel;
            }
        }

        private void Button_NewCertificate(object sender, RoutedEventArgs e)
        {
            //present new managed item (certificate request) UI
            if (!MainViewModel.IsRegisteredVersion && MainViewModel.ManagedSites != null && MainViewModel.ManagedSites.Count >= 5)
            {
                MessageBox.Show("You are using the trial version of this app. Please purchase a registration key to upgrade. See the Register option on the About tab.");
                return;
            }

            //select tab Managed Items
            MainViewModel.MainUITabIndex = (int)PrimaryUITabs.ManagedItems;

            MainViewModel.SelectedItem = new Certify.Models.ManagedSite { Name = "New Managed Certificate" };
        }

        private void Button_RenewAll(object sender, RoutedEventArgs e)
        {
            //present new renew all confirmation
            if (MessageBox.Show("This will renew certificates for all auto-renewed items. Proceed?", "Renew All", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                MainViewModel.MainUITabIndex = (int)PrimaryUITabs.CurrentProgress;

                bool autoRenewalsOnly = true;
                // renewals is a long running process so we need to run renewals process in the background and present UI to show progress.
                // TODO: We should prevent starting the renewals process if it is currently in progress.
                if (MainViewModel.RenewAllCommand.CanExecute(autoRenewalsOnly))
                {
                    MainViewModel.RenewAllCommand.Execute(autoRenewalsOnly);
                }
            }
        }

        private void Button_ScheduledTaskConfig(object sender, RoutedEventArgs e)
        {
            //show UI to update auto renewal task
            var d = new Windows.ScheduledTaskConfig { Owner = this };
            d.ShowDialog();
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //check for any startup actions required such as vault import

            if (MainViewModel.ImportedManagedSites.Any())
            {
                //show import ui
                Task.Delay(100);
                var d = new Windows.ImportManagedSites { Owner = this };
                d.ShowDialog();
            }

            if (!MainViewModel.IsRegisteredVersion)
            {
                this.Title += " [Free Trial Version]";
            }
        }

        private void MetroWindow_ContentRendered(object sender, EventArgs e)
        {
            if (!MainViewModel.HasRegisteredContacts)
            {
                //start by registering
                MessageBox.Show("Get started by registering a new contact, then you can start requesting certificates.");
                var d = new Windows.EditContactDialog { Owner = this };
                d.ShowDialog();
            }
        }

        private void ButtonUpdateAvailable_Click(object sender, RoutedEventArgs e)
        {
            if (MainViewModel.UpdateCheckResult != null)
            {
                var gotoDownload = MessageBox.Show(MainViewModel.UpdateCheckResult.Message.Body + "\r\nVisit download page now?", Core.Properties.Resources.AppName, MessageBoxButton.YesNo);
                if (gotoDownload == MessageBoxResult.Yes)
                {
                    System.Diagnostics.ProcessStartInfo sInfo = new System.Diagnostics.ProcessStartInfo(MainViewModel.UpdateCheckResult.Message.DownloadPageURL);
                    System.Diagnostics.Process.Start(sInfo);
                }
            }
        }
    }
}