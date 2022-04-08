using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Certify.Locales;
using Certify.Management;
using Certify.Models;
using Certify.UI.Settings;
using Certify.UI.Shared;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private TelemetryManager tc;

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

            Activate();
        }

        private async Task NewCertificate(ManagedCertificate original = null)
        {

            // save or discard site changes before creating a new site/certificate
            if (!await _itemViewModel.ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            if (!_appViewModel.IsRegisteredVersion && _appViewModel.NumManagedCerts >= NUM_ITEMS_FOR_REMINDER)
            {
                MessageBox.Show(SR.MainWindow_TrialLimitationReached);

                if (_appViewModel.NumManagedCerts >= NUM_ITEMS_FOR_LIMIT)
                {
                    return;
                }
            }
            else
            {
                if (_appViewModel.IsRegisteredVersion && _appViewModel.NumManagedCerts >= 1)
                {
                    if (await _appViewModel.CheckLicenseIsActive() == false)
                    {
                        MessageBox.Show(Certify.Locales.SR.MainWindow_KeyExpired);

                        if (_appViewModel.NumManagedCerts >= NUM_ITEMS_FOR_LIMIT)
                        {
                            return;
                        }
                    }
                }
            }

            // check user has registered a contact with ACME CA first
            if (EnsureContactRegistered())
            {

                //present new managed item (certificate request) UI
                //select tab Managed Items
                _appViewModel.MainUITabIndex = (int)PrimaryUITabs.ManagedCertificates;

                _appViewModel.SelectedItem = null; // deselect site list item

                if (original != null)
                {
                    // start new from duplicate
                    var duplicate = original.CopyAsTemplate(preserveAttributes: true);
                    duplicate.Id = null;

                    _appViewModel.SelectedItem = duplicate;

                }
                else
                {
                    // start new
                    _appViewModel.SelectedItem = new Certify.Models.ManagedCertificate();

                    //default to auto deploy for new managed certs
                    _appViewModel.SelectedItem.RequestConfig.DeploymentSiteOption = Models.DeploymentOption.Auto;
                }
            }
        }

        private async void Button_NewCertificate(object sender, RoutedEventArgs e)
        {
            await NewCertificate();

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
            var uiSettings = _appViewModel.UISettings;

            if (uiSettings != null)
            {
                try
                {
                    // only apply saved left pos if it's not off-screen
                    var virtScreenWidth = System.Windows.SystemParameters.VirtualScreenWidth;
                    var virtScreenHeight = System.Windows.SystemParameters.VirtualScreenHeight;

                    if (uiSettings.Width < virtScreenWidth)
                    {
                        Width = uiSettings.Width ?? Width;
                    }

                    if (uiSettings.Height < virtScreenHeight)
                    {
                        Height = uiSettings.Height ?? Height;
                    }

                    if (uiSettings.Top >= 0 && uiSettings.Top < (virtScreenHeight - Height))
                    {
                        Top = (double)uiSettings.Top;
                    }

                    if (uiSettings.Left >= 0 && uiSettings.Left < (virtScreenWidth - Width))
                    {
                        Left = (double)uiSettings.Left;
                    }

                    // set theme based on pref
                    if (uiSettings.UITheme != null)
                    {
                        ((ICertifyApp)_appViewModel.GetApplication()).ToggleTheme(uiSettings.UITheme);
                    }
                    else
                    {
                        // default theme
                        ((ICertifyApp)_appViewModel.GetApplication()).ToggleTheme(_appViewModel.DefaultUITheme);
                    }

                    if (uiSettings.Scaling > 0.5 && uiSettings.Scaling < 2)
                    {
                        _appViewModel.UIScaleFactor = uiSettings.Scaling ?? 1;
                    }

                    _appViewModel.UISettings = uiSettings;
                }
                catch
                {
                    // failed to get window position etc
                }
            }
            else
            {
                _appViewModel.UISettings = new UISettings();

                // default theme
                ((ICertifyApp)_appViewModel.GetApplication()).ToggleTheme(_appViewModel.DefaultUITheme);
            }

            await PerformAppStartupChecks();

        }

        private async Task PerformAppStartupChecks()
        {
            Mouse.OverrideCursor = Cursors.AppStarting;
            _appViewModel.IsLoading = true;

            // setup plugins

            _appViewModel.PluginManager = new Management.PluginManager();

            _appViewModel.PluginManager.LoadPlugins(new List<string> { "Licensing", "DashboardClient" });

            var licensingManager = _appViewModel.PluginManager.LicensingManager;
            if (licensingManager != null)
            {
                if (licensingManager.IsInstallRegistered(ViewModel.AppViewModel.ProductTypeId, EnvironmentUtil.GetAppDataFolder()))
                {
                    _appViewModel.IsRegisteredVersion = true;
                }
            }

            // setup connection to background service

            var cts = new CancellationTokenSource();

            var connectedOk = await _appViewModel.InitServiceConnections(null, cts.Token);

            if (_appViewModel.IsServiceAvailable && !connectedOk)
            {
                // on some slower systems the service may connect but the status hub stream might fail (not yet started), try again
                await Task.Delay(2000);
                connectedOk = await _appViewModel.InitServiceConnections(null, cts.Token);

                if (!connectedOk)
                {
                    _appViewModel.Log.Error("Service connected, but status stream failed.");
                }
            }

            if (_appViewModel.IsServiceAvailable)
            {
                await _appViewModel.LoadSettingsAsync();

                // service host diagnostics

                var svcDiag = await _appViewModel.PerformServiceDiagnostics();
                if (svcDiag.Any(d => d.IsSuccess == false))
                {
                    _appViewModel.SystemDiagnosticWarning = svcDiag.First(d => d.IsSuccess == false).Message;
                }
            }

            var diagnostics = await Management.Util.PerformAppDiagnostics(includeTempFileCheck: false, _appViewModel.Preferences.NtpServer);
            if (diagnostics.Any(d => d.IsSuccess == false))
            {
                _appViewModel.SystemDiagnosticWarning = diagnostics.First(d => d.IsSuccess == false).Message;
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
                    MessageBox.Show("Certify SSL Manager service not started. Please restart the service. If this problem persists please refer to https://docs.certifytheweb.com/docs/faq and if you cannot resolve the problem contact support@certifytheweb.com.");
                }

                if (_appViewModel.IsFeatureEnabled(FeatureFlags.SERVER_CONNECTIONS))
                {
                    // user can select a connection
                }
                else
                {
                    _appViewModel.GetApplication().Shutdown();
                }

                return;
            }

            // init telemetry if enabled
            InitTelemetry();

            // check if IIS is available, if so also populates IISVersion
            await _appViewModel.CheckServerAvailability(Models.StandardServerTypes.IIS);

            _appViewModel.IsLoading = false;

            if (!_appViewModel.IsRegisteredVersion)
            {
                Title += SR.MainWindow_TitleTrialPostfix;
            }
            else
            {
                _appViewModel.IsLicenseExpired = await _appViewModel.CheckLicenseIsActive() == false;
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

        private bool EnsureContactRegistered()
        {
            if (!_appViewModel.HasRegisteredContacts)
            {
                //start by registering
                MessageBox.Show(SR.MainWindow_GetStartGuideWithNewCert);
                var d = new Windows.EditAccountDialog { Owner = Window.GetWindow(this) };

                d.ShowDialog();
            }

            return _appViewModel.HasRegisteredContacts;
        }

        private void InitTelemetry()
        {
            if (_appViewModel.Preferences.EnableAppTelematics)
            {
                tc = new TelemetryManager(Certify.Locales.ConfigResources.AIInstrumentationKey);
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
                PerformUpdateConfirmation(_appViewModel.UpdateCheckResult);
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
                    _appViewModel.GetApplication().Shutdown();
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
            // allow canceling exit to save changes
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

        private async void ManagedCertificates_OnDuplicate(ManagedCertificate original)
        {
            await Application.Current.Dispatcher.InvokeAsync(async delegate
            {
                await NewCertificate(original);
            });
        }
    }
}
