using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Certify.Models.Config;

namespace Certify.UI.Windows
{
    public class EditCredentialViewModel : Models.BindableBase
    {
        public ObservableCollection<ProviderParameter> CredentialSet { get; set; }
        public StoredCredential Item { get; set; }
        public List<ProviderDefinition> ChallengeProviders { get; set; }
    }

    /// <summary>
    /// Interaction logic for EditCredential.xaml 
    /// </summary>
    public partial class EditCredential
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel
        {
            get
            {
                return ViewModel.AppViewModel.Current;
            }
        }

        public StoredCredential Item
        {
            get { return EditViewModel.Item; }
            set { EditViewModel.Item = Item; }
        }

        protected EditCredentialViewModel EditViewModel = new EditCredentialViewModel();

        public EditCredential(StoredCredential editItem = null)
        {
            InitializeComponent();

            this.DataContext = EditViewModel;

            // TODO: move to async
            if (MainViewModel.ChallengeAPIProviders == null && MainViewModel.ChallengeAPIProviders.Count == 0)
            {
                MainViewModel.RefreshChallengeAPIList().Wait();
            }

            EditViewModel.ChallengeProviders = MainViewModel
                .ChallengeAPIProviders
                .Where(p => p.ProviderParameters.Any(pa=>pa.IsCredential))
                .OrderBy(p=>p.Title)
                .ToList();

            if (editItem != null)
            {
                EditViewModel.Item = editItem;
            }

            if (EditViewModel.Item == null)
            {
                EditViewModel.Item = new StoredCredential
                {
                    ProviderType = EditViewModel.ChallengeProviders.First().Id
                };
            }

            this.RefreshCredentialOptions();
        }

        private async void Save_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StoredCredential credential;

            Dictionary<String, string> credentialsToStore = new Dictionary<string, string>();

            if (String.IsNullOrEmpty(EditViewModel.Item.Title))
            {
                MessageBox.Show("Stored credentials require a name.");
                return;
            }

            if (!EditViewModel.CredentialSet.Any())
            {
                MessageBox.Show("No credentials selected.");
                return;
            }

            foreach (var c in this.EditViewModel.CredentialSet)
            {
                //store entered value

                if (c.IsRequired && String.IsNullOrEmpty(c.Value))
                {
                    MessageBox.Show($"{c.Name} is a required value");
                    return;
                }

                if (!String.IsNullOrEmpty(c.Value))
                {
                    credentialsToStore.Add(c.Key, c.Value);
                }
            }

            var item = EditViewModel.Item;

            if (item.StorageKey != null)
            {
                // edit existing
                credential = new StoredCredential
                {
                    StorageKey = item.StorageKey,
                    ProviderType = item.ProviderType,
                    Secret = Newtonsoft.Json.JsonConvert.SerializeObject(credentialsToStore),
                    Title = item.Title
                };
            }
            else
            {
                //create new
                credential = new Models.Config.StoredCredential
                {
                    Title = item.Title,
                    ProviderType = item.ProviderType,
                    StorageKey = Guid.NewGuid().ToString(),
                    DateCreated = DateTime.Now,
                    Secret = Newtonsoft.Json.JsonConvert.SerializeObject(credentialsToStore)
                };
            }

            this.EditViewModel.Item = await MainViewModel.UpdateCredential(credential);

            this.Close();
        }

        private void CredentialTypes_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // populate credentials list
            if (e.Source != null)
            {
                this.RefreshCredentialOptions();
            }
        }

        private void RefreshCredentialOptions()
        {
            var selectedType = this.ProviderTypes.SelectedItem as ProviderDefinition;
            if (selectedType != null)
            {
                this.EditViewModel.CredentialSet = new ObservableCollection<ProviderParameter>(selectedType.ProviderParameters.Where(p=>p.IsCredential));
            }
        }

        private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.Close();
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            if (Item.StorageKey != null)
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var result = await MainViewModel.TestCredentials(Item.StorageKey);

                Mouse.OverrideCursor = Cursors.Arrow;

                MessageBox.Show(result.Message);
            }
        }
    }
}
