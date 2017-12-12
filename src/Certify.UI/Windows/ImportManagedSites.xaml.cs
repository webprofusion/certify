using Certify.Models;
using Certify.UI.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for ImportManagedSites.xaml 
    /// </summary>
    public partial class ImportManagedSites
    {
        protected AppModel MainViewModel { get => AppModel.Current; }

        public ImportManagedSites()
        {
            InitializeComponent();

            this.DataContext = MainViewModel;
        }

        private void ButtonPerformImport(object sender, RoutedEventArgs e)
        {
            MainViewModel.ManagedSites = new ObservableCollection<ManagedSite>(MainViewModel.ImportedManagedSites);

            //MainViewModel.SaveSettings();
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