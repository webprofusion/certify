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
    /// Interaction logic for ImportManagedSites.xaml
    /// </summary>
    public partial class ImportManagedSites
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return ViewModel.AppModel.AppViewModel;
            }
        }

        public ImportManagedSites()
        {
            InitializeComponent();

            this.DataContext = MainViewModel;
        }

        private void ButtonPerformImport(object sender, RoutedEventArgs e)
        {
            this.MainViewModel.ManagedSites = new System.Collections.ObjectModel.ObservableCollection<Models.ManagedSite>(this.MainViewModel.ImportedManagedSites);

            MainViewModel.SaveSettings(null);
            this.Close();
        }

        private void ButtonSkipImport(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MergeOptionChanged(object sender, RoutedEventArgs e)
        {
            //perform merged/unmerged import preview
            MainViewModel.PreviewImport(MergeOptionCheckbox.IsChecked != null ? (bool)MergeOptionCheckbox.IsChecked : false);
        }
    }
}