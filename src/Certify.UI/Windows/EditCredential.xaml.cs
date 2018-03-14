using Certify.Models.Config;
using System;
using System.Linq;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for EditCredential.xaml 
    /// </summary>
    public partial class EditCredential
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return ViewModel.AppModel.Current;
            }
        }

        public StoredCredential Item { get; set; }

        public EditCredential()
        {
            InitializeComponent();

            this.ProviderTypes.ItemsSource = ChallengeProviders.Providers.Where(p => p.ProviderParameters.Any());
        }

        private async void Save_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var secrets = new string[] { UserID.Text, Secret1.Text, Secret2.Text };

            StoredCredential credential;

            if (Item != null)
            {
                // edit existing
                credential = new StoredCredential
                {
                    StorageKey = Item.StorageKey,
                    ProviderType = Item.ProviderType,
                    DateCreated = DateTime.Now,
                    Secret = Newtonsoft.Json.JsonConvert.SerializeObject(secrets),
                    Title = CredentialTitle.Text
                };
            }
            else
            {
                //create new
                credential = new Models.Config.StoredCredential
                {
                    Title = CredentialTitle.Text,
                    ProviderType = "",
                    StorageKey = Guid.NewGuid().ToString(),
                    Secret = Newtonsoft.Json.JsonConvert.SerializeObject(secrets)
                };
            }

            Item = await MainViewModel.UpdateCredential(credential);
            this.Close();
        }

        private void CredentialTypes_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
        }
    }
}