using System;
using System.Windows;

namespace Certify.UI
{
    /// <summary>
    /// Interaction logic for App.xaml 
    /// </summary>
    public partial class App : Application
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return UI.ViewModel.AppModel.Current;
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

            // Test translations
            //System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("zh-HANS");

            // upgrade assembly version of saved settings (if required)
            //Certify.Properties.Settings.Default.UpgradeSettingsVersion(); // deprecated
            //Certify.Management.SettingsManager.LoadAppSettings();

            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);
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
    }
}