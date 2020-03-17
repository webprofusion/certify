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
using Certify.Models;

namespace Certify.UI.Controls.Settings
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class CertificateAuthorities : UserControl
    {
        public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        private bool _settingsInitialised = false;
        private Models.Preferences _prefs => MainViewModel.Preferences;
          
        public CertificateAuthorities()
        {
            InitializeComponent();
            this.DataContext = MainViewModel;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) =>  LoadSettings();

        public void LoadSettings()
        {
            if (!MainViewModel.IsServiceAvailable)
            {
                return;
            }

            this.AccountList.ItemsSource = MainViewModel.AccountDetails;

            EnableAutomaticCAFailover.IsChecked = _prefs.EnableAutomaticCAFailover;

            this.CertificateAuthorityList.ItemsSource = CertificateAuthority.CertificateAuthorities.Where(c => c.IsEnabled == true);

            CertificateAuthorityList.SelectedValue = _prefs.DefaultCertificateAuthority;

            _settingsInitialised = true;
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

        private async void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            if (_settingsInitialised)
            {
                _prefs.EnableAutomaticCAFailover = (EnableAutomaticCAFailover.IsChecked == true);

                _prefs.DefaultCertificateAuthority = CertificateAuthorityList.SelectedValue.ToString();

                await MainViewModel.SavePreferences();
            }
        }

        private void CertificateAuthorityList_SelectionChanged(object sender, SelectionChangedEventArgs e) => SettingsUpdated(sender, e);

        private async void Button_Delete(object sender, RoutedEventArgs e)
        {
            if (sender!=null)
            {
                var button = sender as Button;
                var account = button.DataContext as AccountDetails;

                if (MessageBox.Show($"Remove this account? {account.AccountURI}", "Confirm Account Removal", MessageBoxButton.YesNoCancel)== MessageBoxResult.Yes)
                {
                    await MainViewModel.RemoveAccount(account.StorageKey??account.ID);
                }    
            }
        }
    }
}
