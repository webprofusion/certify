using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Certify.Locales;
using Certify.Models;
using Certify.UI.Settings;
using Microsoft.ApplicationInsights;

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

            this.Activate();
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
            if (!await _itemViewModel.ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            if (!_appViewModel.IsRegisteredVersion && _appViewModel.ManagedCertificates != null && _appViewModel.ManagedCertificates.Count >= NUM_ITEMS_FOR_REMINDER)
            {
                MessageBox.Show(SR.MainWindow_TrialLimitationReached);

                if (_appViewModel.ManagedCertificates?.Count >= NUM_ITEMS_FOR_LIMIT)
                {
                    return;
                }
            }
            else
            {
                if (_appViewModel.IsRegisteredVersion && _appViewModel.ManagedCertificates?.Count >= 1)
                {
                    var licensingManager = ViewModel.AppViewModel.Current.PluginManager?.LicensingManager;

                    if (licensingManager != null && !await licensingManager.IsInstallActive(ViewModel.AppViewModel.ProductTypeId, Management.Util.GetAppDataFolder()))
                    {
                        MessageBox.Show(Certify.Locales.SR.MainWindow_KeyExpired);

                        if (_appViewModel.ManagedCertificates?.Count >= NUM_ITEMS_FOR_LIMIT)
                        {
                            return;
                        }
                    }
                }
            }

            // check user has registered a contact with LE first
            if (!_appViewModel.HasRegisteredContacts)
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
            var settings = new Models.RenewalSettings { };

            // if ctrl is pressed, force renewal
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                settings.Mode = Models.RenewalMode.All;
            }

            // save or discard site changes before creating a new site/certificate
            if (!await _itemViewModel.ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            //present new renew all confirmation
            if (MessageBox.Show(SR.MainWindow_RenewAllConfirm, SR.Renew_All, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _appViewModel.MainUITabIndex = (int)PrimaryUITabs.CurrentProgress;

                // renewals is a long running process so we need to run renewals process in the
                // background and present UI to show progress.
                // TODO: We should prevent starting the renewals process if it is currently in progress.
                if (_appViewModel.RenewAllCommand.CanExecute(settings))
                {
                    _appViewModel.RenewAllCommand.Execute(settings);
                }
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var uiSettings = UISettings.Load();

            if (uiSettings != null)
            {
                Width = uiSettings.Width ?? Width;
                Height = uiSettings.Height ?? Height;

                Top = uiSettings.Top ?? Top;

                // only apply saved left pos if it's not off-screen
                double virtScreenWidth = System.Windows.SystemParameters.VirtualScreenWidth;
                if (uiSettings.Left < virtScreenWidth)
                {
                    Left = uiSettings.Left ?? Left;
                }

                // set theme based on pref
                if (uiSettings.UITheme != null)
                {
                    ((Certify.UI.App)App.Current).ToggleTheme(uiSettings.UITheme);
                }
                else
                {
                    // default theme
                    ((Certify.UI.App)App.Current).ToggleTheme(_appViewModel.DefaultUITheme);
                }

                if (uiSettings.Scaling > 0.5 && uiSettings.Scaling < 2)
                {
                    _appViewModel.UIScaleFactor = uiSettings.Scaling ?? 1;
                }

                _appViewModel.UISettings = uiSettings;
            }
            else
            {
                // default theme
                ((Certify.UI.App)App.Current).ToggleTheme(_appViewModel.DefaultUITheme);
            }

            await PerformAppStartupChecks();

          
        }


        private async Task PerformAppStartupChecks()
        {
            Mouse.OverrideCursor = Cursors.AppStarting;
            _appViewModel.IsLoading = true;

            var diagnostics = await Management.Util.PerformAppDiagnostics(_appViewModel.Preferences.NtpServer);
            if (diagnostics.Any(d => d.IsSuccess == false))
            {
                _appViewModel.SystemDiagnosticWarning = diagnostics.First(d => d.IsSuccess == false).Message;
            }

            var cts = new CancellationTokenSource();
            
            var connectedOk = await _appViewModel.InitServiceConnections(null, cts.Token);

            if (_appViewModel.IsServiceAvailable)
            {
                await _appViewModel.LoadSettingsAsync();

                // TODO: service diagnostics
                var svc = await _appViewModel.PerformServiceDiagnostics();
                var svcDiag = await _appViewModel.PerformServiceDiagnostics();
                if (svcDiag.Any(d => d.IsSuccess == false))
                {
                    _appViewModel.SystemDiagnosticWarning = svcDiag.First(d => d.IsSuccess == false).Message;
                }
            }

            Mouse.OverrideCursor = Cursors.Arrow;

            // quit if service/service client cannot connect
            if (!_appViewModel.IsServiceAvailable)
            {
                _appViewModel.IsLoading = false;

                var config = _appViewModel.GetAppServiceConfig();

                if (!string.IsNullOrEmpty(config.ServiceFaultMsg))
                {
                    MessageBox.Show("Certify SSL Manager service not started. " + config.ServiceFaultMsg);
                }
                else
                {
                    MessageBox.Show("Certify SSL Manager service not started. Please restart the service. If this problem persists please refer to https://docs.certifytheweb.com/docs/faq.html and if you cannot resolve the problem contact support@certifytheweb.com.");
                }

                App.Current.Shutdown();
                return;
            }

           

            // init telemetry if enabled
            InitTelemetry();

            // setup plugins

            _appViewModel.PluginManager = new Management.PluginManager();

            _appViewModel.PluginManager.LoadPlugins(new List<string> { "Licensing", "DashboardClient" });

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



            if (!_appViewModel.IsRegisteredVersion)
            {
                Title += SR.MainWindow_TitleTrialPostfix;
            }

            //check for updates and report result to view model
            if (_appViewModel.IsServiceAvailable)
            {
                var updateCheck = await _appViewModel.CheckForUpdates();

                if (updateCheck != null && updateCheck.IsNewerVersion)
                {
                    _appViewModel.UpdateCheckResult = updateCheck;
                    _appViewModel.IsUpdateAvailable = true;

                    PerformUpdateConfirmation(updateCheck);
                }
            }
        }

        private void EnsureContactRegistered()
        {
            if (!_appViewModel.HasRegisteredContacts)
            {
                //start by registering
                MessageBox.Show(SR.MainWindow_GetStartGuideWithNewCert);
                var d = new Windows.EditAccountDialog { Owner = Window.GetWindow(this) };

                d.ShowDialog();
            }
        }

        private void InitTelemetry()
        {
            if (_appViewModel.Preferences.EnableAppTelematics)
            {
                tc = new Certify.Management.Util().InitTelemetry(Certify.Locales.ConfigResources.AIInstrumentationKey);
                tc.TrackEvent("Start");
            }
            else
            {
                tc = null;
            }
        }

        private void ButtonUpdateAvailable_Click(object sender, RoutedEventArgs e)
        {
            if (_appViewModel.IsUpdateInProgress)
            {
                return;
            }

            if (_appViewModel.UpdateCheckResult != null)
            {
                this.PerformUpdateConfirmation(_appViewModel.UpdateCheckResult);
            }
        }

        private async void PerformUpdateConfirmation(Models.UpdateCheck updateCheck)
        {

            // offer to start download and notify when ready to apply

            var d = new Windows.UpdateAvailable(updateCheck) { Owner = Window.GetWindow(this) };

            if (d.ShowDialog() == true)
            {
                _appViewModel.IsUpdateInProgress = true;
                UpdateIcon.Spin = true;
                UpdateIcon.Icon = FontAwesome.WPF.FontAwesomeIcon.Superpowers;
                UpdateIcon.SpinDuration = 1;

                _appViewModel.UpdateCheckResult = await new Utils.UpdateCheckUtils().UpdateWithDownload();
                _appViewModel.IsUpdateInProgress = false;
                UpdateIcon.Spin = false;
            }
            else
            {
                // if update is mandatory (where there is a major bug etc) quit until user updates
                if (updateCheck.MustUpdate)
                {
                    // offer to take user to download page
                    var gotoDownload = MessageBox.Show(Application.Current.MainWindow, updateCheck.Message.Body + "\r\nVisit download page now?", ConfigResources.AppName, MessageBoxButton.YesNo);
                    if (gotoDownload == MessageBoxResult.Yes)
                    {
                        Utils.Helpers.LaunchBrowser(ConfigResources.AppWebsiteURL);
                    }
                    else
                    {
                        MessageBox.Show(Application.Current.MainWindow, SR.Update_MandatoryUpdateQuit);
                    }

                    //quit
                    App.Current.Shutdown();
                }
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_appViewModel.IsFeatureEnabled(Models.FeatureFlags.SERVER_CONNECTIONS))
            {
                _appViewModel.ChooseConnection(this);
            }
        }

        private async void MetroWindow_Closing(object sender, CancelEventArgs e)
        {
            // allow cancelling exit to save changes
            if (!await _itemViewModel.ConfirmDiscardUnsavedChanges())
            {
                e.Cancel = true;
            }

            if (_appViewModel.UISettings == null)
            {
                _appViewModel.UISettings = new UISettings();
            }

            _appViewModel.UISettings.Width = Width;
            _appViewModel.UISettings.Height = Height;
            _appViewModel.UISettings.Left = Left;
            _appViewModel.UISettings.Top = Top;
            _appViewModel.UISettings.Scaling = _appViewModel.UIScaleFactor;

            UISettings.Save(_appViewModel.UISettings);
        }
    }
}
