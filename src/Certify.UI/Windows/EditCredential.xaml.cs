using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    public static class PasswordBoxAssistant
    {
        public static readonly DependencyProperty BoundPassword =
          DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxAssistant), new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

        public static readonly DependencyProperty BindPassword = DependencyProperty.RegisterAttached(
            "BindPassword", typeof(bool), typeof(PasswordBoxAssistant), new PropertyMetadata(false, OnBindPasswordChanged));

        private static readonly DependencyProperty UpdatingPassword =
            DependencyProperty.RegisterAttached("UpdatingPassword", typeof(bool), typeof(PasswordBoxAssistant), new PropertyMetadata(false));

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = d as PasswordBox;

            // only handle this event when the property is attached to a PasswordBox
            // and when the BindPassword attached property has been set to true
            if (d == null || !GetBindPassword(d))
            {
                return;
            }

            // avoid recursive updating by ignoring the box's changed event
            box.PasswordChanged -= HandlePasswordChanged;

            var newPassword = (string)e.NewValue;

            if (!GetUpdatingPassword(box))
            {
                box.Password = newPassword;
            }

            box.PasswordChanged += HandlePasswordChanged;
        }

        private static void OnBindPasswordChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            // when the BindPassword attached property is set on a PasswordBox,
            // start listening to its PasswordChanged event

            var box = dp as PasswordBox;

            if (box == null)
            {
                return;
            }

            var wasBound = (bool)(e.OldValue);
            var needToBind = (bool)(e.NewValue);

            if (wasBound)
            {
                box.PasswordChanged -= HandlePasswordChanged;
            }

            if (needToBind)
            {
                box.PasswordChanged += HandlePasswordChanged;
            }
        }

        private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
        {
            var box = sender as PasswordBox;

            // set a flag to indicate that we're updating the password
            SetUpdatingPassword(box, true);
            // push the new password into the BoundPassword property
            SetBoundPassword(box, box.Password);
            SetUpdatingPassword(box, false);
        }

        public static void SetBindPassword(DependencyObject dp, bool value)
        {
            dp.SetValue(BindPassword, value);
        }

        public static bool GetBindPassword(DependencyObject dp)
        {
            return (bool)dp.GetValue(BindPassword);
        }

        public static string GetBoundPassword(DependencyObject dp)
        {
            return (string)dp.GetValue(BoundPassword);
        }

        public static void SetBoundPassword(DependencyObject dp, string value)
        {
            dp.SetValue(BoundPassword, value);
        }

        private static bool GetUpdatingPassword(DependencyObject dp)
        {
            return (bool)dp.GetValue(UpdatingPassword);
        }

        private static void SetUpdatingPassword(DependencyObject dp, bool value)
        {
            dp.SetValue(UpdatingPassword, value);
        }
    }

    /// <summary>
    /// Selects template based on the type of the data item. 
    /// </summary>
    public class ControlTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var context = container as FrameworkElement;
            DataTemplate template = null;

            if (null == container)
            {
                throw new NullReferenceException("container");
            }
            else if (null == context)
            {
                throw new Exception("container must be FramekworkElement");
            }
            else if (null == item)
            {
                return null;
            }

            template = null;

            var providerParameter = item as ProviderParameter;
            if (providerParameter == null)
            {
                template = context.FindResource("ProviderStringParameter") as DataTemplate;
            }
            else if (providerParameter.IsPassword)
            {
                template = context.FindResource("ProviderPasswordParameter") as DataTemplate;
            }
            else if (providerParameter.Options.Count() != 0)
            {
                template = context.FindResource("ProviderDropDownParameter") as DataTemplate;
            }
            else
            {
                template = context.FindResource("ProviderStringParameter") as DataTemplate;
            }

            return template ?? base.SelectTemplate(item, container);
        }
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
                    ProviderType = EditViewModel.ChallengeProviders.First().Id
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
            var selectedType = ProviderTypes.SelectedItem as ProviderDefinition;
            if (selectedType != null)
            {
                EditViewModel.CredentialSet = new ObservableCollection<ProviderParameter>(selectedType.ProviderParameters.Where(p => p.IsCredential));
            }
        }

        private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
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
