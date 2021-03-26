using System;
using System.Windows;
using Certify.UI.Shared;
using ControlzEx.Theming;
using ToastNotifications;
using ToastNotifications.Lifetime;
using ToastNotifications.Messages;
using ToastNotifications.Position;

namespace Certify.UI
{
    /// <summary>
    /// Interaction logic for App.xaml 
    /// </summary>
    public partial class App : Application, Certify.UI.Shared.ICertifyApp
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

            if (initialTheme != null)
            {
                if (initialTheme == "Dark")
                {
                    ThemeManager.Current.ChangeTheme(Application.Current, "Dark.Green");
                }
                else
                {
                    ThemeManager.Current.ChangeTheme(Application.Current, "Light.Green");
                }

                // refresh bindings to force dynamic resources to redraw
                MainViewModel.RaisePropertyChangedEvent(nameof(MainViewModel.ManagedCertificates));
                return initialTheme;
            }
            else
            {
                var theme = ThemeManager.Current.DetectTheme();
                string themeSelection;

                if (theme.BaseColorScheme == "Light")
                {
                    ThemeManager.Current.ChangeTheme(Application.Current, "Dark.Green");
                    themeSelection = "Dark";
                }
                else
                {
                    ThemeManager.Current.ChangeTheme(Application.Current, "Light.Green");
                    themeSelection = "Light";
                }

                MainViewModel.RaisePropertyChangedEvent(nameof(MainViewModel.ManagedCertificates));
                return themeSelection;
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


        public void ShowNotification(string msg, NotificationType type = NotificationType.Info, bool autoClose = true)
        {
            var opts = new ToastNotifications.Core.MessageOptions { ShowCloseButton = false };

            opts.FontSize = 12 * MainViewModel.UIScaleFactor;

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
