using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Certify.Locales;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for AboutControl.xaml 
    /// </summary>
    public partial class AboutControl : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        public AboutControl()
        {
            InitializeComponent();
        }

        private void PopulateAppInfo()
        {
            if (MainViewModel.IsServiceAvailable)
            {
                ServiceConnected.Foreground = System.Windows.Media.Brushes.DarkGreen;
                ServiceConnected.Icon = FontAwesome.WPF.FontAwesomeIcon.Chain;
            }
            else
            {
                ServiceConnected.Foreground = System.Windows.Media.Brushes.Red;
                ServiceConnected.Icon = FontAwesome.WPF.FontAwesomeIcon.ChainBroken;
            }
            lblAppVersion.Text = ConfigResources.AppName + " " + Management.Util.GetAppVersion();

            if (MainViewModel.IsRegisteredVersion)
            {
                Register.IsEnabled = false;
                ValidateKey.IsEnabled = false;

                lblRegistrationType.Text = "Registered Version";
                lblRegistrationDetails.Text = "";
            }

            creditLibs.Text = "";

            // add details of current languages translator team
            if (!string.IsNullOrEmpty(SR.LanguageAuthor) && !creditLibs.Text.Contains(SR.About_LanguageTranslator))
            {
                creditLibs.Text = SR.About_LanguageTranslator + SR.LanguageAuthor + Environment.NewLine + Environment.NewLine;
            }

            if (System.IO.File.Exists("THIRD_PARTY_LICENSES.txt"))
            {
                creditLibs.Text += System.IO.File.ReadAllText("THIRD_PARTY_LICENSES.txt");
            }
        }

        private async void UpdateCheck_Click(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Wait;

            await PerformCheckForUpdates(silent: false);

            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private async Task PerformCheckForUpdates(bool silent)
        {
            var updateCheck = await new Management.Util().CheckForUpdates();

            if (updateCheck != null)
            {
                MainViewModel.UpdateCheckResult = updateCheck;
                if (updateCheck.IsNewerVersion)
                {
                    MainViewModel.IsUpdateAvailable = true;

                    var gotoDownload = MessageBox.Show(updateCheck.Message.Body + "\r\nVisit download page now?", ConfigResources.AppName, MessageBoxButton.YesNo);
                    if (gotoDownload == MessageBoxResult.Yes)
                    {
                        var sInfo = new System.Diagnostics.ProcessStartInfo(ConfigResources.AppWebsiteURL);
                        System.Diagnostics.Process.Start(sInfo);
                    }
                }
                else
                {
                    if (!silent)
                    {
                        (App.Current as App).ShowNotification(ConfigResources.UpdateCheckLatestVersion, App.NotificationType.Success);
                    }
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e) => System.Diagnostics.Process.Start("https://certifytheweb.com/register?src=app");

        private void Button_ApplyRegistrationKey(object sender, RoutedEventArgs e)
        {
            var d = new Windows.Registration { Owner = Window.GetWindow(this) };
            d.ShowDialog();

            d.Unloaded += ApplyRegistration_Completed;
        }

        private void ApplyRegistration_Completed(object sender, EventArgs e) =>
            //refresh registration status TODO: main window title
            PopulateAppInfo();

        private void Help_Click(object sender, RoutedEventArgs e) => System.Diagnostics.Process.Start("https://certifytheweb.com");

        private void Feedback_Click(object sender, RoutedEventArgs e)
        {
            var d = new Windows.Feedback("", false) { Owner = Window.GetWindow(this) };
            d.ShowDialog();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => PopulateAppInfo();
    }
}
