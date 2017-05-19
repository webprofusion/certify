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
using System.Windows.Shapes;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for Registration.xaml
    /// </summary>
    public partial class Registration
    {
        public Registration()
        {
            InitializeComponent();
        }

        private async void ValidateKey_Click(object sender, RoutedEventArgs e)
        {
            var productTypeId = ViewModel.AppModel.ProductTypeId;

            var email = EmailAddress.Text?.Trim().ToLower();
            var key = LicenseKey.Text?.Trim().ToLower();

            if (String.IsNullOrEmpty(email))
            {
                MessageBox.Show("Please enter the email address you used when registering your key.");
                return;
            }

            if (String.IsNullOrEmpty(key))
            {
                MessageBox.Show("Please enter your license key.");
                return;
            }

            var pluginManager = new PluginManager();
            pluginManager.LoadPlugins();

            if (pluginManager.LicensingManager != null)
            {
                var licensingManager = pluginManager.LicensingManager;

                try
                {
                    var validationResult = await licensingManager.Validate(productTypeId, email, key);
                    if (validationResult.IsValid)
                    {
                        var installRegistration = await licensingManager.RegisterInstall(productTypeId, email, key, System.Environment.MachineName);

                        if (installRegistration.IsSuccess)
                        {
                            var settingsPath = Util.GetAppDataFolder();
                            if (licensingManager.FinaliseInstall(productTypeId, installRegistration, settingsPath))
                            {
                                ViewModel.AppModel.AppViewModel.IsRegisteredVersion = true;
                                MessageBox.Show(installRegistration.Message);
                                this.Close();
                            }
                        }
                        else
                        {
                            MessageBox.Show(installRegistration.Message);
                        }
                    }
                    else
                    {
                        MessageBox.Show(validationResult.ValidationMessage);
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show("There was a problem trying to validate your license key. Please try again or contact support.");
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}