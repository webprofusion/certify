using Certify.Management;
using Certify.Models;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.UI.Models
{
    [ImplementPropertyChanged]
    public class AppModel : INotifyPropertyChanged
    {
        public static AppModel AppViewModel
        {
            get
            {
                return ((Certify.UI.App)Certify.UI.App.Current).AppViewModel;
            }
        }

        private CertifyManager certifyManager = null;

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string propertyName, object before, object after)
        {
            //Perform property validation
            var propertyChanged = PropertyChanged;
            if (propertyChanged != null)
            {
                propertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        #region properties

        public List<Certify.Models.ManagedSite> ManagedSites { get; set; }

        public Certify.Models.ManagedSite SelectedItem { get; set; }

        public bool ShowOnlyStartedSites { get; set; } = false;

        public List<SiteBindingItem> WebSiteList
        {
            get
            {
                //get list of sites from IIS
                var iisManager = new IISManager();
                return iisManager.GetPrimarySites(ShowOnlyStartedSites);
            }
        }

        public SiteBindingItem SelectedWebSite
        {
            get; set;
        }

        public DomainOption PrimarySubjectDomain
        {
            get
            {
                if (SelectedItem != null)
                {
                    var primary = SelectedItem.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain = true);
                    if (primary != null) return primary;
                }

                return null;
            }

            set
            {
                foreach (var d in SelectedItem.DomainOptions)
                {
                    if (d.Domain == value.Domain)
                    {
                        d.IsPrimaryDomain = true;
                        d.IsSelected = true;
                    }
                    else
                    {
                        d.IsPrimaryDomain = false;
                    }
                }

                SelectedItem.IsChanged = true;
            }
        }

        /// <summary>
        /// Determine if user should be able to choose/change the Website for the current SelectedItem
        /// </summary>
        public bool IsWebsiteSelectable
        {
            get
            {
                if (SelectedItem != null && SelectedItem.Id == null)
                {
                    return true;
                }
                return false;
            }
        }

        #endregion properties

        #region methods

        public AppModel()
        {
            certifyManager = new CertifyManager();
        }

        public void LoadSettings()
        {
            this.ManagedSites = certifyManager.GetManagedSites();

            if (this.ManagedSites.Any())
            {
                //preselect the first managed site
                this.SelectedItem = this.ManagedSites[0];
            }
        }

        public void SaveSettings()
        {
            certifyManager.SaveManagedSites(this.ManagedSites);
        }

        public async Task<List<Certify.Models.CertificateRequestResult>> RenewAll()
        {
            var results = await certifyManager.PerformRenewalAllManagedSites(false);
            return results;
        }

        public ManagedItem AddOrUpdateManagedSite(ManagedSite item)
        {
            var existing = this.ManagedSites.FirstOrDefault(s => s.Id == item.Id);

            //add new or replace existing

            if (existing != null)
            {
                this.ManagedSites.Remove(existing);
            }

            this.ManagedSites.Add(item);

            //save settings
            certifyManager.SaveManagedSites(this.ManagedSites);

            return item;
        }

        internal void DeleteManagedSite(ManagedSite selectedItem)
        {
            var existing = this.ManagedSites.FirstOrDefault(s => s.Id == selectedItem.Id);

            //remove existing

            if (existing != null)
            {
                this.ManagedSites.Remove(existing);
            }

            //save settings
            certifyManager.SaveManagedSites(this.ManagedSites);
        }

        #endregion methods
    }
}