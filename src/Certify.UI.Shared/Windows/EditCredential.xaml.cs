using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Certify.Models.Config;
using Newtonsoft.Json;

namespace Certify.UI.Windows
{
    /// <summary>
    /// A secret field being edited, which can instead take its value from an API Key or Token stored credential
    /// </summary>
    public class LinkableSecretField : Models.BindableBase
    {
        public LinkableSecretField(ProviderParameter secret, ProviderParameter link, List<StoredCredential> options)
        {
            Secret = secret;
            Link = link;
            Options = options;
        }

        public ProviderParameter Secret { get; }
        public ProviderParameter Link { get; }
        public List<StoredCredential> Options { get; }

        public bool IsChoosingLink { get; set; }

        public string LinkedStorageKey
        {
            get => Link.Value;
            set
            {
                Link.Value = value;
                IsChoosingLink = false;

                RaisePropertyChangedEvent(nameof(IsLinked));
                RaisePropertyChangedEvent(nameof(LinkedTitle));
            }
        }

        public bool IsLinked => !string.IsNullOrEmpty(Link.Value);

        public string LinkedTitle => Options.FirstOrDefault(o => o.StorageKey == Link.Value)?.Title ?? Link.Value;
    }

    public class EditCredentialViewModel : Models.BindableBase
    {

        public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        /// <summary>
        /// The credential fields being edited, including any links of secret fields to other credentials
        /// </summary>
        public List<ProviderParameter> CredentialFields { get; set; } = new List<ProviderParameter>();

        /// <summary>
        /// The fields as shown, with each secret field which can link to another credential shown together with its link
        /// </summary>
        public ObservableCollection<object> CredentialSet { get; set; }
        public StoredCredential Item { get; set; }
        public List<ChallengeProviderDefinition> ChallengeProviders { get; set; }

        /// <summary>
        /// Optional expiry date for the credential, exposed as a local DateTime for binding to a DatePicker
        /// </summary>
        public DateTime? ItemExpiryDate
        {
            get => Item?.DateExpiry?.UtcDateTime;
            set
            {
                if (Item != null)
                {
                    Item.DateExpiry = value == null ? (DateTimeOffset?)null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
                }
            }
        }

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
            Width *= MainViewModel.UIScaleFactor;
            Height *= MainViewModel.UIScaleFactor;

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

            if (string.IsNullOrEmpty(EditViewModel.Item.Title))
            {
                MessageBox.Show("Stored credentials require a name.");
                return;
            }

            if (!EditViewModel.CredentialFields.Any())
            {
                MessageBox.Show("No credentials selected.");
                return;
            }

            var valuesToStore = StoredCredentialLinks.GetValuesToStore(EditViewModel.CredentialFields);
            if (!valuesToStore.IsSuccess)
            {
                MessageBox.Show(valuesToStore.Message);
                return;
            }

            var credentialsToStore = valuesToStore.Result;

            var item = EditViewModel.Item;

            if (item.StorageKey != null)
            {
                // edit existing
                credential = new StoredCredential
                {
                    StorageKey = item.StorageKey,
                    ProviderType = item.ProviderType,
                    Secret = Newtonsoft.Json.JsonConvert.SerializeObject(credentialsToStore),
                    Title = item.Title,
                    AllowUnlock = item.AllowUnlock,
                    DateExpiry = item.DateExpiry
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
                    DateCreated = DateTime.UtcNow,
                    AllowUnlock = item.AllowUnlock,
                    DateExpiry = item.DateExpiry,
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
                    var fields = StoredCredentialLinks.GetEditableFields(selectedType);
                    var linkOptions = MainViewModel.StoredCredentials?.Where(c => c.ProviderType == StoredCredentialLinks.LinkedCredentialType).ToList() ?? new List<StoredCredential>();

                    EditViewModel.CredentialFields = fields;
                    EditViewModel.CredentialSet = new ObservableCollection<object>(fields
                        .Where(f => !StoredCredentialLinks.IsLinkKey(f.Key))
                        .Select(f =>
                        {
                            var link = fields.FirstOrDefault(l => l.Key == StoredCredentialLinks.GetLinkKey(f.Key));
                            return link != null ? new LinkableSecretField(f, link, linkOptions) : (object)f;
                        }));

                    EditViewModel.RaisePropertyChangedEvent(nameof(EditViewModel.SelectedChallengeProvider));
                    EditViewModel.RaisePropertyChangedEvent(nameof(EditViewModel.ItemExpiryDate));
                }
            }
        }

        private void LinkSecret_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is LinkableSecretField field)
            {
                field.IsChoosingLink = !field.IsChoosingLink;
            }
        }

        private void ClearSecretLink_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is LinkableSecretField field)
            {
                field.LinkedStorageKey = null;
            }
        }

        private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e) => Close();

        private void HelpUrl_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            e.Handled = true;
            if (e.Uri != null && e.Uri.IsAbsoluteUri)
            {
                Utils.Helpers.LaunchBrowser(e.Uri.AbsoluteUri);
            }
        }
    }
}
