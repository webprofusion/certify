using Certify.Models;
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

        private void UserControl_OnLoaded(object sender, RoutedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(lvManagedSites.ItemsSource).Filter = (item) =>
            {
                string filter = txtFilter.Text.Trim();
                return filter == "" || filter.Split(';').Where(f => f.Trim() != "").Any(f =>
                    ((Models.ManagedSite)item).Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1 ||
                    (((Models.ManagedSite)item).DomainOptions?.Any(d => d.Domain.IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1) ?? false) ||
                    (((Models.ManagedSite)item).Comments ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1);
            };
        }

        private void ListViewItem_InteractionEvent(object sender, InputEventArgs e)
        {
            var item = (ListViewItem)sender;
            var site = (ManagedSite)item.DataContext;
            bool changingSelection = MainViewModel.SelectedItem != site ||
                (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl));

            if (changingSelection && !MainViewModel.ConfirmDiscardUnsavedChanges())
            {
                // user did not want to discard changes, ignore click
                e.Handled = true;
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

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(lvManagedSites.ItemsSource).Refresh();
        }
    }
}