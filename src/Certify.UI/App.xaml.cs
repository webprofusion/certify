using System;
using System.Windows;
using Serilog;

namespace Certify.UI
{
    /// <summary>
    /// Interaction logic for App.xaml 
    /// </summary>
    public partial class App : Application
    {

        protected Certify.UI.ViewModel.AppViewModel MainViewModel
        {
            get
            {
                return UI.ViewModel.AppViewModel.Current;
            }
        }

        protected Models.Providers.ILog Log
        {
            get
            {
                return MainViewModel.Log;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            /*

            // get the current app style (theme and accent) from the application you can then use the
            // current theme and custom accent instead set a new theme
            Tuple<MahApps.Metro.AppTheme, MahApps.Metro.Accent> appStyle = MahApps.Metro.ThemeManager.DetectAppStyle(Application.Current);

            // now set the Green accent and dark theme
            MahApps.Metro.ThemeManager.ChangeAppStyle(Application.Current,
                                        MahApps.Metro.ThemeManager.GetAccent("Red"),
                                        MahApps.Metro.ThemeManager.GetAppTheme("BaseLight"));
*/

            // Test translations
            //System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("zh-HANS");

            // upgrade assembly version of saved settings (if required)
            //Certify.Properties.Settings.Default.UpgradeSettingsVersion(); // deprecated
            //Certify.Management.SettingsManager.LoadAppSettings();

            var currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);

            Log?.Information("UI Startup");
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
          
            var feedbackMsg = "";
            if (e.ExceptionObject != null)
            {
                feedbackMsg = "An error occurred: " + ((Exception)e.ExceptionObject).ToString();

                Log?.Error(feedbackMsg);
            }

            var d = new Windows.Feedback(feedbackMsg, isException: true);
            d.ShowDialog();
        }
    }
}
