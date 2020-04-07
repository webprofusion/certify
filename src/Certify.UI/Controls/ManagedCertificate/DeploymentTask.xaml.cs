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

        protected ViewModel.DeploymentTaskConfigViewModel EditModel = new ViewModel.DeploymentTaskConfigViewModel(null);

        public bool EditAsPostRequestTask { get; set; } = true;

        public DeploymentTask()
        {
            InitializeComponent();
            DataContext = EditModel;

            this.StoredCredentials.ItemsSource = EditModel.FilteredCredentials;
            EditAsPostRequestTask = true;

        }

        public void SetEditItem(DeploymentTaskConfig config)
        {

            EditModel = new ViewModel.DeploymentTaskConfigViewModel(config);
            DataContext = EditModel;

            Task.Run(async () => await RefreshEditModelOptions(resetDefaults:false));

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

            await RefreshEditModelOptions(true);

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
            await RefreshEditModelOptions(true);
        }



        public async Task<bool> Save()
        {
            EditModel.CaptureEditedParameters();

            // validate task configuration using the selected provider

            var msgTitle = "Edit Deployment Task";

            if (EditModel.SelectedItem.TaskTypeId == null)
            {
                MessageBox.Show("Please select the required Task Type.", msgTitle);
                return false;
            }

            if (string.IsNullOrEmpty(EditModel.SelectedItem.TaskName))
            {
                if (!string.IsNullOrEmpty(EditModel.DeploymentProvider?.DefaultTitle))
                {
                    // use default title and continue
                    EditModel.SelectedItem.TaskName = EditModel.DeploymentProvider.DefaultTitle;
                }
            }

            if (string.IsNullOrEmpty(EditModel.SelectedItem.TaskName))
            {

                // check task name populated
                MessageBox.Show("A unique Task Name required, this may be used later to run the task manually.", msgTitle);
                return false;

            }
            else
            {
                // check task name is unique for this managed cert
                if (
                    AppViewModel.SelectedItem.PostRequestTasks?.Any(t => t.Id != EditModel.SelectedItem.Id && t.TaskName.ToLower().Trim() == EditModel.SelectedItem.TaskName.ToLower().Trim()) == true
                    || AppViewModel.SelectedItem.PreRequestTasks?.Any(t => t.Id != EditModel.SelectedItem.Id && t.TaskName.ToLower().Trim() == EditModel.SelectedItem.TaskName.ToLower().Trim()) == true
                 )
                {
                    MessageBox.Show("A unique Task Name is required, this task name is already in use for this managed certificate.", msgTitle);
                    return false;
                }
            }


            // if remote target, check target specified. TODO: Could also check host resolves.
            if (!string.IsNullOrEmpty(EditModel.SelectedItem.ChallengeProvider)
                && EditModel.SelectedItem.ChallengeProvider != StandardAuthTypes.STANDARD_AUTH_LOCAL
                && string.IsNullOrEmpty(EditModel.SelectedItem.TargetHost)
                )
            {
                // check task name populated
                MessageBox.Show("Target Host name or IP is required if deployment target is not Local.", msgTitle);
                return false;
            }

            // validate task provider specific config
            var results = await AppViewModel.CertifyClient.ValidateDeploymentTask(new Models.Utils.DeploymentTaskValidationInfo { ManagedCertificate = AppViewModel.SelectedItem, TaskConfig = EditModel.SelectedItem });
            if (results.Any(r => r.IsSuccess == false))
            {
                var firstFailure = results.FirstOrDefault(r => r.IsSuccess == false);
                MessageBox.Show(firstFailure.Message, msgTitle);
                return false;
            }

            if (EditAsPostRequestTask)
            {
                if (AppViewModel.SelectedItem.PostRequestTasks == null)
                {
                    AppViewModel.SelectedItem.PostRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
                }

                // add/update edited deployment task in selectedItem config
                if (EditModel.SelectedItem.Id == null)
                {
                    //add new
                    EditModel.SelectedItem.Id = Guid.NewGuid().ToString();
                    AppViewModel.SelectedItem.PostRequestTasks.Add(EditModel.SelectedItem);
                }
                else
                {
                    var original = AppViewModel.SelectedItem.PostRequestTasks.First(f => f.Id == EditModel.SelectedItem.Id);
                    AppViewModel.SelectedItem.PostRequestTasks[AppViewModel.SelectedItem.PostRequestTasks.IndexOf(original)] = EditModel.SelectedItem;
                }
            }
            else
            {
                if (AppViewModel.SelectedItem.PreRequestTasks == null)
                {
                    AppViewModel.SelectedItem.PreRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
                }

                // add/update edited deployment task in selectedItem config
                if (EditModel.SelectedItem.Id == null)
                {
                    //add new
                    EditModel.SelectedItem.Id = Guid.NewGuid().ToString();
                    AppViewModel.SelectedItem.PreRequestTasks.Add(EditModel.SelectedItem);
                }
                else
                {
                    var original = AppViewModel.SelectedItem.PreRequestTasks.First(f => f.Id == EditModel.SelectedItem.Id);
                    AppViewModel.SelectedItem.PreRequestTasks[AppViewModel.SelectedItem.PreRequestTasks.IndexOf(original)] = EditModel.SelectedItem;
                }
            }


            return true;
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
                    MessageBox.Show("Deployment Task command has been copied to the clipboard.");
                }
                else
                {
                    MessageBox.Show("Another process is preventing access to the clipboard. Please try again.");
                }
            }
        }
    }
}
