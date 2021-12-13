using System;
using System.Windows;
using System.Windows.Input;
using Certify.Models;
using Certify.Models.Utils;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for Registration.xaml 
    /// </summary>
    public partial class Registration
    {
        protected Models.Providers.ILog Log => ViewModel.AppViewModel.Current.Log;

        public class Model : BindableBase
        {
            public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;
            public bool IsRegistrationMode { get; set; } = true;
        }
        public Model EditModel { get; set; } = new Model();

        public Registration()
        {
            InitializeComponent();


            this.DataContext = EditModel;

            this.Width *= EditModel.MainViewModel.UIScaleFactor;
            this.Height *= EditModel.MainViewModel.UIScaleFactor;
        }

        private async void ValidateKey_Click(object sender, RoutedEventArgs e)
        {
            var productTypeId = ViewModel.AppViewModel.ProductTypeId;

            var email = EmailAddress.Text?.Trim().ToLower();
            var key = LicenseKey.Text?.Trim().ToLower();

            if (string.IsNullOrEmpty(email))
            {
                MessageBox.Show(Certify.Locales.SR.Registration_NeedEmail);
                return;
            }

            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show(Certify.Locales.SR.Registration_NeedKey);
                return;
            }

            ValidateKey.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            var licensingManager = ViewModel.AppViewModel.Current.PluginManager?.LicensingManager;

            if (licensingManager != null)
            {
                try
                {

                    var validationResult = await licensingManager.Validate(productTypeId, email, key);
                    if (validationResult.IsValid)
                    {
                        var instance = new Models.Shared.RegisteredInstance
                        {
                            InstanceId = ViewModel.AppViewModel.Current.Preferences.InstanceId,
                            AppVersion = Management.Util.GetAppVersion().ToString()
                        };

                        var installRegistration = await licensingManager.RegisterInstall(productTypeId, email, key, instance);

                        Mouse.OverrideCursor = Cursors.Arrow;
                        if (installRegistration.IsSuccess)
                        {
                            var settingsPath = EnvironmentUtil.GetAppDataFolder();
                            if (licensingManager.FinaliseInstall(productTypeId, installRegistration, settingsPath))
                            {
                                ViewModel.AppViewModel.Current.IsRegisteredVersion = true;
                                MessageBox.Show(installRegistration.Message);

                                Close();
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
                catch (System.Net.Http.HttpRequestException exp)
                {
                    Log?.Information("ValidateKey:" + exp.ToString());
                    MessageBox.Show("Communication with the Certify The Web API failed. Check your system can communicate with https://api.certifytheweb.com/v1/update using a web browser. \r\n\r\nIf your system is running an older version of Windows, check https://docs.certifytheweb.com for 'TLS Cipher', as updated registry settings may be required.");
                }
                catch (Exception exp)
                {

                    Log?.Information("ValidateKey:" + exp.ToString());

                    MessageBox.Show(Certify.Locales.SR.Registration_KeyValidationError);
                    MessageBox.Show(exp.ToString());
                }
            }
            else
            {
                MessageBox.Show("Could not load the licensing validation plugin. The app may need to be re-installed.");
            }

            ValidateKey.IsEnabled = true;
            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e) => Utils.Helpers.LaunchBrowser(e.Uri.ToString());

        private async void Deactivate_Click(object sender, RoutedEventArgs e)
        {
            var productTypeId = ViewModel.AppViewModel.ProductTypeId;

            var email = DeactivateEmail.Text?.Trim().ToLower();


            if (string.IsNullOrEmpty(email))
            {
                MessageBox.Show(Certify.Locales.SR.Registration_NeedEmail);
                return;
            }


            Mouse.OverrideCursor = Cursors.Wait;

            var licensingManager = ViewModel.AppViewModel.Current.PluginManager?.LicensingManager;

            if (licensingManager != null)
            {
                var instance = new Models.Shared.RegisteredInstance
                {
                    InstanceId = ViewModel.AppViewModel.Current.Preferences.InstanceId,
                    AppVersion = Management.Util.GetAppVersion().ToString()
                };
                var resultOK = await licensingManager.DeactivateInstall(productTypeId, EnvironmentUtil.GetAppDataFolder(), email, instance);

                Mouse.OverrideCursor = Cursors.Arrow;

                if (resultOK)
                {
                    ViewModel.AppViewModel.Current.IsRegisteredVersion = false;
                    MessageBox.Show("This install has now been deactivated. You can enter a different license key or use your key on another install.");
                    Close();
                }
                else
                {
                    MessageBox.Show("The install could not be deactivated, check specified email address is correct for account. You can manually delete the C:\\ProgramData\\Certify\\reg_1 file and deactivate your install on https://certifytheweb.com");

                }
            }

            Mouse.OverrideCursor = Cursors.Arrow;
        }
    }
}
