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
    /// Interaction logic for ManagedSites.xaml
    /// </summary>
    public partial class ManagedSites
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return ViewModel.AppModel.AppViewModel;
            }
        }

        public ManagedSites()
        {
            InitializeComponent();
            this.DataContext = MainViewModel;
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                if (MainViewModel.SelectedItem != null && MainViewModel.SelectedItemHasChanges && MainViewModel.SelectedItem.Id != null)
                {
                    //user needs to save or discard changes before changing selection
                    MessageBox.Show("You have unsaved changes. Save or Discard your changes before proceeding.");
                }
                else
                {
                    MainViewModel.SelectedItem = (Certify.Models.ManagedSite)e.AddedItems[0];
                    MainViewModel.MarkAllChangesCompleted();
                }
            }
        }

        private void ListView_TouchDown(object sender, TouchEventArgs e)
        {
        }

        private void ManagedItemSettings_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (MainViewModel.ManagedSites != null && MainViewModel.ManagedSites.Any())
            {
                // this.MainViewModel.ManagedSites[0].Name = DateTime.Now.ToShortDateString();
            }
        }
    }
}