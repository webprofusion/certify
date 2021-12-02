using System;
using System.Windows;
using System.Windows.Input;
using Certify.Shared;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Edit details for a certify server connection
    /// </summary>
    public partial class EditConnectionDialog
    {
        public ServerConnection Item { get; set; }

        public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;


        public EditConnectionDialog(ServerConnection editItem = null)
        {
            InitializeComponent();

            Item = new ServerConnection();
            Item.Id = Guid.NewGuid().ToString();
            Item.ServerMode = "v2";

            if (editItem != null)
            {
                // clone item for editing
                Item = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerConnection>(Newtonsoft.Json.JsonConvert.SerializeObject(editItem));
            }

            DataContext = this;

            this.Width *= MainViewModel.UIScaleFactor;
            this.Height *= MainViewModel.UIScaleFactor;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Arrow;
            Close();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            //add/update connection

            Mouse.OverrideCursor = Cursors.Wait;

            var resultOK = await MainViewModel.SaveServerConnection(Item);

            Mouse.OverrideCursor = Cursors.Arrow;

            if (resultOK)
            {
                Close();
            }
            else
            {
                MessageBox.Show("Failed to save connection details.");
            }

        }

    }
}
