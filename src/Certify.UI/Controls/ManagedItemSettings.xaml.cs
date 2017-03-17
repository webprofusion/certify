using Certify.Management;
using Certify.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Interaction logic for ManagedItemSettings.xaml
    /// </summary>
    public partial class ManagedItemSettings : UserControl
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return UI.ViewModel.AppModel.AppViewModel;
            }
        }

        public ManagedItemSettings()
        {
            InitializeComponent();
        }

        private void Button_Save(object sender, RoutedEventArgs e)
        {
            if (this.MainViewModel.SelectedItemHasChanges)
            {
                if (MainViewModel.SelectedItem.Id == null && MainViewModel.SelectedWebSite == null)
                {
                    MessageBox.Show("Select the website to create a certificate for.");
                    return;
                }

                if (String.IsNullOrEmpty(MainViewModel.SelectedItem.Name))
                {
                    MessageBox.Show("A name is required for this item.");
                    return;
                }

                if (MainViewModel.PrimarySubjectDomain == null)
                {
                    MessageBox.Show("A Primary Domain must be selected");
                    return;
                }
                //save changes

                //creating new managed item
                MainViewModel.SaveManagedItemChanges();
            }
            else
            {
                MessageBox.Show("No changes were made, skipping save");
            }
        }

        private void Button_DiscardChanges(object sender, RoutedEventArgs e)
        {
            //if new item, discard and select first item in managed sites
            if (MainViewModel.SelectedItem.Id == null)
            {
                ReturnToDefaultManagedItemView();
            }
            else
            {
                //reload settings for managed sites, discard changes
                var currentSiteId = MainViewModel.SelectedItem.Id;
                MainViewModel.LoadSettings();
                MainViewModel.SelectedItem = MainViewModel.ManagedSites.FirstOrDefault(m => m.Id == currentSiteId);
            }

            MainViewModel.MarkAllChangesCompleted();
        }

        private void ReturnToDefaultManagedItemView()
        {
            MainViewModel.SelectFirstOrDefaultItem();
        }

        private void Button_RequestCertificate(object sender, RoutedEventArgs e)
        {
            MainViewModel.SelectedItem.IsChanged = true;
        }

        private void Button_Delete(object sender, RoutedEventArgs e)
        {
            if (this.MainViewModel.SelectedItem.Id == null)
            {
                //item not saved, discard
                ReturnToDefaultManagedItemView();
            }
            else
            {
                if (MessageBox.Show("Are you sure you want to delete this item? Deleting the item will not affect IIS settings etc.", "Confirm Delete", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    this.MainViewModel.DeleteManagedSite(this.MainViewModel.SelectedItem);
                    ReturnToDefaultManagedItemView();
                }
            }
        }

        private void Website_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainViewModel.SelectedWebSite != null)
            {
                string siteId = MainViewModel.SelectedWebSite.SiteId;
                if (MainViewModel.PopulateManagedSiteSettingsCommand.CanExecute(siteId))
                {
                    MainViewModel.PopulateManagedSiteSettingsCommand.Execute(siteId);
                }
            }
        }

        private void PrimaryDomain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void SANDomain_Toggled(object sender, RoutedEventArgs e)
        {
            this.MainViewModel.SelectedItem.IsChanged = true;
        }
    }

    [System.Windows.Data.ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBooleanConverter : System.Windows.Data.IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            if (targetType != typeof(bool?) && targetType != typeof(bool))
                throw new InvalidOperationException("The target must be a boolean");
            if (value == null) return false;
            return !(bool)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            return Convert(value, targetType, parameter, culture);
        }

        #endregion IValueConverter Members
    }
}