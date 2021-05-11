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

        protected Certify.UI.ViewModel.AppViewModel MainViewModel => UI.ViewModel.AppViewModel.Current;

        protected Models.Providers.ILog Log => MainViewModel.Log;

        public string ToggleTheme(string initialTheme = null) => AppHelper.ToggleTheme(Application.Current, MainViewModel, initialTheme);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += CurrentDomain_UnhandledException;

            AppHelper.Startup(Log, _notifier, MainViewModel, e);
        }

        public void ChangeCulture(string culture, bool reopenWindow = true) => AppHelper.ChangeCulture(culture, reopenWindow);

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) => AppHelper.CurrentDomain_UnhandledException(Log, sender, e);

        public void ShowNotification(string msg, NotificationType type = NotificationType.Info, bool autoClose = true) => AppHelper.ShowNotification(MainViewModel, _notifier, msg, type, autoClose);
    }
}
