using System.Windows;
using System.Windows.Controls;
using Certify.Models;

namespace Certify.UI.Controls.Settings
{
    /// <summary>
    /// Interaction logic for ManagementHub.xaml
    /// </summary>
    public partial class ManagementHub : UserControl
    {
        public class EditViewModel : BindableBase
        {
            public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

            public string ManagementHubAPIUrl { get; set; } = string.Empty;
            public string ClientID { get; set; }
            public string ClientSecret { get; set; }

            public bool IsConnected { get; set; }
            public string StatusMessage { get; set; } = string.Empty;
        }

        public EditViewModel EditModel { get; set; } = new EditViewModel();

        public ManagementHub()
        {
            InitializeComponent();
            DataContext = EditModel;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!EditModel.MainViewModel.IsServiceAvailable)
            {
                return;
            }

            // load settings and hub connection status
            var config = EditModel.MainViewModel.GetAppServiceConfig();

            EditModel.ManagementHubAPIUrl = config.ManagementServerHubAPI;

            var status = await EditModel.MainViewModel.CheckManagementHubConnectionStatus();

            EditModel.IsConnected = status.IsSuccess;
            EditModel.StatusMessage = status.Message;
        }

        private async void Join_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var result = await EditModel.MainViewModel.JoinManagementHub(EditModel.ManagementHubAPIUrl, EditModel.ClientID, EditModel.ClientSecret);

            EditModel.IsConnected = result.IsSuccess;
            EditModel.StatusMessage = result.Message;

            if (result.IsSuccess)
            {
                MessageBox.Show("Successfully joined Management Hub.");
            }
            else
            {
                MessageBox.Show(result.Message);
            }
        }
    }
}
