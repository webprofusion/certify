using Certify.Management;
using Certify.Models;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Certify.UI.ViewModel
{
    [ImplementPropertyChanged]
    public class AppModel : BindableBase
    {
        public static AppModel AppViewModel { get; } = new AppModel();

        private CertifyManager certifyManager = null;

        #region properties

        public ObservableCollection<Certify.Models.ManagedSite> ManagedSites { get; set; }

        private Certify.Models.ManagedSite _selectedItem;

        public Certify.Models.ManagedSite SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                _selectedItem = value;
                RaisePropertyChanged(nameof(SelectedItem));
            }
        }

        public bool SelectedItemHasChanges
        {
            get
            {
                if (this.SelectedItem != null)
                {
                    if (this.SelectedItem.IsChanged || (this.SelectedItem.RequestConfig != null && this.SelectedItem.RequestConfig.IsChanged) || (this.SelectedItem.DomainOptions != null && this.SelectedItem.DomainOptions.Any(d => d.IsChanged)))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

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

        /// <summary>
        /// Reset all IsChanged flags for the Selected Item
        /// </summary>
        internal void MarkAllChangesCompleted()
        {
            SelectedItem.IsChanged = false;
            SelectedItem.RequestConfig.IsChanged = false;
            SelectedItem.DomainOptions.ForEach(d => d.IsChanged = false);

            RaisePropertyChanged(nameof(SelectedItemHasChanges));
        }

        internal void SelectFirstOrDefaultItem()
        {
            SelectedItem = ManagedSites.FirstOrDefault();
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

        public bool IsItemSelected
        {
            get
            {
                return (this.SelectedItem != null);
            }
        }

        public bool IsSelectedItemValid
        {
            get
            {
                if (this.SelectedItem != null && this.SelectedItem.Id != null && this.SelectedItem.IsChanged == false)
                {
                    return true;
                }
                else
                {
                    return false;
                }
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
            this.ManagedSites = new ObservableCollection<ManagedSite>(certifyManager.GetManagedSites());

            if (this.ManagedSites.Any())
            {
                //preselect the first managed site
                //  this.SelectedItem = this.ManagedSites[0];
            }
        }

        public void SaveSettings(object param)
        {
            certifyManager.SaveManagedSites(this.ManagedSites.ToList());
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
            certifyManager.SaveManagedSites(this.ManagedSites.ToList());

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
            certifyManager.SaveManagedSites(this.ManagedSites.ToList());
        }

        #endregion methods

        #region commands

        //public ICommand SaveAllCommand => new RelayCommand(SaveSettings);
        //public ICommand AddOrUpdateManagedSiteCommand => new RelayCommand(AddOrUpdateManagedSiteCommand);

        #endregion commands
    }
}