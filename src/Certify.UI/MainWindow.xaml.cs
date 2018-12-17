using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Certify.Locales;
using Microsoft.ApplicationInsights;
using Serilog;

namespace Certify.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public enum PrimaryUITabs
        {
            ManagedCertificates = 0,

            CurrentProgress = 1
        }

        private TelemetryClient tc = null;

        protected Certify.UI.ViewModel.AppViewModel _appViewModel => UI.ViewModel.AppViewModel.Current;
        protected Certify.UI.ViewModel.ManagedCertificateViewModel _itemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;
        private const int NUM_ITEMS_FOR_REMINDER = 3;
        private const int NUM_ITEMS_FOR_LIMIT = 10;

        public int NumManagedCertificates
        {
            get
            {
                if (_appViewModel != null && _appViewModel.ManagedCertificates != null)
                {
                    return _appViewModel.ManagedCertificates.Count;
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
            DataContext = _appViewModel;
        }

        private async void Button_NewCertificate(object sender, RoutedEventArgs e)
        {
#if ALPHA
            MessageBox.Show("You are using an alpha version of Certify The Web. You should only use this version for testing and should not consider it suitable for use on production servers.");
#endif

#if BETA
            MessageBox.Show("You are using a beta version of Certify The Web. Please report any issues you find.");
#endif

            // save or discard site changes before creating a new site/certificate
            if (!await _itemViewModel.ConfirmDiscardUnsavedChanges()) return;

            if (!_appViewModel.IsRegisteredVersion && _appViewModel.ManagedCertificates != null && _appViewModel.ManagedCertificates.Count >= 3)
            {
                MessageBox.Show(SR.MainWindow_TrialLimitationReached);

                if (_appViewModel.ManagedCertificates?.Count >= NUM_ITEMS_FOR_LIMIT)
                {
                    return;
                }
            }
            else
            {
                if (_appViewModel.IsRegisteredVersion && _appViewModel.ManagedCertificates?.Count >= NUM_ITEMS_FOR_REMINDER)
                {
                    var licensingManager = ViewModel.AppViewModel.Current.PluginManager?.LicensingManager;

                    if (licensingManager != null && !await licensingManager.IsInstallActive(ViewModel.AppViewModel.ProductTypeId, Management.Util.GetAppDataFolder()))
                    {
                        _appViewModel.IsRegisteredVersion = false;
                    }
                }
            }

            // check user has registered a contact with LE first
            if (string.IsNullOrEmpty(_appViewModel.PrimaryContactEmail))
            {
                EnsureContactRegistered();
                return;
            }

            //present new managed item (certificate request) UI
            //select tab Managed Items
            _appViewModel.MainUITabIndex = (int)PrimaryUITabs.ManagedCertificates;

            _appViewModel.SelectedItem = null; // deselect site list item
            _appViewModel.SelectedItem = new Certify.Models.ManagedCertificate();

            //default to auto deploy for new managed certs
            _appViewModel.SelectedItem.RequestConfig.DeploymentSiteOption = Models.DeploymentOption.Auto; 
        }

        private async void Button_RenewAll(object sender, RoutedEventArgs e)
        {
            // save or discard site changes before creating a new site/certificate
            if (!await _itemViewModel.ConfirmDiscardUnsavedChanges()) return;

            //present new renew all confirmation
            if (MessageBox.Show(SR.MainWindow_RenewAllConfirm, SR.Renew_All, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _appViewModel.MainUITabIndex = (int)PrimaryUITabs.CurrentProgress;

                bool autoRenewalsOnly = true;
                // renewals is a long running process so we need to run renewals process in the
                // background and present UI to show progress.
                // TODO: We should prevent starting the renewals process if it is currently in progress.
                if (_appViewModel.RenewAllCommand.CanExecute(autoRenewalsOnly))
                {
                    _appViewModel.RenewAllCommand.Execute(autoRenewalsOnly);
                }
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await PerformAppStartupChecks();
        }

        private async Task PerformAppStartupChecks()
        {
            Mouse.OverrideCursor = Cursors.AppStarting;
            _appViewModel.IsLoading = true;

            await _appViewModel.InitServiceConnections();

            if (_appViewModel.IsServiceAvailable)
            {
                await _appViewModel.LoadSettingsAsync();
            }

            Mouse.OverrideCursor = Cursors.Arrow;

            // quit if service/service client cannot connect
            if (!_appViewModel.IsServiceAvailable)
            {
                _appViewModel.IsLoading = false;

                var config = _appViewModel.CertifyClient.GetAppServiceConfig();
                if (!string.IsNullOrEmpty(config.ServiceFaultMsg))
                {
                    MessageBox.Show("Certify SSL Manager service not started. "+ config.ServiceFaultMsg);
                } else
                {
                    MessageBox.Show("Certify SSL Manager service not started. Please restart the service. If this problem persists please refer to https://docs.certifytheweb.com/docs/faq.html and if you cannot resolve the problem contact support@certifytheweb.com.");
                }

                
                App.Current.Shutdown();
                return;
            }

            var diagnostics = await Management.Util.PerformAppDiagnostics();
            if (diagnostics.Any(d => d.IsSuccess == false))
            {
                MessageBox.Show(diagnostics.First(d => d.IsSuccess == false).Message, "Warning");
            }

            // init telemetry if enabled
            InitTelemetry();

            // setup plugins

            _appViewModel.PluginManager = new Management.PluginManager();

            _appViewModel.PluginManager.LoadPlugins();

            var licensingManager = _appViewModel.PluginManager.LicensingManager;
            if (licensingManager != null)
            {
                if (licensingManager.IsInstallRegistered(ViewModel.AppViewModel.ProductTypeId, Certify.Management.Util.GetAppDataFolder()))
                {
                    _appViewModel.IsRegisteredVersion = true;
                }
            }

            // check if IIS is available, if so also populates IISVersion
            await _appViewModel.CheckServerAvailability(Models.StandardServerTypes.IIS);

            _appViewModel.IsLoading = false;

            // check if primary contact registered with LE
            EnsureContactRegistered();

            if (!_appViewModel.IsRegisteredVersion)
            {
                this.Title += SR.MainWindow_TitleTrialPostfix;
            }

            //check for updates and report result to view model
            if (_appViewModel.IsServiceAvailable)
            {
                var updateCheck = await _appViewModel.CertifyClient.CheckForUpdates();

                if (updateCheck != null && updateCheck.IsNewerVersion)
                {
                    _appViewModel.UpdateCheckResult = updateCheck;
                    _appViewModel.IsUpdateAvailable = true;

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
            if (!_appViewModel.HasRegisteredContacts)
            {
                //start by registering
                MessageBox.Show(SR.MainWindow_GetStartGuideWithNewCert);
                var d = new Windows.EditContactDialog { };
                d.ShowDialog();
            }
        }

        private void InitTelemetry()
        {
            if (_appViewModel.Preferences.EnableAppTelematics)
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
            if (_appViewModel.IsUpdateInProgress) return;

            if (_appViewModel.UpdateCheckResult != null)
            {
                // offer to start download and notify when ready to apply
                if (MessageBox.Show(_appViewModel.UpdateCheckResult.Message.Body + "\r\n" + SR.Update_DownloadNow, ConfigResources.AppName, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _appViewModel.IsUpdateInProgress = true;
                    UpdateIcon.Spin = true;
                    UpdateIcon.SpinDuration = 1;

                    _appViewModel.UpdateCheckResult = await new Utils.UpdateCheckUtils().UpdateWithDownload();
                    _appViewModel.IsUpdateInProgress = false;
                    UpdateIcon.Spin = false;
                }
                else
                {
                    // otherwise offer to go to download page
                    var gotoDownload = MessageBox.Show(_appViewModel.UpdateCheckResult.Message.Body + "\r\n" + SR.MainWindow_VisitDownloadPage, ConfigResources.AppName, MessageBoxButton.YesNo);
                    if (gotoDownload == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.ProcessStartInfo sInfo = new System.Diagnostics.ProcessStartInfo(_appViewModel.UpdateCheckResult.Message.DownloadPageURL);
                        System.Diagnostics.Process.Start(sInfo);
                    }
                }
            }
        }

        private async void MetroWindow_Closing(object sender, CancelEventArgs e)
        {
            // allow cancelling exit to save changes
            if (!await _itemViewModel.ConfirmDiscardUnsavedChanges()) e.Cancel = true;
        }
    }
}
