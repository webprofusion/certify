using Certify.Locales;
using Microsoft.ApplicationInsights;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

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

        private TelemetryClient tc = null;

        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return UI.ViewModel.AppModel.Current;
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

            if (MainViewModel.IsRegisteredVersion)
            {
                var licensingManager = ViewModel.AppModel.Current.PluginManager?.LicensingManager;

                if (licensingManager != null && !await licensingManager.IsInstallActive(ViewModel.AppModel.ProductTypeId, Management.Util.GetAppDataFolder()))
                {
                    MainViewModel.IsRegisteredVersion = false;
                }
            }

            if (!MainViewModel.IsRegisteredVersion && MainViewModel.ManagedSites != null && MainViewModel.ManagedSites.Count >= 5)
            {
                MessageBox.Show(SR.MainWindow_TrialLimitationReached);
                return;
            }

            // check user has registered a contact with LE first
            if (String.IsNullOrEmpty(MainViewModel.PrimaryContactEmail))
            {
                EnsureContactRegistered();
                return;
            }

            //present new managed item (certificate request) UI
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

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await PerformAppStartupChecks();
        }

        private async Task PerformAppStartupChecks()
        {
            Mouse.OverrideCursor = Cursors.AppStarting;
            MainViewModel.IsLoading = true;

            await MainViewModel.InitServiceConnections();

            if (MainViewModel.IsServiceAvailable)
            {
                await MainViewModel.LoadSettingsAsync();
            }

            Mouse.OverrideCursor = Cursors.Arrow;

            // quit if service/service client cannot connect
            if (!MainViewModel.IsServiceAvailable)
            {
                MainViewModel.IsLoading = false;
                MessageBox.Show("Certify SSL Manager service is not started. Please restart the service.");
                App.Current.Shutdown();
                return;
            }

            //init telemetry if enabled
            InitTelemetry();

            //check version capabilities
            MainViewModel.PluginManager = new Management.PluginManager();

            MainViewModel.PluginManager.LoadPlugins();

            var licensingManager = MainViewModel.PluginManager.LicensingManager;
            if (licensingManager != null)
            {
                if (licensingManager.IsInstallRegistered(ViewModel.AppModel.ProductTypeId, Certify.Management.Util.GetAppDataFolder()))
                {
                    MainViewModel.IsRegisteredVersion = true;
                }
            }

            //check for any startup actions required such as vault import

            /* if (!this.MainViewModel.ManagedSites.Any())
             {
                 //if we have a vault, preview import.
                 this.MainViewModel.PreviewImport(sanMergeMode: true);
             }*/

            // check if IIS is available, if so also populates IISVersion
            await MainViewModel.CheckServerAvailability(Models.StandardServerTypes.IIS);

            MainViewModel.IsLoading = false;

            if (MainViewModel.IsIISAvailable)
            {
                if (MainViewModel.ImportedManagedSites.Any())
                {
                    //show import ui
                    var d = new Windows.ImportManagedSites();
                    d.ShowDialog();
                }
            }
            else
            {
                //warn if IIS not detected
                MessageBox.Show(SR.MainWindow_IISNotAvailable);
            }

            // check if primary contact registered with LE
            EnsureContactRegistered();

            if (!MainViewModel.IsRegisteredVersion)
            {
                this.Title += SR.MainWindow_TitleTrialPostfix;
            }

            //check for updates and report result to view model
            if (MainViewModel.IsServiceAvailable)
            {
                var updateCheck = await MainViewModel.CertifyClient.CheckForUpdates();

                if (updateCheck != null && updateCheck.IsNewerVersion)
                {
                    MainViewModel.UpdateCheckResult = updateCheck;
                    MainViewModel.IsUpdateAvailable = true;

                    //TODO: move this to UpdateCheckUtils and share with update from About page
                    // if update is mandatory (where there is a major bug etc) quit until user updates
                    if (updateCheck.MustUpdate)
                    {
                        // offer to take user to download page
                        var gotoDownload = MessageBox.Show(updateCheck.Message.Body + "\r\nVisit download page now?", ConfigResources.AppName, MessageBoxButton.YesNo);
                        if (gotoDownload == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.ProcessStartInfo sInfo = new System.Diagnostics.ProcessStartInfo(ConfigResources.AppWebsiteURL);
                            System.Diagnostics.Process.Start(sInfo);
                        }
                        else
                        {
                            MessageBox.Show(SR.Update_MandatoryUpdateQuit);
                        }

                        //quit
                        App.Current.Shutdown();
                    }
                }
            }
        }

        private void EnsureContactRegistered()
        {
            if (!MainViewModel.HasRegisteredContacts)
            {
                //start by registering
                MessageBox.Show(SR.MainWindow_GetStartGuideWithNewCert);
                var d = new Windows.EditContactDialog { };
                d.ShowDialog();
            }
        }

        private void InitTelemetry()
        {
            if (MainViewModel.Preferences.EnableAppTelematics)
            {
                tc = new Certify.Management.Util().InitTelemetry();
                tc.TrackEvent("Start");
            }
            else
            {
                tc = null;
            }
        }

        private async void ButtonUpdateAvailable_Click(object sender, RoutedEventArgs e)
        {
            if (MainViewModel.IsUpdateInProgress) return;

            if (MainViewModel.UpdateCheckResult != null)
            {
                // offer to start download and notify when ready to apply
                if (MessageBox.Show(MainViewModel.UpdateCheckResult.Message.Body + "\r\n" + SR.Update_DownloadNow, ConfigResources.AppName, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    MainViewModel.IsUpdateInProgress = true;
                    UpdateIcon.Spin = true;
                    UpdateIcon.SpinDuration = 1;

                    MainViewModel.UpdateCheckResult = await new Utils.UpdateCheckUtils().UpdateWithDownload();
                    MainViewModel.IsUpdateInProgress = false;
                    UpdateIcon.Spin = false;
                }
                else
                {
                    // otherwise offer to go to download page
                    var gotoDownload = MessageBox.Show(MainViewModel.UpdateCheckResult.Message.Body + "\r\n" + SR.MainWindow_VisitDownloadPage, ConfigResources.AppName, MessageBoxButton.YesNo);
                    if (gotoDownload == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.ProcessStartInfo sInfo = new System.Diagnostics.ProcessStartInfo(MainViewModel.UpdateCheckResult.Message.DownloadPageURL);
                        System.Diagnostics.Process.Start(sInfo);
                    }
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