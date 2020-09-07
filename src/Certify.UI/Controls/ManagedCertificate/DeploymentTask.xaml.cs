using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.UI.Controls.ManagedCertificate
{
    public class DebugDataBindingConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            Debugger.Break();
            return value;
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            Debugger.Break();
            return value;
        }
    }

    /// <summary>
    /// Interaction logic for DeploymentTask.xaml
    /// </summary>
    public partial class DeploymentTask : UserControl
    {
        protected ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        protected ViewModel.DeploymentTaskConfigViewModel EditModel;


        public DeploymentTask()
        {
            InitializeComponent();
        }

        public void SetEditItem(DeploymentTaskConfig config, bool editAsPostRequestTask)
        {

            EditModel = new ViewModel.DeploymentTaskConfigViewModel(config, editAsPostRequestTask);

            this.StoredCredentials.ItemsSource = EditModel.FilteredCredentials;

            DataContext = EditModel;

            var providers = EditModel.DeploymentProviders;

            Task.Run(async () => await RefreshEditModelOptions(resetDefaults: false));

        }

        private async Task RefreshEditModelOptions(bool resetDefaults = false)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await EditModel.RefreshOptions(resetDefaults: resetDefaults);
            });
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
        }

        private async void TaskTypeSelection_Click(object sender, RoutedEventArgs e)
        {
            // user has selected a task type from the list view of different task providers
            EditModel.SelectedItem.TaskTypeId = ((sender as Button).DataContext as DeploymentProviderDefinition).Id;

            await RefreshEditModelOptions();

            EditModel.RaisePropertyChangedEvent("SelectedItem");
        }

        private async void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            var cred = new Windows.EditCredential
            {
                Owner = Window.GetWindow(this)
            };

            cred.Item.ProviderType = EditModel.SelectedItem.ChallengeProvider;

            cred.ShowDialog();

            //refresh credentials list on complete

            await RefreshEditModelOptions();

            //await RefreshCredentialOptions();

            var credential = cred.Item;

            if (cred.Item != null && cred.Item.StorageKey != null)
            {
                // create a new challenge config based on new credentialsSelectedItem
                EditModel.SelectedItem.ChallengeProvider = credential.ProviderType;
                EditModel.SelectedItem.ChallengeCredentialKey = credential.StorageKey;
            }

        }

        private void ParameterInput_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            //EditModel.SelectedItem.IsChanged = true;
        }

        private void ShowParamLookup_Click(object sender, RoutedEventArgs e)
        {
            // EditModel.ShowZoneLookup = true;
        }

        private async void TaskProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TaskProviderList.SelectedValue != null)
            {
                await RefreshEditModelOptions(true);
            }
        }

        private async void TargetType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TargetType.SelectedValue != null)
            {
                await RefreshEditModelOptions();
            }
        }



        public async Task<bool> Save()
        {
            var result = await EditModel.Save();

            if (result.IsSuccess)
            {
                return true;
            }
            else
            {
                MessageBox.Show(result.Message, "Validation");
                return false;
            }
        }

        private void TaskName_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            EditModel.RaisePropertyChangedEvent("CLICommand");
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

        private async void DeferredInstructions1_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // copy command to clipboard
            if (sender != null)
            {
                var text = EditModel.CLICommand;
                var copiedOK = await WaitForClipboard(text);

                if (copiedOK)
                {
                    (App.Current as App).ShowNotification("Deployment Task command has been copied to the clipboard.");
                }
                else
                {
                    (App.Current as App).ShowNotification("Deployment Task command has been copied to the clipboard.", App.NotificationType.Warning);
                }
            }
        }
    }
}
