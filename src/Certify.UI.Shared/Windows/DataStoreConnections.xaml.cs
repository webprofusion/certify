using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Certify.Models;
using Certify.Shared;
using Certify.UI.ViewModel;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for DataStoreConnections.xaml
    /// </summary>
    public partial class DataStoreConnections
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public class EditModel : BindableBase
        {
            public string MigrationSourceId { get; set; }
            public string MigrationTargetId { get; set; }
            public bool IsLoading { get; set; }
            public DataStoreConnection DefaultStore { get; set; }
            public List<DataStoreConnection> Connections { get; set; }
        }

        private EditModel _model = new EditModel();
        public DataStoreConnections()
        {
            InitializeComponent();

            RefreshConnections();

            if (_model.Connections?.Count > 1)
            {
                _model.MigrationSourceId = _model.Connections[0].Id;
                _model.MigrationTargetId = _model.Connections[1].Id;
            }

            DataContext = _model;
        }

        private async void RefreshConnections()
        {
            var dataStores = await ViewModel.AppViewModel.Current.GetDataStoreConnections();
            _model.Connections = dataStores;

            if (string.IsNullOrEmpty(AppViewModel.Current.Preferences.ConfigDataStoreConnectionId))
            {
                _model.DefaultStore = _model.Connections.FirstOrDefault();
            }
            else
            {
                _model.DefaultStore = _model.Connections.FirstOrDefault(c => c.Id == AppViewModel.Current.Preferences.ConfigDataStoreConnectionId);
            }

            ConnectionList.ItemsSource = dataStores;
            SourceList.ItemsSource = dataStores;
            TargetList.ItemsSource = dataStores;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            Close();
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            var selectedConnection = (sender as Button).DataContext as DataStoreConnection;

            if (selectedConnection?.Id == AppViewModel.Current.Preferences.ConfigDataStoreConnectionId)
            {
                MessageBox.Show("This is the current data store connection.", "Connect to Data Store");
                return;
            }

            if (MessageBox.Show($"Switch primary data store for service to {selectedConnection.Title}?", "Switch Data Store", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
            {
                //TODO: perform data store connection and schema check before switching
                var results = await ViewModel.AppViewModel.Current.SetDefaultDataStore(selectedConnection.Id, _cts.Token);

                if (results.Any(r => r.HasError))
                {
                    var err = results.First(r => r.HasError);
                    MessageBox.Show(err.Description, err.Title);
                }
                else
                {
                    // refresh settings not that default data store has been updated
                    await ViewModel.AppViewModel.Current.LoadSettingsAsync();

                    Close();
                }
            }
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // cancel pending operations (if any)
            _cts.Cancel();
        }

        private async void CopyData_Click(object sender, RoutedEventArgs e)
        {
            var sourceId = this.SourceList.SelectedValue as string;
            var targetId = this.TargetList.SelectedValue as string;

            if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId))
            {
                AppViewModel.Current.ShowNotification("The source and target data source selections are required.", Shared.NotificationType.Error);
                return;
            }

            if (sourceId == targetId)
            {
                AppViewModel.Current.ShowNotification("The source and target data source cannot be the same.", Shared.NotificationType.Error);
                return;
            }

            var source = _model.Connections.FirstOrDefault(c => c.Id == sourceId);
            var target = _model.Connections.FirstOrDefault(c => c.Id == targetId);

            if (MessageBox.Show($"Copy data from {source?.Title} to {target?.Title}? Existing items in the target will be overwritten.", "Copy Data", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
            {
                _model.IsLoading = true;

                var results = await ViewModel.AppViewModel.Current.CopyDataStore(sourceId, targetId);

                string msg;
                if (results.Any(r => r.HasError))
                {
                    msg = string.Join("\n", results.Where(r => r.HasError).Select(t => t.Description));
                    AppViewModel.Current.ShowNotification(msg, Shared.NotificationType.Error);
                }
                else
                {
                    msg = string.Join("\n", results.Select(t => t.Description));
                    AppViewModel.Current.ShowNotification(msg, Shared.NotificationType.Info);
                }

                _model.IsLoading = false;
            }
        }

        private void AddConnection_Click(object sender, RoutedEventArgs e)
        {
            var d = new Windows.EditDataStoreConnectionDialog { Owner = Window.GetWindow(this) };
            d.ShowDialog();

            RefreshConnections();
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            var editItem = (sender as Button).DataContext as DataStoreConnection;
            var d = new Windows.EditDataStoreConnectionDialog(editItem) { Owner = Window.GetWindow(this) };
            d.ShowDialog();

            RefreshConnections();
        }
    }
}
