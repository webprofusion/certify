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
using System.Windows.Shapes;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for Connections.xaml
    /// </summary>
    public partial class Connections 
    {
        public Connections()
        {
            InitializeComponent();


            var list = ViewModel.AppViewModel.Current.GetServerConnections();
            this.ConnectionList.ItemsSource = list;

        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            var selectedConnection = this.ConnectionList.SelectedItem;

            if (selectedConnection is Shared.ServerConnection)
            {
                ViewModel.AppViewModel.Current.InitServiceConnections(selectedConnection as Shared.ServerConnection);
                ViewModel.AppViewModel.Current.LoadSettingsAsync();
            }

       

            this.Close();
        }
    }
}
