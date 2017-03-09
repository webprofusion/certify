using Certify.Management;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.UI.Models
{
    public class AppModel : BindableBase
    {
        public AppModel()
        {
        }

        public List<Certify.Models.ManagedSite> ManagedSites { get; set; }

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
        }
    }
}