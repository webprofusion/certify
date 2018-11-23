using System;
using System.Windows;
using System.Windows.Input;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for AddToDashboard.xaml 
    /// </summary>
    public partial class AddToDashboard
    {
        public AddToDashboard()
        {
            InitializeComponent();
        }

        private async void ValidateKey_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailAddress.Text?.Trim().ToLower();
            var pwd = Password.Password.Trim();

            if (String.IsNullOrEmpty(email))
            {
                MessageBox.Show(Certify.Locales.SR.Registration_NeedEmail);
                return;
            }

            if (String.IsNullOrEmpty(pwd))
            {
                // MessageBox.Show(Certify.Locales.SR.Registration_NeedKey);
                return;
            }

            ValidateKey.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            var dashboardClient = ViewModel.AppViewModel.Current.PluginManager?.DashboardClient;

            if (dashboardClient != null)
            {
                try
                {
                    var instance = new Models.Shared.RegisteredInstance
                    {
                        InstanceId = ViewModel.AppViewModel.Current.Preferences.InstanceId,
                        AppVersion = Management.Util.GetAppVersion().ToString(),
                        OS = Environment.OSVersion.ToString(),
                        MachineName = Environment.MachineName
                    };

                    var resultOK = await dashboardClient.RegisterInstance(instance, email, pwd, (bool)CreateNewAccount.IsChecked);
                    Mouse.OverrideCursor = Cursors.Arrow;

                    if (resultOK)
                    {
                        await ViewModel.AppViewModel.Current.SetInstanceRegistered();
                        MessageBox.Show("Server registration completed.");
                        this.Close();
                    }
                    else
                    {
                        MessageBox.Show("Server registration could not complete.");
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show(Certify.Locales.SR.Registration_KeyValidationError);
                }
            }
            else
            {
                MessageBox.Show(Certify.Locales.SR.Registration_UnableToVerify);
            }

            ValidateKey.IsEnabled = true;
            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
