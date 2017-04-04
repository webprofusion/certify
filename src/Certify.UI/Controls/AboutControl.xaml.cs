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
        public AboutControl()
        {
            InitializeComponent();

            PopulateAppInfo();
        }

        private void PopulateAppInfo()
        {
            this.lblAppVersion.Text = Core.Properties.Resources.AppName + " " + GetAppVersion();
        }

        private Version GetAppVersion()
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var v = assembly.GetName().Version;
            return v;
        }

        private async void UpdateCheck_Click(object sender, RoutedEventArgs e)
        {
            PerformCheckForUpdates(silent: false);
        }

        private async void PerformCheckForUpdates(bool silent)
        {
            var v = GetAppVersion();
            var updateCheck = await new Util().CheckForUpdates(v);

            if (updateCheck != null)
            {
                if (updateCheck.IsNewerVersion)
                {
                    var gotoDownload = MessageBox.Show(updateCheck.Message.Body + "\r\nVisit download page now?", Core.Properties.Resources.AppName, MessageBoxButton.YesNo);
                    if (gotoDownload == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.ProcessStartInfo sInfo = new System.Diagnostics.ProcessStartInfo(Core.Properties.Resources.AppWebsiteURL);
                        System.Diagnostics.Process.Start(sInfo);
                    }
                }
                else
                {
                    if (!silent)
                    {
                        MessageBox.Show(Core.Properties.Resources.UpdateCheckLatestVersion, Core.Properties.Resources.AppName);
                    }
                }
            }
        }
    }
}