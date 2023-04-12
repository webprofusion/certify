using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Certify.Models;
using static System.Net.Mime.MediaTypeNames;

namespace Certify.UI.Controls.Settings
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class CertificateAuthorities : UserControl
    {
        public class Model : BindableBase
        {
            public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;
            public Models.Preferences Prefs => MainViewModel.Preferences;

            public bool SettingsInitialised { get; set; }
        }
        public Model EditModel { get; set; } = new Model();

        public CertificateAuthorities()
        {
            InitializeComponent();
            DataContext = EditModel;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => LoadSettings();

        public void LoadSettings()
        {
            if (!EditModel.MainViewModel.IsServiceAvailable)
            {
                return;
            }

            EditModel.SettingsInitialised = false;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            EditModel.MainViewModel.RefreshCertificateAuthorityList();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            AccountList.ItemsSource = EditModel.MainViewModel.AccountDetails;

            EnableAutomaticCAFailover.IsChecked = EditModel.Prefs.EnableAutomaticCAFailover;

            CertificateAuthorityList.ItemsSource = EditModel.MainViewModel.CertificateAuthorities.Where(c => c.IsEnabled == true);

            CertificateAuthorityList.SelectedValue = EditModel.Prefs.DefaultCertificateAuthority;

            EditModel.SettingsInitialised = true;

            EditModel.RaisePropertyChangedEvent(null);
        }

        private void Button_NewContact(object sender, RoutedEventArgs e)
        {
            //present new contact dialog
            var d = new Windows.EditAccountDialog
            {
                Owner = Window.GetWindow(this)
            };
            d.ShowDialog();
        }

        private void Button_EditCertificateAuthority(object sender, RoutedEventArgs e)
        {
            //present new contact dialog
            var d = new Windows.EditCertificateAuthority
            {
                Owner = Window.GetWindow(this)
            };

            d.Closed += (object s, EventArgs arg) =>
            {

                LoadSettings();
            };

            d.ShowDialog();
        }

        private async void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            if (EditModel.SettingsInitialised)
            {
                EditModel.Prefs.EnableAutomaticCAFailover = (EnableAutomaticCAFailover.IsChecked == true);

                EditModel.Prefs.DefaultCertificateAuthority = CertificateAuthorityList.SelectedValue?.ToString() ?? EditModel.Prefs.DefaultCertificateAuthority;

                await EditModel.MainViewModel.SavePreferences();
            }
        }

        private void CertificateAuthorityList_SelectionChanged(object sender, SelectionChangedEventArgs e) => SettingsUpdated(sender, e);

        private async void Button_Delete(object sender, RoutedEventArgs e)
        {
            if (sender != null)
            {
                var button = sender as Button;
                var account = button.DataContext as AccountDetails;

                if (MessageBox.Show($"Remove this account? {account.AccountURI}", "Confirm Account Removal", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    await EditModel.MainViewModel.RemoveAccount(string.IsNullOrEmpty(account.StorageKey) ? account.ID : account.StorageKey);
                }
            }
        }

        private void Button_Edit(object sender, RoutedEventArgs e)
        {
            if (sender != null)
            {
                var button = sender as Button;
                var account = button.DataContext as AccountDetails;

                //present edit contact dialog
                var d = new Windows.EditAccountDialog(new ContactRegistration
                {
                    StorageKey = account.StorageKey,
                    AgreedToTermsAndConditions = true,
                    CertificateAuthorityId = account.CertificateAuthorityId,
                    EmailAddress = account.Email,
                    IsStaging = account.IsStagingAccount,
                    PreferredChain = account.PreferredChain,
                    EabKey = account.EabKey,
                    EabKeyAlgorithm = account.EabKeyAlgorithm,
                    EabKeyId = account.EabKeyId
                })
                {
                    Owner = Window.GetWindow(this)
                };
                d.ShowDialog();
            }
        }

        private async void AccountList_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

            // copy account to clipboard
            if (sender != null)
            {
                var item = (sender as StackPanel)?.DataContext as AccountDetails;
                if (item != null)
                {
                    var text = $"Account URI: {item.AccountURI}\r\n" +
                        $"Account Fingerprint: {item.AccountFingerprint}\r\n" +
                        $"Account Key: \r\n{item.AccountKey}";

                    var copiedOK = await WaitForClipboard(text);

                    if (copiedOK)
                    {
                        ViewModel.AppViewModel.Current.ShowNotification("Account details have been copied to the clipboard.");
                    }
                }
            }
        }

        private async Task<bool> WaitForClipboard(string text)
        {
            // if running under terminal services etc the clipboard can take multiple attempts to set
            // https://stackoverflow.com/questions/68666/clipbrd-e-cant-open-error-when-setting-the-clipboard-from-net
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    Clipboard.SetText(text);

                    return true;
                }
                catch { }

                await Task.Delay(50);
            }

            return false;
        }
    }
}
