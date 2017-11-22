using Certify.Management;
using System;
using System.Windows;
using System.Windows.Input;

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
                MessageBox.Show(Certify.Locales.SR.Registration_NeedEmail);
                return;
            }

            if (String.IsNullOrEmpty(key))
            {
                MessageBox.Show(Certify.Locales.SR.Registration_NeedKey);
                return;
            }

            ValidateKey.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            var licensingManager = ViewModel.AppModel.AppViewModel.PluginManager?.LicensingManager;

            if (licensingManager != null)
            {
                try
                {
                    var validationResult = await licensingManager.Validate(productTypeId, email, key);
                    if (validationResult.IsValid)
                    {
                        var instance = new Models.Shared.RegisteredInstance
                        {
                            InstanceId = ViewModel.AppModel.AppViewModel.Preferences.InstanceId,
                            AppVersion = new Management.Util().GetAppVersion().ToString()
                        };

                        var installRegistration = await licensingManager.RegisterInstall(productTypeId, email, key, instance);

                        Mouse.OverrideCursor = Cursors.Arrow;
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
                            ValidateKey.IsEnabled = true;
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