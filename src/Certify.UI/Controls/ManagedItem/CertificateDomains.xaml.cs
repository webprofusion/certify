using Certify.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Certify.UI.Controls.ManagedItem
{
    /// <summary>
    /// Interaction logic for CertificateDomains.xaml 
    /// </summary>
    public partial class CertificateDomains : UserControl
    {
        public ObservableCollection<SiteBindingItem> WebSiteList { get; set; }

        protected Certify.UI.ViewModel.AppModel MainViewModel => UI.ViewModel.AppModel.Current;

        public CertificateDomains()
        {
            InitializeComponent();
            this.MainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            WebSiteList = new ObservableCollection<SiteBindingItem>();
        }

        private void SetFilter()
        {
            CollectionViewSource.GetDefaultView(MainViewModel.SelectedItem.DomainOptions).Filter = (item) =>
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
                // ie only need the list of sites for new managed sites, existing ones are already set
                if (MainViewModel.SelectedItem != null && MainViewModel.SelectedItem.Id == null)
                {
                    //get list of sites from IIS. FIXME: this is async and we should gather this at startup (or on refresh) instead
                    WebSiteList = new ObservableCollection<SiteBindingItem>(await MainViewModel.CertifyClient.GetServerSiteList(StandardServerTypes.IIS));
                    WebsiteDropdown.ItemsSource = WebSiteList;
                }
            }
        }

        private async void Website_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainViewModel.SelectedWebSite != null)
            {
                string siteId = MainViewModel.SelectedWebSite.SiteId;

                SiteQueryInProgress.Visibility = Visibility.Visible;
                await MainViewModel.PopulateManagedSiteSettings(siteId);
                SiteQueryInProgress.Visibility = Visibility.Hidden;
            }
        }

        private async void RefreshSanList_Click(object sender, RoutedEventArgs e)
        {
            await MainViewModel.SANRefresh();
        }

        private void AddDomains_Click(object sender, RoutedEventArgs e)
        {
            var item = MainViewModel.SelectedItem;

            // parse text input to add as manual domain options
            string domains = ManualDomains.Text;
            if (!string.IsNullOrEmpty(domains))
            {
                var domainList = domains.Split(",; ".ToCharArray());
                string invalidDomains = "";
                foreach (var d in domainList)
                {
                    if (!string.IsNullOrEmpty(d.Trim()))
                    {
                        var domain = d.ToLower().Trim();
                        if (!item.DomainOptions.Any(o => o.Domain == domain))
                        {
                            var option = new DomainOption
                            {
                                Domain = domain,
                                IsManualEntry = true,
                                IsSelected = true
                            };
                            if (Uri.CheckHostName(domain) == UriHostNameType.Dns || (domain.StartsWith("*.") && Uri.CheckHostName(domain.Replace("*.", "")) == UriHostNameType.Dns))
                            {
                                item.DomainOptions.Add(option);
                            }
                            else
                            {
                                invalidDomains += domain + "\n";
                            }
                        }
                    }
                }

                if (!String.IsNullOrEmpty(invalidDomains))
                {
                    MessageBox.Show("Invalid domains: " + invalidDomains);
                }
                else
                {
                    //all domain added, clear manual entry
                    ManualDomains.Text = "";
                }
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
    }
}