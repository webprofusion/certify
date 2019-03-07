using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Certify.Models.Config;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for DeploymentTask.xaml
    /// </summary>
    public partial class DeploymentTask : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        protected ViewModel.DeploymentTaskConfigViewModel EditModel = new ViewModel.DeploymentTaskConfigViewModel(null);

        public DeploymentTask()
        {
            InitializeComponent();

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

            await RefreshCredentialOptions();

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

        private void TaskProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TaskProviderList.SelectedValue != null)
            {
                this.EditModel.DeploymentProvider = AppViewModel.DeploymentTaskProviders.First(d => d.Id == TaskProviderList.SelectedValue.ToString());
                /*ProviderDescription.Text = this.EditModel.DeploymentProvider.Description;
                DeploymentTaskParams.ItemsSource = DeploymentProvider.ProviderParameters;*/
            }
        }

        private async Task RefreshCredentialOptions()
        {
            // filter list of matching credentials
            await AppViewModel.RefreshStoredCredentialsList();
            var credentials = AppViewModel.StoredCredentials.Where(s => s.ProviderType == EditModel.SelectedItem.ChallengeProvider);
            var currentSelectedValue = EditModel.SelectedItem.ChallengeCredentialKey;

            // updating item source also clears selected value, so this workaround sets it back
            // this is only an issue when you have two or more credentials for one provider
            StoredCredentialList.ItemsSource = credentials;

            if (currentSelectedValue != null)
            {
                EditModel.SelectedItem.ChallengeCredentialKey = currentSelectedValue;
            }

            //select first credential by default
            if (credentials.Count() > 0)
            {

                var selectedCredential = credentials.FirstOrDefault(c => c.StorageKey == EditModel.SelectedItem.ChallengeCredentialKey);
                if (selectedCredential != null)
                {
                    // ItemViewModel.PrimaryChallengeConfig.ChallengeCredentialKey = credentials.First().StorageKey;
                }
                else
                {
                    EditModel.SelectedItem.ChallengeCredentialKey = credentials.First().StorageKey;
                }
            }

        }

        private void RefreshParameters()
        {
            if (EditModel.SelectedItem.Parameters == null)
            {
                EditModel.SelectedItem.Parameters = new Dictionary<string, string>();
            }

            var definition = AppViewModel.ChallengeAPIProviders.FirstOrDefault(p => p.Id == EditModel.SelectedItem.ChallengeProvider);

            if (definition != null)
            {
                if (definition.ProviderParameters.Any(p => p.IsCredential))
                {
                    EditModel.UsesCredentials = true;
                }
                else
                {
                    EditModel.UsesCredentials = false;
                }

                EditModel.UsesCredentials = true;
                //
                // add or update provider parameters (if any) TODO: remove unused params
                var providerParams = definition.ProviderParameters.Where(p => p.IsCredential == false);

                foreach (var pa in providerParams)
                {
 
                    if (!EditModel.SelectedItem.Parameters.Any(p => p.Key == pa.Key))
                    {
                        EditModel.SelectedItem.Parameters.Add(pa.Key, "");
                    }
                }

                var toRemove = EditModel.SelectedItem.Parameters.ToList().Where(p => !providerParams.Any(pp => pp.Key == p.Key));
              
                foreach (var r in toRemove)
                {
                    EditModel.SelectedItem.Parameters.Remove(r.Key);
                }
            }
        }

        private async Task RefreshAllOptions()
        {
            RefreshParameters();
            await RefreshCredentialOptions();

        }

    }
}
