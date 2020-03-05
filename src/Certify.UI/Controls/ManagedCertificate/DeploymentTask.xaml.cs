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

        public DeploymentTask()
        {
            InitializeComponent();
            DataContext = EditModel;

            this.StoredCredentials.ItemsSource = EditModel.FilteredCredentials;

        }

        public void SetEditItem(DeploymentTaskConfig config)
        {

            EditModel = new ViewModel.DeploymentTaskConfigViewModel(config);
            DataContext = EditModel;

            Task.Run(async () => { await EditModel.RefreshOptions(); });

        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);


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

            await EditModel.RefreshOptions();
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
                /*ProviderDescription.Text = this.EditModel.DeploymentProvider.Description;
               DeploymentTaskParams.ItemsSource = DeploymentProvider.ProviderParameters;*/
                await EditModel.RefreshOptions();
            }
        }

        private async void TargetType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await EditModel.RefreshOptions();
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
                // check task name populated
                MessageBox.Show("A unique Task Name required, this may be used later to run the task manually.", msgTitle);
                return false;
            }
            else
            {
                // check task name is unique for this managed cert
                if (AppViewModel.SelectedItem.DeploymentTasks?.Any() == true)
                {
                    if (AppViewModel.SelectedItem.DeploymentTasks.Any(t => t.Id != EditModel.SelectedItem.Id && t.TaskName.ToLower().Trim() == EditModel.SelectedItem.TaskName.ToLower().Trim()))
                    {
                        MessageBox.Show("A unique Task Name is required, this task name is already in use for this managed certificate.", msgTitle);
                        return false;
                    }
                }
            }

            // if remote target, check target specified. TODO: Could also check host resolves.
            if (
                EditModel.SelectedItem.ChallengeProvider != StandardAuthTypes.STANDARD_AUTH_LOCAL &&
                string.IsNullOrEmpty(EditModel.SelectedItem.TargetHost)
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

            if (AppViewModel.SelectedItem.DeploymentTasks == null)
            {
                AppViewModel.SelectedItem.DeploymentTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
            }

            // add/update edited deployment task in selectedItem config
            if (EditModel.SelectedItem.Id == null)
            {
                //add new
                EditModel.SelectedItem.Id = Guid.NewGuid().ToString();
                AppViewModel.SelectedItem.DeploymentTasks.Add(EditModel.SelectedItem);
            }
            else
            {
                var original = AppViewModel.SelectedItem.DeploymentTasks.First(f => f.Id == EditModel.SelectedItem.Id);
                AppViewModel.SelectedItem.DeploymentTasks[AppViewModel.SelectedItem.DeploymentTasks.IndexOf(original)] = EditModel.SelectedItem;
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
