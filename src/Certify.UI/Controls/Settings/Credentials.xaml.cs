using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.UI.Controls.Settings
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Credentials : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        public Models.Config.StoredCredential _selectedStoredCredential = null;

        public Credentials()
        {
            InitializeComponent();
            this.DataContext = MainViewModel;

        }

        private async Task LoadCurrentSettings()
        {
            if (!MainViewModel.IsServiceAvailable)
            {
                return;
            }

            DataContext = MainViewModel;

            //load stored credentials list
            await MainViewModel.RefreshStoredCredentialsList();
            CredentialsList.ItemsSource = FilteredStoredCredentials;
        }
        private IEnumerable<StoredCredential> FilteredStoredCredentials => MainViewModel.StoredCredentials.Where(c => c.ProviderType != StandardAuthTypes.STANDARD_ACME_ACCOUNT);
        private async void UserControl_Loaded(object sender, RoutedEventArgs e) => await LoadCurrentSettings();

        private void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            var cred = new Windows.EditCredential
            {
                Owner = Window.GetWindow(this)
            };

            cred.ShowDialog();

            UpdateDisplayedCredentialsList();
        }

        private void ModifyStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            //modify the selected credential
            if (_selectedStoredCredential != null)
            {
                var d = new Windows.EditCredential(_selectedStoredCredential)
                {
                    Owner = Window.GetWindow(this)
                };

                d.ShowDialog();

                UpdateDisplayedCredentialsList();
            }
        }

        private void UpdateDisplayedCredentialsList() => App.Current.Dispatcher.Invoke((Action)delegate
                                                       {
                                                           CredentialsList.ItemsSource = FilteredStoredCredentials;
                                                       });

        private async void DeleteStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            //delete the selected credential, if not currently in use
            if (_selectedStoredCredential != null)
            {
                if (MessageBox.Show("Are you sure you wish to delete this stored credential?", "Confirm Delete", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    //confirm item not used then delete
                    var deleted = await MainViewModel.DeleteCredential(_selectedStoredCredential?.StorageKey);
                    if (!deleted)
                    {
                        MessageBox.Show("This stored credential could not be removed. It may still be in use by a managed site.");
                    }
                }

                UpdateDisplayedCredentialsList();
            }
        }

        private async void TestStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStoredCredential != null)
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var result = await MainViewModel.TestCredentials(_selectedStoredCredential.StorageKey);

                Mouse.OverrideCursor = Cursors.Arrow;


                (App.Current as App).ShowNotification(result.Message);

            }
        }

        private void CredentialsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                _selectedStoredCredential = (Models.Config.StoredCredential)e.AddedItems[0];
            }
            else
            {
                _selectedStoredCredential = null;
            }
        }

    }
}
