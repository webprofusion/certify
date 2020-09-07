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
using PropertyChanged;

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

            public bool SettingsInitialised { get; set; } = false;


        }
        public Model EditModel { get; set; } = new Model();

        public CertificateAuthorities()
        {
            InitializeComponent();
            this.DataContext = EditModel;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => LoadSettings();

        public void LoadSettings()
        {
            if (!EditModel.MainViewModel.IsServiceAvailable)
            {
                return;
            }

            EditModel.SettingsInitialised = false;

            EditModel.MainViewModel.RefreshCertificateAuthorityList();

            this.AccountList.ItemsSource = EditModel.MainViewModel.AccountDetails;

            EnableAutomaticCAFailover.IsChecked = EditModel.Prefs.EnableAutomaticCAFailover;

            this.CertificateAuthorityList.ItemsSource = EditModel.MainViewModel.CertificateAuthorities.Where(c => c.IsEnabled == true);

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
                    await EditModel.MainViewModel.RemoveAccount(account.StorageKey ?? account.ID);
                }
            }
        }
    }
}
