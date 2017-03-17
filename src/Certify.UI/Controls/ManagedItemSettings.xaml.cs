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
                MainViewModel.SelectedItem = GetUpdatedManagedSiteSettings();
                MainViewModel.AddOrUpdateManagedSite(MainViewModel.SelectedItem);

                MainViewModel.MarkAllChangesCompleted();
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
            if (this.MainViewModel.SelectedWebSite != null)
            {
                PopulateManagedSiteSettings(this.MainViewModel.SelectedWebSite.SiteId);
            }
        }

        private void PrimaryDomain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void SANDomain_Toggled(object sender, RoutedEventArgs e)
        {
            this.MainViewModel.SelectedItem.IsChanged = true;
        }

        /// <summary>
        /// For the given set of options get a new CertRequestConfig to store
        /// </summary>
        /// <returns></returns>
        private ManagedSite GetUpdatedManagedSiteSettings()
        {
            var item = MainViewModel.SelectedItem;
            CertRequestConfig config = new CertRequestConfig();

            // RefreshDomainOptionSettingsFromUI();
            var primaryDomain = item.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true);

            //if no primary domain need to go back and select one
            if (primaryDomain == null) throw new ArgumentException("Primary subject domain must be set.");

            IdnMapping _idnMapping = new IdnMapping();
            config.PrimaryDomain = _idnMapping.GetAscii(primaryDomain.Domain); // ACME service requires international domain names in ascii mode

            //apply remaining selected domains as subject alternative names
            config.SubjectAlternativeNames =
                item.DomainOptions.Where(dm => dm.Domain != primaryDomain.Domain && dm.IsSelected == true)
                .Select(i => i.Domain)
                .ToArray();

            //config.PerformChallengeFileCopy = true;
            //config.PerformExtensionlessConfigChecks = !chkSkipConfigCheck.Checked;
            config.PerformAutoConfig = true;

            // config.EnableFailureNotifications = chkEnableNotifications.Checked;

            //determine if this site has an existing entry in Managed Sites, if so use that, otherwise start a new one

            if (MainViewModel.SelectedItem.Id == null)
            {
                var siteInfo = MainViewModel.SelectedWebSite;
                //if siteInfo null we need to go back and select a site

                item.Id = Guid.NewGuid().ToString() + ":" + siteInfo.SiteId;
                item.GroupId = siteInfo.SiteId;

                config.WebsiteRootPath = Environment.ExpandEnvironmentVariables(siteInfo.PhysicalPath);
            }

            item.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;

            //store domain options settings and request config for this site so we can replay for automated renewal
            // managedSite.DomainOptions = this.domains;
            //managedSite.RequestConfig = config;

            return item;
        }

        private void PopulateManagedSiteSettings(string siteId)
        {
            var managedSite = this.MainViewModel.SelectedItem;
            managedSite.Name = this.MainViewModel.SelectedWebSite.SiteName;

            //TODO: if this site would be a duplicate need to increment the site name

            //set defaults first
            managedSite.RequestConfig.PerformExtensionlessConfigChecks = true;
            managedSite.RequestConfig.PerformAutomatedCertBinding = true;
            managedSite.RequestConfig.PerformAutoConfig = true;
            managedSite.RequestConfig.EnableFailureNotifications = true;
            managedSite.RequestConfig.ChallengeType = "http-01";
            managedSite.IncludeInAutoRenew = true;
            managedSite.DomainOptions = new List<DomainOption>();

            //for the given selected web site, allow the user to choose which domains to combine into one certificate
            var allSites = new IISManager().GetSiteBindingList(false);
            var domains = new List<DomainOption>();
            foreach (var d in allSites)
            {
                if (d.SiteId == siteId)
                {
                    DomainOption opt = new DomainOption { Domain = d.Host, IsPrimaryDomain = false, IsSelected = true };
                    domains.Add(opt);
                }
            }

            if (domains.Any())
            {
                //mark first domain as primary, if we have no other settings
                if (!domains.Any(d => d.IsPrimaryDomain == true))
                {
                    domains[0].IsPrimaryDomain = true;
                }

                managedSite.DomainOptions = domains;

                //MainViewModel.EnsureNotifyPropertyChange(nameof(MainViewModel.PrimarySubjectDomain));
            }
            else
            {
                MessageBox.Show("The selected site has no domain bindings setup. Configure the domains first using Edit Bindings in IIS.", "Website Bindings");
            }

            //TODO: load settings from previously saved managed site?
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