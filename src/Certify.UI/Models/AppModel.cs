using Certify.Management;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.UI.Models
{
    [ImplementPropertyChanged]
    public class AppModel
    {
        public AppModel()
        {
        }

        public List<Certify.Models.ManagedSite> ManagedSites { get; set; }

        public Certify.Models.ManagedSite SelectedItem { get; set; }

        public static AppModel AppViewModel
        {
            get
            {
                return ((Certify.UI.App)Certify.UI.App.Current).AppViewModel;
            }
        }

        public void LoadSettings()
        {
            var certifyManager = new CertifyManager();
            this.ManagedSites = certifyManager.GetManagedSites();

            this.SelectedItem = this.ManagedSites[0];
        }
    }
}