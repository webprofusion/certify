using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Certify.Models.Config;
using Newtonsoft.Json;

namespace Certify.UI.Windows
{
    public class EditCredentialViewModel : Models.BindableBase
    {

        public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        public ObservableCollection<ProviderParameter> CredentialSet { get; set; }
        public StoredCredential Item { get; set; }
        public List<ChallengeProviderDefinition> ChallengeProviders { get; set; }

        public ChallengeProviderDefinition SelectedChallengeProvider
        {
            get
            {
                if (Item != null && !string.IsNullOrEmpty(Item.ProviderType))
                {
                    return ChallengeProviders.FirstOrDefault(i => i.Id == Item.ProviderType);
                }
                else { return null; }
            }
        }
    }

    /// <summary>
    /// Interaction logic for EditCredential.xaml 
    /// </summary>
    public partial class EditCredential
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        public StoredCredential Item
        {
            get => EditViewModel.Item;
            set => EditViewModel.Item = value;
        }

        protected EditCredentialViewModel EditViewModel = new EditCredentialViewModel();

        public EditCredential(StoredCredential editItem = null)
        {

            EditViewModel = new EditCredentialViewModel();

            InitializeComponent();
            this.Width *= MainViewModel.UIScaleFactor;
            this.Height *= MainViewModel.UIScaleFactor;

            DataContext = EditViewModel;

            // TODO: move to async
            if (MainViewModel.ChallengeAPIProviders == null && MainViewModel.ChallengeAPIProviders.Count == 0)
            {
                MainViewModel.RefreshChallengeAPIList().Wait();
            }

            EditViewModel.ChallengeProviders = MainViewModel
                .ChallengeAPIProviders
                .Where(p => p.ProviderParameters.Any(pa => pa.IsCredential))
                .OrderBy(p => p.Title)
                .ToList();

            if (editItem != null)
            {
                EditViewModel.Item = editItem;
            }

            if (EditViewModel.Item == null)
            {
                EditViewModel.Item = new StoredCredential
                {
                    ProviderType = EditViewModel.ChallengeProviders.FirstOrDefault()?.Id
                };
            }

            RefreshCredentialOptions();
        }

        private async void Save_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StoredCredential credential;

            var credentialsToStore = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(EditViewModel.Item.Title))
            {
                MessageBox.Show("Stored credentials require a name.");
                return;
            }

            if (!EditViewModel.CredentialSet.Any())
            {
                MessageBox.Show("No credentials selected.");
                return;
            }

            foreach (var c in EditViewModel.CredentialSet)
            {
                //store entered value

                if (c.IsRequired && string.IsNullOrEmpty(c.Value))
                {
                    MessageBox.Show($"{c.Name} is a required value");
                    return;
                }

                if (!string.IsNullOrEmpty(c.Value))
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

            EditViewModel.Item = await MainViewModel.UpdateCredential(credential);

            Close();
        }

        private void CredentialTypes_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // populate credentials list
            if (e.Source != null)
            {
                RefreshCredentialOptions();
            }
        }

        private void RefreshCredentialOptions()
        {
            if (ProviderTypes.SelectedItem != null)
            {

                var selectedType = JsonConvert.DeserializeObject<ProviderDefinition>(JsonConvert.SerializeObject(ProviderTypes.SelectedItem as ProviderDefinition));
                if (selectedType != null)
                {
                    EditViewModel.CredentialSet = new ObservableCollection<ProviderParameter>(selectedType.ProviderParameters.Where(p => p.IsCredential));

                    EditViewModel.RaisePropertyChangedEvent(nameof(EditViewModel.SelectedChallengeProvider));
                }
            }
        }

        private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e) => Close();

        private void HelpUrl_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            e.Handled = true;

            Utils.Helpers.LaunchBrowser(e.Uri.AbsoluteUri);
        }
    }
}
