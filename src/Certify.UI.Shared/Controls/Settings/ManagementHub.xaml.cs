using System;
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
            if (!TryGetValidatedJoinSettings(out var managementHubApiUrl, out var clientId, out var clientSecret, out var validationMessage))
            {
                EditModel.IsConnected = false;
                EditModel.StatusMessage = validationMessage;
                MessageBox.Show(validationMessage, "Management Hub Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            EditModel.ManagementHubAPIUrl = managementHubApiUrl;
            EditModel.ClientID = clientId;
            EditModel.ClientSecret = clientSecret;

            var checkResult = await EditModel.MainViewModel.CheckManagementHubCredentials(managementHubApiUrl, clientId, clientSecret);
            if (!checkResult.IsSuccess)
            {
                EditModel.IsConnected = false;
                EditModel.StatusMessage = checkResult.Message;
                MessageBox.Show(checkResult.Message, "Management Hub Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = await EditModel.MainViewModel.JoinManagementHub(managementHubApiUrl, clientId, clientSecret);

            EditModel.IsConnected = result.IsSuccess;
            EditModel.StatusMessage = result.Message;

            if (result.IsSuccess)
            {
                if (!string.IsNullOrWhiteSpace(result.Message) && result.Message.Contains("instance already known to hub", System.StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Successfully re-joined Management Hub. This instance was already known to the hub. If this host was cloned, verify the hub-assigned identity is intentional.");
                }
                else
                {
                    MessageBox.Show("Successfully joined Management Hub.");
                }
            }
            else
            {
                MessageBox.Show(result.Message);
            }
        }

        private bool TryGetValidatedJoinSettings(out string managementHubApiUrl, out string clientId, out string clientSecret, out string validationMessage)
        {
            managementHubApiUrl = EditModel.ManagementHubAPIUrl?.Trim() ?? string.Empty;
            clientId = EditModel.ClientID?.Trim() ?? string.Empty;
            clientSecret = EditModel.ClientSecret?.Trim() ?? string.Empty;
            validationMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(managementHubApiUrl))
            {
                validationMessage = "Management Hub API URL is required.";
                return false;
            }

            if (!Uri.TryCreate(managementHubApiUrl, UriKind.Absolute, out var hubUri))
            {
                validationMessage = "Management Hub API URL must be a valid absolute URL, for example https://hub.example.com.";
                return false;
            }

            if (hubUri.Scheme != Uri.UriSchemeHttp && hubUri.Scheme != Uri.UriSchemeHttps)
            {
                validationMessage = "Management Hub API URL must use http or https.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(hubUri.Query) || !string.IsNullOrWhiteSpace(hubUri.Fragment))
            {
                validationMessage = "Management Hub API URL must not include a query string or fragment.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                validationMessage = "Client ID is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                validationMessage = "Client Secret is required.";
                return false;
            }

            managementHubApiUrl = hubUri.GetLeftPart(UriPartial.Path).TrimEnd('/');

            return true;
        }
    }
}
