using System;
using System.Windows;
using Serilog;
using ToastNotifications;
using ToastNotifications.Lifetime;
using ToastNotifications.Position;
using ToastNotifications.Messages;

namespace Certify.UI
{
    /// <summary>
    /// Interaction logic for App.xaml 
    /// </summary>
    public partial class App : Application
    {

        private Notifier _notifier;

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

        public string ToggleTheme(string initialTheme = null)
        {
            var appStyle = MahApps.Metro.ThemeManager.DetectAppStyle(Application.Current);

            if ((appStyle.Item1.Name == "BaseLight" && initialTheme == null) || initialTheme == "Dark")
            {
                MahApps.Metro.ThemeManager.ChangeAppStyle(Application.Current,
                                     MahApps.Metro.ThemeManager.GetAccent("Green"),
                                     MahApps.Metro.ThemeManager.GetAppTheme("BaseDark"));
                return "Dark";

            }
            else
            {
                MahApps.Metro.ThemeManager.ChangeAppStyle(Application.Current,
                                     MahApps.Metro.ThemeManager.GetAccent("Green"),
                                     MahApps.Metro.ThemeManager.GetAppTheme("BaseLight"));
                return "Light";
            }
        }
        protected override void OnStartup(StartupEventArgs e)
        {



            // Test translations
            //System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("zh-HANS");

            // upgrade assembly version of saved settings (if required)
            //Certify.Properties.Settings.Default.UpgradeSettingsVersion(); // deprecated
            //Certify.Management.SettingsManager.LoadAppSettings();

            var currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);

            Log?.Information("UI Startup");

            // setup notifications toast handler
            _notifier = new Notifier(cfg =>
            {
                cfg.PositionProvider = new WindowPositionProvider(
                    parentWindow: Application.Current.MainWindow,
                    corner: Corner.TopRight,
                    offsetX: 10,
                    offsetY: 10);

                cfg.LifetimeSupervisor = new TimeAndCountBasedLifetimeSupervisor(
                    notificationLifetime: TimeSpan.FromSeconds(3),
                    maximumNotificationCount: MaximumNotificationCount.FromCount(5));

                cfg.Dispatcher = Application.Current.Dispatcher;
            });
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

        public enum NotificationType
        {
            Info = 1,
            Success = 2,
            Error = 3,
            Warning = 4
        }
        public void ShowNotification(string msg, NotificationType type = NotificationType.Info, bool autoClose = true)
        {
            var opts = new ToastNotifications.Core.MessageOptions { ShowCloseButton = false };

            if (type == NotificationType.Error)
            {
                _notifier.ShowError(msg, opts);
            }
            else if (type == NotificationType.Success)
            {
                _notifier.ShowSuccess(msg, opts);
            }
            else if (type == NotificationType.Warning)
            {
                _notifier.ShowWarning(msg, opts);
            }
            else
            {
                _notifier.ShowInformation(msg, opts);
            }

        }
    }
}
