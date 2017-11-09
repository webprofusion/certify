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

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for VaultExplorer.xaml 
    /// </summary>
    public partial class VaultExplorer : UserControl
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return ViewModel.AppModel.AppViewModel;
            }
        }

        public VaultExplorer()
        {
            InitializeComponent();
            this.DataContext = MainViewModel;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            //refresh vault content
            //MainViewModel.LoadVaultTree();

            vaultTreeView.Items.Clear();
            foreach (var i in MainViewModel.VaultTree)
            {
                var item = new TreeViewItem();
                item.Header = i.Name;

                foreach (var c in i.Children)
                {
                    item.Items.Add(c.Name);
                }
                vaultTreeView.Items.Add(item);
            }
        }
    }
}