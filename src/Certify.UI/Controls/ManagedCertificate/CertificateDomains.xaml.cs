using Certify.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for CertificateDomains.xaml 
    /// </summary>
    public partial class CertificateDomains : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateModel ItemViewModel => UI.ViewModel.ManagedCertificateModel.Current;
        protected Certify.UI.ViewModel.AppModel AppViewModel => UI.ViewModel.AppModel.Current;

        private Models.ManagedCertificate SelectedItem => ItemViewModel.SelectedItem;

        public CertificateDomains()
        {
            InitializeComponent();
            this.AppViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        }

        private void SetFilter()
        {
            CollectionViewSource.GetDefaultView(SelectedItem.DomainOptions).Filter = (item) =>
            {
                string filter = DomainFilter.Text.Trim();
                return filter == "" || filter.Split(';').Where(f => f.Trim() != "").Any(f =>
                    ((Models.DomainOption)item).Domain.IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1);
            };
        }

        private async void MainViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedItem")
            {
                //get list of sites from local server if we don't already have it
                if (ItemViewModel.WebSiteList.Count == 0) await ItemViewModel.RefreshWebsiteList();

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

                if (SelectedItem != null)
                {
                    // if website previously selected, preselect in dropdown
                    if (SelectedItem.GroupId == null) SelectedItem.GroupId = "";

                    var selectedWebsite = ItemViewModel.WebSiteList.FirstOrDefault(w => w.SiteId == SelectedItem.GroupId);
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
            if (ItemViewModel.SelectedWebSite != null)
            {
                string siteId = ItemViewModel.SelectedWebSite.SiteId;

                SiteQueryInProgress.Visibility = Visibility.Visible;

                await ItemViewModel.PopulateManagedCertificateSettings(siteId);

                SiteQueryInProgress.Visibility = Visibility.Hidden;
            }
        }

        private async void RefreshSanList_Click(object sender, RoutedEventArgs e)
        {
            await ItemViewModel.SANRefresh();
        }

        private void AddDomains_Click(object sender, RoutedEventArgs e)
        {
            if (ItemViewModel.UpdateDomainOptions(ManualDomains.Text))
            {
                // domains added, clear entry
                ManualDomains.Text = "";
            }
        }

        private void DataGrid_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // auto toggle item included/not included
            var dg = (DataGrid)sender;
            if (dg.SelectedItem != null)
            {
                var opt = (DomainOption)dg.SelectedItem;
                opt.IsSelected = !opt.IsSelected;
            }
        }

        private void DomainFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            SetFilter();

            //var defaultView = CollectionViewSource.GetDefaultView(DomainOptionsList.DataContext);
            //defaultView.Refresh();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
        }
    }
}