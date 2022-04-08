using System;
using System.Windows;
using Certify.Models.Providers;
using ControlzEx.Theming;
using ToastNotifications;
using ToastNotifications.Lifetime;
using ToastNotifications.Messages;
using ToastNotifications.Position;

namespace Certify.UI.Shared
{
    public enum NotificationType
    {
        Info = 1,
        Success = 2,
        Error = 3,
        Warning = 4
    }
    public enum PrimaryUITabs
    {
        ManagedCertificates = 0,
        CurrentProgress = 1
    }

    public interface ICertifyApp
    {
        string ToggleTheme(string initialTheme = null);
        void ChangeCulture(string culture, bool reopenWindow = true);
        void ShowNotification(string msg, NotificationType type = NotificationType.Info, bool autoClose = true);
    }

    public static class AppHelper
    {
        public static string ToggleTheme(System.Windows.Application application, Certify.UI.ViewModel.AppViewModel mainViewModel, string initialTheme = null)
        {

            if (initialTheme != null)
            {
                if (initialTheme == "Dark")
                {
                    ThemeManager.Current.ChangeTheme(application, "Dark.Green");
                }
                else
                {
                    ThemeManager.Current.ChangeTheme(application, "Light.Green");
                }

                // refresh bindings to force dynamic resources to redraw
                mainViewModel.RaisePropertyChangedEvent(nameof(mainViewModel.ManagedCertificates));
                return initialTheme;
            }
            else
            {
                var theme = ThemeManager.Current.DetectTheme();
                string themeSelection;

                if (theme.BaseColorScheme == "Light")
                {
                    ThemeManager.Current.ChangeTheme(application, "Dark.Green");
                    themeSelection = "Dark";
                }
                else
                {
                    ThemeManager.Current.ChangeTheme(application, "Light.Green");
                    themeSelection = "Light";
                }

                mainViewModel.RaisePropertyChangedEvent(nameof(mainViewModel.ManagedCertificates));
                return themeSelection;
            }
        }

        public static Notifier Startup(ILog log, ViewModel.AppViewModel mainViewModel, StartupEventArgs e)
        {
            log?.Information("UI Startup");

            mainViewModel.UISettings = Settings.UISettings.Load();

            // Apply translations if required
            if (mainViewModel.UISettings != null)
            {
                ChangeCulture(mainViewModel.UISettings.PreferredUICulture, false);
            }

            // setup notifications toast handler
            return new Notifier(cfg =>
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

        public static void ChangeCulture(string culture, bool reopenWindow = true)
        {
            if (System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("en-") && (culture == null || culture.StartsWith("en-")))
            {
                // English already, nothing to do
                return;
            }

            var cultureInfo = new System.Globalization.CultureInfo(culture);

            //System.Threading.Thread.CurrentThread.CurrentCulture = cultureInfo;
            System.Threading.Thread.CurrentThread.CurrentUICulture = cultureInfo;

            if (reopenWindow)
            {
                MessageBox.Show("To apply language settings, close and re-open the application.");
            }
        }

        public static void CurrentDomain_UnhandledException(ILog log, object sender, UnhandledExceptionEventArgs e)
        {

            var feedbackMsg = "";
            if (e.ExceptionObject != null)
            {
                feedbackMsg = "An error occurred: " + ((Exception)e.ExceptionObject).ToString();

                log?.Error(feedbackMsg);
            }

            var d = new Windows.Feedback(feedbackMsg, isException: true);

            d.ShowDialog();
        }

        public static void ShowNotification(Certify.UI.ViewModel.AppViewModel mainViewModel, Notifier notifier, string msg, NotificationType type = NotificationType.Info, bool autoClose = true)
        {
            var opts = new ToastNotifications.Core.MessageOptions { ShowCloseButton = false };

            opts.FontSize = 12 * mainViewModel.UIScaleFactor;

            if (type == NotificationType.Error)
            {
                notifier.ShowError(msg, opts);
            }
            else if (type == NotificationType.Success)
            {
                notifier.ShowSuccess(msg, opts);
            }
            else if (type == NotificationType.Warning)
            {
                notifier.ShowWarning(msg, opts);
            }
            else
            {
                notifier.ShowInformation(msg, opts);
            }
        }
    }
}
