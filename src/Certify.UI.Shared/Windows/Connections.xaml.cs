using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Certify.Shared;
using Certify.UI.ViewModel;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for Connections.xaml
    /// </summary>
    public partial class Connections
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public Connections()
        {
            InitializeComponent();

            this.RefreshConnections();
            this.DataContext = AppViewModel.Current;
        }

        private void RefreshConnections()
        {
            var list = ViewModel.AppViewModel.Current.GetServerConnections();
            ConnectionList.ItemsSource = list;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {

            Close();
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            var selectedConnection = (sender as Button).DataContext as ServerConnection;

            await ViewModel.AppViewModel.Current.ConnectToServer(selectedConnection, _cts.Token);

            Close();
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // cancel pending operations (if any)
            _cts.Cancel();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();

        }

        private void AddConnection_Click(object sender, RoutedEventArgs e)
        {
            var d = new Windows.EditConnectionDialog { Owner = Window.GetWindow(this) };
            d.ShowDialog();
            RefreshConnections();
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            var editItem = (sender as Button).DataContext as ServerConnection;
            var d = new Windows.EditConnectionDialog(editItem) { Owner = Window.GetWindow(this) };
            d.ShowDialog();

            RefreshConnections();
        }
    }
}
