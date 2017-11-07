using Certify.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
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
        public const string ITEMMANAGERCONFIG = "manageditems";

        /// <summary>
        /// If true, one or more of our managed sites are hosted within a Local IIS server on the
        /// same machine
        /// </summary>
        public bool EnableLocalIISMode { get; set; } //TODO: driven by config

        private Dictionary<string, ManagedSite> ManagedSites { get; set; }
        public string StorageSubfolder = ""; //if specifed will be appended to AppData path as subfolder to load/save to

        public ItemManager()
        {
            EnableLocalIISMode = true;
            ManagedSites = new Dictionary<string, ManagedSite>();
        }

        public void StoreSettings()
        {
            var watch = Stopwatch.StartNew();
            string appDataPath = Util.GetAppDataFolder(StorageSubfolder);

            lock (ITEMMANAGERCONFIG)
            {
                var path = Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.db");

                //backup settings file
                if (!File.Exists(path))
                {
                    SQLiteConnection.CreateFile(path);
                    using (var target = new SQLiteConnection($"Data Source={path}"))
                    {
                        target.Open();
                        using (var cmd = new SQLiteCommand("CREATE TABLE managedsettings (id TEXT NOT NULL UNIQUE PRIMARY KEY, json TEXT NOT NULL)", target))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                // save modified items into settings database
                using (var target = new SQLiteConnection($"Data Source={path}"))
                {
                    target.Open();
                    using (var tran = target.BeginTransaction())
                    {
                        foreach (var deleted in ManagedSites.Values.Where(s => s.Deleted).ToList())
                        {
                            using (var cmd = new SQLiteCommand("DELETE FROM managedsettings WHERE id=@id", target))
                            {
                                cmd.Parameters.Add(new SQLiteParameter("@id", deleted.Id));
                                cmd.ExecuteNonQuery();
                            }
                            ManagedSites.Remove(deleted.Id);
                        }
                        foreach (var changed in ManagedSites.Values.Where(s => s.IsChanged))
                        {
                            using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO managedsettings (id,json) VALUES (@id,@json)", target))
                            {
                                cmd.Parameters.Add(new SQLiteParameter("@id", changed.Id));
                                cmd.Parameters.Add(new SQLiteParameter("@json", JsonConvert.SerializeObject(changed)));
                                cmd.ExecuteNonQuery();
                            }
                            changed.IsChanged = false;
                        }
                        tran.Commit();
                    }
                }
            }

            // reset IsChanged as all items have been persisted
            Debug.WriteLine($"StoreSettings[Sqlite] took {watch.ElapsedMilliseconds}ms for {ManagedSites.Count} records");
        }

        public void DeleteAllManagedSites()
        {
            foreach (var site in ManagedSites.Values) site.Deleted = true;
        }

        public void LoadSettings()
        {
            UpgradeSettings();

            var watch = Stopwatch.StartNew();
            // FIXME: this method should be async and called only when absolutely required, these
            //        files can be hundreds of megabytes
            string appDataPath = Util.GetAppDataFolder(StorageSubfolder);
            var path = Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.db");

            if (File.Exists(path))
            {
                lock (ITEMMANAGERCONFIG)
                {
                    var managedSites = new List<ManagedSite>();
                    using (var target = new SQLiteConnection($"Data Source={path}"))
                    using (var cmd = new SQLiteCommand("SELECT json FROM managedsettings", target))
                    {
                        target.Open();
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                managedSites.Add(JsonConvert.DeserializeObject<ManagedSite>((string)reader["json"]));
                            }
                        }
                    }
                    foreach (var site in managedSites) site.IsChanged = false;
                    ManagedSites = managedSites.ToDictionary(s => s.Id);
                }
            }
            else
            {
                ManagedSites = new Dictionary<string, ManagedSite>();
            }
            Debug.WriteLine($"LoadSettings[Sqlite] took {watch.ElapsedMilliseconds}ms for {ManagedSites.Count} records");
        }

        private void UpgradeSettings()
        {
            var watch = Stopwatch.StartNew();
            string appDataPath = Util.GetAppDataFolder(StorageSubfolder);
            var json = Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.json");
            var db = Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.db");
            if (File.Exists(json) && !File.Exists(db))
            {
                lock (ITEMMANAGERCONFIG)
                {
                    // read managed sites using tokenize stream, this is useful for large files
                    var serializer = new JsonSerializer();
                    using (StreamReader sr = new StreamReader(json))
                    using (JsonTextReader reader = new JsonTextReader(sr))
                    {
                        var managedSiteList = serializer.Deserialize<List<ManagedSite>>(reader);

                        //safety check, if any dupe id's exists (which they shouldn't but the test data set did) make Id unique in the set.
                        var duplicateKeys = managedSiteList.GroupBy(x => x.Id).Where(group => group.Count() > 1).Select(group => group.Key);
                        foreach (var dupeKey in duplicateKeys)
                        {
                            var count = 0;
                            foreach (var i in managedSiteList.Where(m => m.Id == dupeKey))
                            {
                                i.Id = i.Id + "_" + count;
                                count++;
                            }
                        }

                        ManagedSites = managedSiteList.ToDictionary(s => s.Id);
                    }
                }

                StoreSettings(); // upgrade to SQLite db storage
                File.Delete($"{json}.bak");
                File.Move(json, $"{json}.bak");
                Debug.WriteLine($"UpgradeSettings[Json->Sqlite] took {watch.ElapsedMilliseconds}ms for {ManagedSites.Count} records");
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
                    Debug.WriteLine("Can't get IIS site list.");
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