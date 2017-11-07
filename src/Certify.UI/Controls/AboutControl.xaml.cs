using Certify.Locales;
using Certify.Management;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for AboutControl.xaml 
    /// </summary>
    public partial class AboutControl : UserControl
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return ViewModel.AppModel.AppViewModel;
            }
        }

        public AboutControl()
        {
            InitializeComponent();

            PopulateAppInfo();
        }

        private void PopulateAppInfo()
        {
            this.lblAppVersion.Text = ConfigResources.AppName + " " + new Certify.Management.Util().GetAppVersion();

            if (this.MainViewModel.IsRegisteredVersion)
            {
                this.Register.IsEnabled = false;
                this.ValidateKey.IsEnabled = false;

                this.lblRegistrationType.Text = "Registered Version";
                this.lblRegistrationDetails.Text = "";
            }

            if (!string.IsNullOrEmpty(SR.LanguageAuthor))
            {
                this.creditLibs.Text += Environment.NewLine + SR.About_LanguageTranslator + SR.LanguageAuthor;
            }
        }

        private async void UpdateCheck_Click(object sender, RoutedEventArgs e)
        {
            await PerformCheckForUpdates(silent: false);
        }

        private async Task PerformCheckForUpdates(bool silent)
        {
            var updateCheck = await new Util().CheckForUpdates();

            if (updateCheck != null)
            {
                if (updateCheck.IsNewerVersion)
                {
                    var gotoDownload = MessageBox.Show(updateCheck.Message.Body + "\r\nVisit download page now?", ConfigResources.AppName, MessageBoxButton.YesNo);
                    if (gotoDownload == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.ProcessStartInfo sInfo = new System.Diagnostics.ProcessStartInfo(ConfigResources.AppWebsiteURL);
                        System.Diagnostics.Process.Start(sInfo);
                    }
                }
                else
                {
                    if (!silent)
                    {
                        MessageBox.Show(ConfigResources.UpdateCheckLatestVersion, ConfigResources.AppName);
                    }
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://certifytheweb.com/register?src=app");
        }

        private void Button_ApplyRegistrationKey(object sender, RoutedEventArgs e)
        {
            var d = new Windows.Registration { Owner = Window.GetWindow(this) };
            d.ShowDialog();

            d.Unloaded += ApplyRegistration_Completed;
        }

        private void ApplyRegistration_Completed(object sender, EventArgs e)
        {
            //refresh registration status TODO: main window title
            PopulateAppInfo();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://certifytheweb.com");
        }

        private void Feedback_Click(object sender, RoutedEventArgs e)
        {
            var d = new Windows.Feedback("", false);
            d.ShowDialog();
        }
    }
}