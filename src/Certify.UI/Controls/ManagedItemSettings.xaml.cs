using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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
            this.MainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        }

        private void MainViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            this.SettingsTab.SelectedIndex = 0;
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

                if (MainViewModel.SelectedItem.RequestConfig.PerformAutomatedCertBinding)
                {
                    MainViewModel.SelectedItem.RequestConfig.BindingIPAddress = null;
                    MainViewModel.SelectedItem.RequestConfig.BindingPort = null;
                    MainViewModel.SelectedItem.RequestConfig.BindingUseSNI = null;
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
            if (MainViewModel.SelectedItem != null)
            {
                if (MainViewModel.SelectedItem.IsChanged)
                {
                    //save changes
                    MainViewModel.SaveManagedItemChanges();
                }

                //begin request
                MainViewModel.MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                if (MainViewModel.BeginCertificateRequestCommand.CanExecute((MainViewModel.SelectedItem.Id)))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(
    () =>
    {
        MainViewModel.BeginCertificateRequestCommand.Execute(MainViewModel.SelectedItem.Id);
    }));
                }
            }
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

        private void SANDomain_Toggled(object sender, RoutedEventArgs e)
        {
            this.MainViewModel.SelectedItem.IsChanged = true;
        }
    }
}