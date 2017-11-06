using Certify.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    /// <summary>
    /// SiteManager encapsulates settings and operations on the list of Sites we manage certificates
    /// for using Certify and is additional to the ACMESharp Vault. These could be Local IIS,
    /// Manually Configured, DNS driven etc
    /// </summary>
    public class ItemManager
    {
        public const string ITEMMANAGERCONFIG = "manageditems.json";

        /// <summary>
        /// If true, one or more of our managed sites are hosted within a Local IIS server on the
        /// same machine
        /// </summary>
        public bool EnableLocalIISMode { get; set; } //TODO: driven by config

        private Dictionary<string,ManagedSite> ManagedSites { get; set; }
        public string StorageSubfolder = ""; //if specifed will be appended to AppData path as subfolder to load/save to

        public ItemManager()
        {
            EnableLocalIISMode = true;
            ManagedSites = new Dictionary<string,ManagedSite>(); // this.Preview();
        }

        public void StoreSettings()
        {
            string appDataPath = Util.GetAppDataFolder(StorageSubfolder);
            //string siteManagerConfig = Newtonsoft.Json.JsonConvert.SerializeObject(this.ManagedSites, Newtonsoft.Json.Formatting.Indented);

            lock (ITEMMANAGERCONFIG)
            {
                var path = Path.Combine(appDataPath, ITEMMANAGERCONFIG);

                //backup settings file
                if (File.Exists(path))
                {
                    // delete old settings backup if present
                    if (File.Exists(path + ".bak"))
                    {
                        File.Delete(path + ".bak");
                    }

                    // backup settings
                    File.Move(path, path + ".bak");
                }

                // serialize JSON directly to a file
                using (StreamWriter file = File.CreateText(path))
                {
                    new JsonSerializer().Serialize(file, ManagedSites.Values);
                }
            }

            // reset IsChanged as all items have been persisted
            foreach (var site in ManagedSites.Values) { site.IsChanged = false; }
        }

        public void DeleteAllManagedSites()
        {
            LoadSettings();
            ManagedSites.Clear();
            StoreSettings();
        }

        public void LoadSettings()
        {
            // FIXME: this method should be async and called only when absolutely required, these
            //        files can be hundreds of megabytes
            string appDataPath = Util.GetAppDataFolder(StorageSubfolder);
            var path = Path.Combine(appDataPath, ITEMMANAGERCONFIG);

            if (File.Exists(path))
            {
                lock (ITEMMANAGERCONFIG)
                {
                    // read managed sites using tokenize stream, this is useful for large files
                    var managedSites = new List<ManagedSite>();
                    var serializer = new JsonSerializer();
                    using (StreamReader sr = new StreamReader(path))
                    using (JsonTextReader reader = new JsonTextReader(sr))
                    {
                        managedSites = serializer.Deserialize<List<ManagedSite>>(reader);
                    }

                    // reset IsChanged for all loaded settings
                    managedSites.ForEach(s => s.IsChanged = false);
                    ManagedSites = managedSites.ToDictionary(s => s.Id);
                }
            }
            else
            {
                ManagedSites = new Dictionary<string,ManagedSite>();
            }
        }

        /// <summary>
        /// For current configured environment, show preview of recommended site management (for
        /// local IIS, scan sites and recommend actions)
        /// </summary>
        /// <returns></returns>
        public List<ManagedSite> Preview()
        {
            List<ManagedSite> sites = new List<ManagedSite>();

            if (EnableLocalIISMode)
            {
                try
                {
                    var iisSites = new IISManager().GetSiteBindingList(ignoreStoppedSites: CoreAppSettings.Current.IgnoreStoppedSites).OrderBy(s => s.SiteId).ThenBy(s => s.Host);

                    var siteIds = iisSites.GroupBy(x => x.SiteId);

                    foreach (var s in siteIds)
                    {
                        ManagedSite managedSite = new ManagedSite { Id = s.Key };
                        managedSite.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;
                        managedSite.TargetHost = "localhost";
                        managedSite.Name = iisSites.First(i => i.SiteId == s.Key).SiteName;

                        //TODO: replace site binding with domain options
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

        public ManagedSite GetManagedSite(string siteId)
        {
            return ManagedSites.TryGetValue(siteId, out var retval) ? retval : null;
        }

        public List<ManagedSite> GetManagedSites()
        {
            LoadSettings();
            return new List<ManagedSite>(ManagedSites.Values);
        }

        public void UpdatedManagedSites(List<ManagedSite> managedSites)
        {
            ManagedSites = managedSites.ToDictionary(site => site.Id);
            StoreSettings();
        }

        public void UpdatedManagedSite(ManagedSite managedSite, bool loadLatest = true, bool saveAfterUpdate = true)
        {
            if (loadLatest) LoadSettings();
            ManagedSites[managedSite.Id] = managedSite;
            if (saveAfterUpdate) StoreSettings();
        }

        public void DeleteManagedSite(ManagedSite site)
        {
            LoadSettings();
            ManagedSites.Remove(site.Id);
            StoreSettings();
        }
    }
}