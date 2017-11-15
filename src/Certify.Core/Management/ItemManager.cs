using Certify.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        private Dictionary<string, ManagedSite> ManagedSitesCache { get; set; }
        public string StorageSubfolder = ""; //if specifed will be appended to AppData path as subfolder to load/save to
        public bool IsSingleInstanceMode { get; set; } = true; //if true, access to this resource is centralised so we can make assumptions about when reload of settings is required etc

        public ItemManager()
        {
            ManagedSitesCache = new Dictionary<string, ManagedSite>();
        }

        private string GetDbPath()
        {
            string appDataPath = Util.GetAppDataFolder(StorageSubfolder);
            return Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.db");
        }

        /// <summary>
        /// Perform a full backup and save of the current set of managed sites 
        /// </summary>
        public async Task StoreSettings()
        {
            var watch = Stopwatch.StartNew();

            var path = GetDbPath();

            //create database if it doesn't exist
            if (!File.Exists(path))
            {
                using (var db = new SQLiteConnection($"Data Source={path}"))
                {
                    await db.OpenAsync();
                    using (var cmd = new SQLiteCommand("CREATE TABLE manageditem (id TEXT NOT NULL UNIQUE PRIMARY KEY, json TEXT NOT NULL)", db))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }

            // save all new/modified items into settings database
            using (var db = new SQLiteConnection($"Data Source={path}"))
            {
                await db.OpenAsync();
                using (var tran = db.BeginTransaction())
                {
                    foreach (var deleted in ManagedSitesCache.Values.Where(s => s.Deleted).ToList())
                    {
                        using (var cmd = new SQLiteCommand("DELETE FROM manageditem WHERE id=@id", db))
                        {
                            cmd.Parameters.Add(new SQLiteParameter("@id", deleted.Id));
                            await cmd.ExecuteNonQueryAsync();
                        }
                        ManagedSitesCache.Remove(deleted.Id);
                    }
                    foreach (var changed in ManagedSitesCache.Values.Where(s => s.IsChanged))
                    {
                        using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO manageditem (id,json) VALUES (@id,@json)", db))
                        {
                            cmd.Parameters.Add(new SQLiteParameter("@id", changed.Id));
                            cmd.Parameters.Add(new SQLiteParameter("@json", JsonConvert.SerializeObject(changed)));
                            await cmd.ExecuteNonQueryAsync();
                        }
                        changed.IsChanged = false;
                    }
                    tran.Commit();
                }
            }

            // reset IsChanged as all items have been persisted
            Debug.WriteLine($"StoreSettings[SQLite] took {watch.ElapsedMilliseconds}ms for {ManagedSitesCache.Count} records");
        }

        public async Task DeleteAllManagedSites()
        {
            foreach (var site in ManagedSitesCache.Values)
            {
                site.Deleted = true;
                await DeleteManagedSite(site);
            }
        }

        public async Task LoadAllManagedItems(bool skipIfLoaded = false)
        {
            if (skipIfLoaded && ManagedSitesCache.Any()) return;

            await UpgradeSettings();

            var watch = Stopwatch.StartNew();
            // FIXME: this method should be async and called only when absolutely required, these
            //        files can be hundreds of megabytes
            var path = GetDbPath();
            if (File.Exists(path))
            {
                var managedSites = new List<ManagedSite>();
                using (var db = new SQLiteConnection($"Data Source={path}"))
                using (var cmd = new SQLiteCommand("SELECT json FROM manageditem", db))
                {
                    await db.OpenAsync();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            managedSites.Add(JsonConvert.DeserializeObject<ManagedSite>((string)reader["json"]));
                        }
                    }
                }

                foreach (var site in managedSites) site.IsChanged = false;
                ManagedSitesCache = managedSites.ToDictionary(s => s.Id);
            }
            else
            {
                ManagedSitesCache = new Dictionary<string, ManagedSite>();
            }
            Debug.WriteLine($"LoadSettings[SQLite] took {watch.ElapsedMilliseconds}ms for {ManagedSitesCache.Count} records");
        }

        private async Task UpgradeSettings()
        {
            var watch = Stopwatch.StartNew();
            string appDataPath = Util.GetAppDataFolder(StorageSubfolder);
            var json = Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.json");
            var db = Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.db");

            if (File.Exists(json) && !File.Exists(db))
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

                    ManagedSitesCache = managedSiteList.ToDictionary(s => s.Id);
                }

                await StoreSettings(); // upgrade to SQLite db storage
                File.Delete($"{json}.bak");
                File.Move(json, $"{json}.bak");
                Debug.WriteLine($"UpgradeSettings[Json->SQLite] took {watch.ElapsedMilliseconds}ms for {ManagedSitesCache.Count} records");
            }
            else
            {
                if (!File.Exists(db))
                {
                    // no setting to upgrade, create the empty database
                    await StoreSettings();
                }
               
            }
        }

        private async Task<ManagedSite> LoadManagedSite(string siteId)
        {
            ManagedSite managedSite = null;

            using (var db = new SQLiteConnection($"Data Source={GetDbPath()}"))
            using (var cmd = new SQLiteCommand("SELECT json FROM manageditem WHERE id=@id", db))
            {
                cmd.Parameters.Add(new SQLiteParameter("@id", siteId));

                db.Open();
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        managedSite = JsonConvert.DeserializeObject<ManagedSite>((string)reader["json"]);
                        managedSite.IsChanged = false;
                        ManagedSitesCache[managedSite.Id] = managedSite;
                    }
                }
            }

            return managedSite;
        }

        public async Task<ManagedSite> GetManagedSite(string siteId)
        {
            ManagedSite result = null;
            if (ManagedSitesCache == null || !ManagedSitesCache.Any())
            {
                Debug.WriteLine("GetManagedSite: No managed sites loaded, will load item directly.");
            }
            else
            {
                // try to get cached version
                result = ManagedSitesCache.TryGetValue(siteId, out var retval) ? retval : null;
            }

            // if we don't have cached copy of info, load it from db
            if (result == null)
            {
                result = await LoadManagedSite(siteId);
            }
            return result;
        }

        public async Task<List<ManagedSite>> GetManagedSites(ManagedSiteFilter filter = null, bool reloadAll = true)
        {
            // Don't reload settings unless we need to or we are unsure if any items have changed
            if (!ManagedSitesCache.Any() || IsSingleInstanceMode == false || reloadAll) await LoadAllManagedItems();

            // filter and convert dictionary to list TODO: use db instead of in memory filter?
            var items = ManagedSitesCache.Values.AsEnumerable();
            if (filter != null)
            {
                if (!String.IsNullOrEmpty(filter.Keyword)) items = items.Where(i => i.Name.ToLowerInvariant().Contains(filter.Keyword.ToLowerInvariant()));

                //TODO: IncludeOnlyNextAutoRenew
                if (filter.MaxResults > 0) items = items.Take(filter.MaxResults);
            }
            return new List<ManagedSite>(items);
        }

        public async Task<ManagedSite> UpdatedManagedSite(ManagedSite managedSite, bool saveAfterUpdate = true)
        {
            ManagedSitesCache[managedSite.Id] = managedSite;

            if (saveAfterUpdate)
            {
                using (var db = new SQLiteConnection($"Data Source={GetDbPath()}"))
                {
                    db.Open();
                    using (var tran = db.BeginTransaction())
                    {
                        using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO manageditem (id,json) VALUES (@id,@json)", db))
                        {
                            cmd.Parameters.Add(new SQLiteParameter("@id", managedSite.Id));
                            cmd.Parameters.Add(new SQLiteParameter("@json", JsonConvert.SerializeObject(managedSite)));
                            await cmd.ExecuteNonQueryAsync();
                        }
                        tran.Commit();
                    }
                }
            }

            return ManagedSitesCache[managedSite.Id];
        }

        public async Task DeleteManagedSite(ManagedSite site)
        {
            // save modified items into settings database
            using (var db = new SQLiteConnection($"Data Source={GetDbPath()}"))
            {
                db.Open();
                using (var tran = db.BeginTransaction())
                {
                    using (var cmd = new SQLiteCommand("DELETE FROM manageditem WHERE id=@id", db))
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@id", site.Id));
                        await cmd.ExecuteNonQueryAsync();
                    }
                    tran.Commit();
                    Debug.WriteLine($"DeleteManagedSite: Completed {site.Id}");
                    ManagedSitesCache.Remove(site.Id);
                }
            }
        }
    }
}