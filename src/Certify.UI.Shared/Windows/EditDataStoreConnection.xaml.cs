using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Certify.Models;
using Certify.Models.Config;
using Certify.Providers;
using Certify.Shared;
using Certify.UI.ViewModel;
using Newtonsoft.Json;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Edit details for a certify data store connection
    /// </summary>
    public partial class EditDataStoreConnectionDialog
    {
        public class EditModel : BindableBase
        {
            public DataStoreConnection Item { get; set; }
            public List<ProviderDefinition> DataStoreProviders { get; set; }
        }

        public EditModel Model { get; set; }
        public AppViewModel MainViewModel => ViewModel.AppViewModel.Current;
        public EditDataStoreConnectionDialog(DataStoreConnection editItem = null)
        {
            InitializeComponent();

            Model = new EditModel
            {
                Item = editItem != null ? JsonConvert.DeserializeObject<DataStoreConnection>(JsonConvert.SerializeObject(editItem)) :
                new DataStoreConnection { Id = Guid.NewGuid().ToString(), Title = "", TypeId = "postgres", ConnectionConfig = "" }
            };

            if (editItem != null)
            {
                // provider type can't be changed after initial save
                ProviderTypes.IsEnabled = false;
            }

            if (Model.Item.IsProtected)
            {
                // the connection details we have are masked, so they can only be replaced outright - editing them in
                // place would send back a partial change which the service cannot merge with the stored secrets
                ConnectionConfig.IsReadOnly = true;
                ProtectedConnectionPanel.Visibility = Visibility.Visible;
            }

            DataContext = this;

            Width *= MainViewModel.UIScaleFactor;
            Height *= MainViewModel.UIScaleFactor;

        }

        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Model.DataStoreProviders = await MainViewModel.GetDataStoreProviders();
        }

        /// <summary>
        /// Clear the masked connection details so new ones can be entered. The masked value does not contain the
        /// stored secrets, so it cannot be sent back as a partial change.
        /// </summary>
        private void ReplaceConnectionDetails_Click(object sender, RoutedEventArgs e)
        {
            Model.Item.IsProtected = false;
            Model.Item.ConnectionConfig = "";

            // the item is a plain model without change notification, so the text box is updated directly
            ConnectionConfig.IsReadOnly = false;
            ConnectionConfig.Text = "";
            ProtectedConnectionPanel.Visibility = Visibility.Collapsed;

            ConnectionConfig.Focus();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Arrow;
            Close();
        }

        private bool Validate()
        {
            var validationError = "";

            if (string.IsNullOrEmpty(Model.Item.Title))
            {
                validationError = "The data store connection requires a title.";
            }

            if (string.IsNullOrEmpty(Model.Item.TypeId))
            {
                validationError = "A provider type is required";
            }

            if (Model.Item.TypeId == "sqlite")
            {
                validationError = "SQLite is the default store type and adding additional SQLite data stores (or editing the default one) is currently not supported";
            }

            if (string.IsNullOrEmpty(Model.Item.ConnectionConfig))
            {
                validationError = "Data store connections require a connection string to specify the connection details to the data source.";
            }

            if (!string.IsNullOrEmpty(validationError))
            {
                MessageBox.Show(validationError, "Add/Edit Data Store Validation Failed");
                return false;
            }
            else
            {
                return true;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate())
            {
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;

            var results = await MainViewModel.SaveDataStoreConnection(Model.Item);

            Mouse.OverrideCursor = Cursors.Arrow;

            if (!results.Any(r => r.HasError))
            {
                Close();
            }
            else
            {
                var err = results.First(r => r.HasError);
                MessageBox.Show(err.Description, err.Title);
            }
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate())
            {
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;

            var results = await MainViewModel.TestDataStoreConnection(Model.Item);

            Mouse.OverrideCursor = Cursors.Arrow;

            if (results.Any(r => r.HasError))
            {
                var err = results.First(r => r.HasError);
                MessageBox.Show(err.Description, err.Title);
            }
            else if (results.Any(r => r.HasWarning))
            {
                var warning = results.First(r => r.HasWarning);
                MessageBox.Show(warning.Description, warning.Title);
            }
            else
            {
                var upgrade = results.FirstOrDefault(r => r.Key == DataStoreActionKeys.SchemaUpgradeAvailable);

                MessageBox.Show(
                    upgrade != null
                        ? $"The data store test was successful.{Environment.NewLine}{Environment.NewLine}{upgrade.Description}"
                        : "The data store test was successful",
                    "Data Store Test");
            }
        }

        private async void ApplyMigrations_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate())
            {
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;

            var check = await MainViewModel.CheckDataStoreSchema(Model.Item);

            Mouse.OverrideCursor = Cursors.Arrow;

            if (!check.IsMigrationRequired && !check.HasOptionalMigrations)
            {
                MessageBox.Show(check.Message, "Apply Migrations");
                return;
            }

            // spell out why an optional step is offered, and that declining leaves a working store working
            var pending = string.Join(Environment.NewLine, check.PendingMigrations.Select(m => m.IsOptional
                ? $" - {m.Description}{Environment.NewLine}   (optional) {m.OptionalReason}"
                : $" - {m.Description}"));

            var optionalNote = check.HasOptionalMigrations && !check.IsMigrationRequired
                ? $"{Environment.NewLine}{Environment.NewLine}This data store works as it is - these changes are optional."
                : string.Empty;

            var confirmation = MessageBox.Show(
                $"{check.Message}{optionalNote}{Environment.NewLine}{Environment.NewLine}{pending}{Environment.NewLine}{Environment.NewLine}Apply these changes to the database now?",
                "Apply Migrations",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;

            var results = await MainViewModel.ApplyDataStoreSchemaMigrations(Model.Item);

            Mouse.OverrideCursor = Cursors.Arrow;

            var failure = results.FirstOrDefault(r => r.HasError);

            if (failure != null)
            {
                MessageBox.Show(failure.Description, failure.Title);
            }
            else
            {
                MessageBox.Show(results.FirstOrDefault()?.Description ?? "Migrations applied.", "Apply Migrations");
            }
        }
    }
}
