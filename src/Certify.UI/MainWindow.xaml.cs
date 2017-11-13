using Certify.Locales;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

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

        public int NumManagedSites
        {
            get
            {
                if (MainViewModel != null && MainViewModel.ManagedSites != null)
                {
                    return MainViewModel.ManagedSites.Count;
                }
                else
                {
                    return 0;
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = MainViewModel;
        }

        private async void Button_NewCertificate(object sender, RoutedEventArgs e)
        {
            // save or discard site changes before creating a new site/certificate
            if (!await MainViewModel.ConfirmDiscardUnsavedChanges()) return;

            //present new managed item (certificate request) UI
            if (!MainViewModel.IsRegisteredVersion && MainViewModel.ManagedSites != null && MainViewModel.ManagedSites.Count >= 5)
            {
                MessageBox.Show(SR.MainWindow_TrialLimitionReached);
                return;
            }

            //select tab Managed Items
            MainViewModel.MainUITabIndex = (int)PrimaryUITabs.ManagedItems;
            MainViewModel.SelectedWebSite = null;
            MainViewModel.SelectedItem = null; // deselect site list item
            MainViewModel.SelectedItem = new Certify.Models.ManagedSite();
        }

        private async void Button_RenewAll(object sender, RoutedEventArgs e)
        {
            // save or discard site changes before creating a new site/certificate
            if (!await MainViewModel.ConfirmDiscardUnsavedChanges()) return;

            //present new renew all confirmation
            if (MessageBox.Show(SR.MainWindow_RenewAllConfirm, SR.Renew_All, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                MainViewModel.MainUITabIndex = (int)PrimaryUITabs.CurrentProgress;

                bool autoRenewalsOnly = true;
                // renewals is a long running process so we need to run renewals process in the
                // background and present UI to show progress.
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
            if (!MainViewModel.IsRegisteredVersion)
            {
                this.Title += SR.MainWindow_TitleTrialPostfix;
            }
        }

        private void MetroWindow_ContentRendered(object sender, EventArgs e)
        {
            //check for updates and report result to view model
            /*if (MainViewModel.Preferences.CheckForUpdatesAtStartup)
            {
                var updateCheck = await new Certify.Management.Util().CheckForUpdates();
                if (updateCheck != null && updateCheck.IsNewerVersion)
                {
                    MainViewModel.UpdateCheckResult = updateCheck;
                    MainViewModel.IsUpdateAvailable = true;
                }
            }*/
        }

        private void ButtonUpdateAvailable_Click(object sender, RoutedEventArgs e)
        {
            if (MainViewModel.UpdateCheckResult != null)
            {
                var gotoDownload = MessageBox.Show(MainViewModel.UpdateCheckResult.Message.Body + "\r\n" + SR.MainWindow_VisitDownloadPage, ConfigResources.AppName, MessageBoxButton.YesNo);
                if (gotoDownload == MessageBoxResult.Yes)
                {
                    System.Diagnostics.ProcessStartInfo sInfo = new System.Diagnostics.ProcessStartInfo(MainViewModel.UpdateCheckResult.Message.DownloadPageURL);
                    System.Diagnostics.Process.Start(sInfo);
                }
            }
        }

        private async void MetroWindow_Closing(object sender, CancelEventArgs e)
        {
            // allow cancelling exit to save changes
            if (!await MainViewModel.ConfirmDiscardUnsavedChanges()) e.Cancel = true;
        }
    }
}