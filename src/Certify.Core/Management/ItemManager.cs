using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Management
{
    /// <summary>
    /// SiteManager encapsulates settings and operations on the list of Sites we manage certificates for using Certify and is additional to the ACMESharp Vault. These could be Local IIS, Manually Configured, DNS driven etc
    /// </summary>
    public class ItemManager
    {
        private const string APPDATASUBFOLDER = "Certify";
        private const string ITEMMANAGERCONFIG = "config.json";

        /// <summary>
        /// If true, one or more of our managed sites are hosted within a Local IIS server on the same machine
        /// </summary>
        public bool EnableLocalIISMode { get; set; } //TODO: driven by config

        private List<ManagedSite> managedSites { get; set; }

        public ItemManager()
        {
            EnableLocalIISMode = true;
            this.managedSites = new List<ManagedSite>(); // this.Preview();
        }

        private string GetAppDataFolder()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\" + APPDATASUBFOLDER;
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
            }
            return path;
        }

        internal void UpdatedManagedSites(List<ManagedSite> managedSites)
        {
            this.managedSites = managedSites;
            this.StoreSettings();
        }

        public void StoreSettings()
        {
            string appDataPath = GetAppDataFolder();
            string siteManagerConfig = Newtonsoft.Json.JsonConvert.SerializeObject(this.managedSites, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(appDataPath + "\\" + ITEMMANAGERCONFIG, siteManagerConfig);
        }

        public void LoadSettings()
        {
            string appDataPath = GetAppDataFolder();
            var path = appDataPath + "\\" + ITEMMANAGERCONFIG;
            if (System.IO.File.Exists(path))
            {
                string configData = System.IO.File.ReadAllText(path);
                this.managedSites = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ManagedSite>>(configData);
            }
            else
            {
                this.managedSites = new List<ManagedSite>();
            }

            foreach (var s in this.managedSites)
            {
                s.IsChanged = false;
            }
        }

        /// <summary>
        /// For current configured environment, show preview of recommended site management (for local IIS, scan sites and recommend actions)
        /// </summary>
        /// <returns></returns>
        public List<ManagedSite> Preview()
        {
            List<ManagedSite> sites = new List<ManagedSite>();

            if (EnableLocalIISMode)
            {
                try
                {
                    var iisSites = new IISManager().GetSiteBindingList(includeOnlyStartedSites: true).OrderBy(s => s.SiteId).ThenBy(s => s.Host);

                    var siteIds = iisSites.GroupBy(x => x.SiteId);

                    foreach (var s in siteIds)
                    {
                        ManagedSite managedSite = new ManagedSite { Id = s.Key };
                        managedSite.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;
                        managedSite.TargetHost = "localhost";
                        managedSite.Name = iisSites.First(i => i.SiteId == s.Key).SiteName;

                        //TODO: replace sute binding with domain options
                        //managedSite.SiteBindings = new List<ManagedSiteBinding>();

                        foreach (var binding in s)
                        {
                            var managedBinding = new ManagedSiteBinding { Hostname = binding.Host, IP = binding.IP, Port = binding.Port, UseSNI = true, CertName = "Certify_" + binding.Host };
                            // managedSite.SiteBindings.Add(managedBinding);
                        }
                        sites.Add(managedSite);
                    }
                }
                catch (Exception)
                {
                    //can't read sites
                    System.Diagnostics.Debug.WriteLine("Can't get IIS site list.");
                }
            }
            return sites;
        }

        public ManagedSite GetManagedSite(string siteId, string domain = null)
        {
            var site = this.managedSites.FirstOrDefault(s => (siteId != null && s.Id == siteId) || (domain != null && s.DomainOptions.Any(bind => bind.Domain == domain)));
            return site;
        }

        public List<ManagedSite> GetManagedSites()
        {
            this.LoadSettings();
            return this.managedSites;
        }

        public void UpdatedManagedSite(ManagedSite managedSite)
        {
            this.LoadSettings();

            var existingSite = this.managedSites.FirstOrDefault(s => s.Id == managedSite.Id);
            if (existingSite != null)
            {
                this.managedSites.Remove(existingSite);
            }

            this.managedSites.Add(managedSite);
            this.StoreSettings();
        }

        public void DeleteManagedSite(ManagedSite site)
        {
            this.LoadSettings();

            var existingSite = this.managedSites.FirstOrDefault(s => s.Id == site.Id);
            if (existingSite != null)
            {
                this.managedSites.Remove(existingSite);
            }
            this.StoreSettings();
        }
    }
}