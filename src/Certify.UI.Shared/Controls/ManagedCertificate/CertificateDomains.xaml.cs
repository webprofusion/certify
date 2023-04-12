using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Certify.Models;
using Certify.Models.Shared.Validation;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for CertificateDomains.xaml 
    /// </summary>
    public partial class CertificateDomains : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        public CertificateDomains()
        {
            InitializeComponent();

            AppViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
            AppViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        }

        private void SetFilter() => CollectionViewSource.GetDefaultView(ItemViewModel.SelectedItem.DomainOptions).Filter = (item) =>
                                  {
                                      var filter = DomainFilter.Text.Trim();
                                      return filter == "" || filter.Split(';').Where(f => f.Trim() != "").Any(f =>
                                          ((Models.DomainOption)item).Domain.IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1);
                                  };

        private async void MainViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedItem")
            {
                ItemViewModel.IsNameEditMode = false;

                //get list of sites from local server if we don't already have it
                if (ItemViewModel.WebSiteList.Count == 0)
                {
                    await ItemViewModel.RefreshWebsiteList();
                }

                if (ItemViewModel.WebSiteList.Count > 0)
                {
                    WebsiteDropdown.ItemsSource = ItemViewModel.WebSiteList;
                    WebsiteDropdown.IsEnabled = true;
                }
                else
                {
                    WebsiteDropdown.IsEnabled = false;
                    WebsiteDropdown.IsEditable = true;
                    WebsiteDropdown.IsReadOnly = true;

                    WebsiteDropdown.Text = "(No IIS Sites Found)";
                }

                if (ItemViewModel.SelectedItem != null)
                {
                    // if website previously selected, preselect in dropdown
                    if (ItemViewModel.SelectedItem.GroupId == null)
                    {
                        ItemViewModel.SelectedItem.GroupId = "";
                    }

                    var selectedWebsite = ItemViewModel.WebSiteList.FirstOrDefault(w => w.Id == ItemViewModel.SelectedItem.GroupId);
                    if (selectedWebsite != null)
                    {
                        ItemViewModel.SelectedWebSite = selectedWebsite;
                    }
                    else
                    {
                        ItemViewModel.SelectedWebSite = null;
                    }
                }
            }
        }

        private async void Website_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ItemViewModel.SelectedWebSite != null && ItemViewModel.SelectedItem != null)
            {
                var siteId = ItemViewModel.SelectedWebSite.Id;

                await ItemViewModel.PopulateManagedCertificateSettings(siteId);
            }
        }

        private async void RefreshWebsiteList_Click(object sender, RoutedEventArgs e)
        {
            await ItemViewModel.RefreshWebsiteList();
        }

        private async void RefreshSanList_Click(object sender, RoutedEventArgs e) => await ItemViewModel.SANRefresh();

        private void AddDomains_Click(object sender, RoutedEventArgs e)
        {
            if (ItemViewModel.UpdateDomainOptions(ManualDomains.Text))
            {
                // domains added, clear entry
                ManualDomains.Text = "";
            }
        }

        private void DomainFilter_TextChanged(object sender, TextChangedEventArgs e) => SetFilter();

        private void RemoveDomainOption_Click(object sender, RoutedEventArgs e)
        {
            if (DomainOptionsList.SelectedItem is DomainOption opt)
            {
                if (MessageBox.Show($"Remove {opt.Domain}?", "Confirm Domain Option Removal", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    ItemViewModel.SelectedItem.DomainOptions.Remove(opt);
                }
            }
        }

        private void SetPrimaryDomainOption_Click(object sender, RoutedEventArgs e)
        {
            if (DomainOptionsList.SelectedItem is DomainOption opt)
            {
                CertificateEditorService.SetPrimarySubjectDomain(ItemViewModel.SelectedItem, opt);
            }
        }

        private void ManualDomains_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                AddDomains_Click(sender, e);
            }
        }

        private void ToggleSelectedDomainOption(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // auto toggle item included/not included
            if (DomainOptionsList.SelectedItem is DomainOption opt)
            {
                opt.IsSelected = !opt.IsSelected;
            }
        }
    }
}
