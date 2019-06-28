using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Certify.Config;
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
            EditModel.RefreshOptions();

            this.StoredCredentials.ItemsSource = EditModel.FilteredCredentials;

        }

        public void SetEditItem(DeploymentTaskConfig config)
        {
            EditModel = new ViewModel.DeploymentTaskConfigViewModel(config);
            DataContext = EditModel;

            EditModel.RefreshOptions();
            
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

        private async void ShowParamLookup_Click(object sender, RoutedEventArgs e)
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

        

        public bool Save()
        {
            EditModel.CaptureEditedParameters();

            // validate task configuration using the selected provider

            if (AppViewModel.SelectedItem.DeploymentTasks==null)
            {
                AppViewModel.SelectedItem.DeploymentTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
            }

            // add/update edited deployment task in selectedItem config
            if (EditModel.SelectedItem.Id == null)
            {
                //add new
                EditModel.SelectedItem.Id = Guid.NewGuid().ToString();
                AppViewModel.SelectedItem.DeploymentTasks.Add(EditModel.SelectedItem);
            } else
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
    }
}
