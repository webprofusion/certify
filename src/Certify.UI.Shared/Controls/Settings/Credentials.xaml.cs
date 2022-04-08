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
    public partial class Credentials : UserControl
    {
        public class EditViewModel : BindableBase
        {
            public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

            public Models.Config.StoredCredential SelectedStoredCredential { get; set; }
        }

        public EditViewModel EditModel { get; set; } = new EditViewModel();

        public Credentials()
        {
            InitializeComponent();
            DataContext = EditModel;

        }

        private async Task LoadCurrentSettings()
        {
            if (!EditModel.MainViewModel.IsServiceAvailable)
            {
                return;
            }

            DataContext = EditModel;

            //load stored credentials list
            await EditModel.MainViewModel.RefreshStoredCredentialsList();
            CredentialsList.ItemsSource = FilteredStoredCredentials;
        }
        private IEnumerable<StoredCredential> FilteredStoredCredentials => EditModel.MainViewModel.StoredCredentials.Where(c => c.ProviderType != StandardAuthTypes.STANDARD_ACME_ACCOUNT);
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
            if (EditModel.SelectedStoredCredential != null)
            {
                var d = new Windows.EditCredential(EditModel.SelectedStoredCredential)
                {
                    Owner = Window.GetWindow(this)
                };

                d.ShowDialog();

                UpdateDisplayedCredentialsList();
            }
        }

        private void UpdateDisplayedCredentialsList() => EditModel.MainViewModel.GetApplication().Dispatcher.Invoke(delegate
                                                       {
                                                           CredentialsList.ItemsSource = FilteredStoredCredentials;
                                                       });

        private async void DeleteStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            //delete the selected credential, if not currently in use
            if (EditModel.SelectedStoredCredential != null)
            {
                if (MessageBox.Show("Are you sure you wish to delete this stored credential?", "Confirm Delete", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    //confirm item not used then delete
                    var deleted = await EditModel.MainViewModel.DeleteCredential(EditModel.SelectedStoredCredential?.StorageKey);
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
            if (EditModel.SelectedStoredCredential != null)
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var result = await EditModel.MainViewModel.TestCredentials(EditModel.SelectedStoredCredential.StorageKey);

                Mouse.OverrideCursor = Cursors.Arrow;

                EditModel.MainViewModel.ShowNotification(result.Message);

            }
        }

        private void CredentialsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                EditModel.SelectedStoredCredential = (Models.Config.StoredCredential)e.AddedItems[0];
            }
            else
            {
                EditModel.SelectedStoredCredential = null;
            }
        }
    }
}
