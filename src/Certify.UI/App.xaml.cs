using MahApps.Metro;
using Microsoft.ApplicationInsights;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Certify.UI
{
    /// <summary>
    /// Interaction logic for App.xaml 
    /// </summary>
    public partial class App : Application
    {
        private TelemetryClient tc = null;

        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return UI.ViewModel.AppModel.AppViewModel;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            /*
            // get the current app style (theme and accent) from the application you can then use the
            // current theme and custom accent instead set a new theme
            Tuple<AppTheme, Accent> appStyle = ThemeManager.DetectAppStyle(Application.Current);

            // now set the Green accent and dark theme
            ThemeManager.ChangeAppStyle(Application.Current,
                                        ThemeManager.GetAccent("Green"),
                                        ThemeManager.GetAppTheme("BaseLight")); // or appStyle.Item1
*/

            // upgrade assembly version of saved settings (if required)
            Certify.Properties.Settings.Default.UpgradeSettingsVersion(); // deprecated
            Certify.Management.SettingsManager.LoadAppSettings();

            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);

            MainViewModel.LoadSettings();

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

            //check for updates and report result to view model
            if (Management.CoreAppSettings.Current.CheckForUpdatesAtStartup)
            {
                Task.Run(async () =>
                {
                    var updateCheck = await new Certify.Management.Util().CheckForUpdates();
                    if (updateCheck != null && updateCheck.IsNewerVersion)
                    {
                        MainViewModel.IsUpdateAvailable = true;
                        MainViewModel.UpdateCheckResult = updateCheck;
                    }
                });
            }

            //init telemetry if enabled
            InitTelemetry();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var feedbackMsg = "";
            if (e.ExceptionObject != null)
            {
                feedbackMsg = "An error occurred: " + ((Exception)e.ExceptionObject).ToString();
            }

            var d = new Windows.Feedback(feedbackMsg, isException: true);
            d.ShowDialog();
        }

        private void InitTelemetry()
        {
            if (Management.CoreAppSettings.Current.EnableAppTelematics)
            {
                tc = new Certify.Management.Util().InitTelemetry();
                tc.TrackEvent("Start");
            }
            else
            {
                tc = null;
            }
        }
    }
}