using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Newtonsoft.Json;

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

        private ConcurrentDictionary<string, ManagedCertificate> _managedCertificatesCache { get; set; }
        public string StorageSubfolder = ""; //if specified will be appended to AppData path as subfolder to load/save to
        public bool IsSingleInstanceMode { get; set; } = true; //if true, access to this resource is centralised so we can make assumptions about when reload of settings is required etc

        // TODO: make db path configurable on service start
        private string _dbPath=$"C:\\programdata\\certify\\{ITEMMANAGERCONFIG}.db";
        private string _connectionString = "";

        public ItemManager()
        {
            _managedCertificatesCache = new ConcurrentDictionary<string, ManagedCertificate>();

            _dbPath = GetDbPath();

            _connectionString = $"Data Source={_dbPath};PRAGMA temp_store=MEMORY;";
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

            //create database if it doesn't exist
            if (!File.Exists(_dbPath))
            {
                using (var db = new SQLiteConnection(_connectionString))
                {
                    await db.OpenAsync();
                    using (var cmd = new SQLiteCommand("CREATE TABLE manageditem (id TEXT NOT NULL UNIQUE PRIMARY KEY, parentid TEXT NULL, json TEXT NOT NULL)", db))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            else
            {
                await UpgradeSchema();
            }

            // save all new/modified items into settings database
            using (var db = new SQLiteConnection(_connectionString))
            {
                await db.OpenAsync();
                using (var tran = db.BeginTransaction())
                {
                    foreach (var deleted in _managedCertificatesCache.Values.Where(s => s.Deleted).ToList())
                    {
                        using (var cmd = new SQLiteCommand("DELETE FROM manageditem WHERE id=@id", db))
                        {
                            cmd.Parameters.Add(new SQLiteParameter("@id", deleted.Id));
                            await cmd.ExecuteNonQueryAsync();
                        }
                        _managedCertificatesCache.TryRemove(deleted.Id, out var val);
                    }

                    foreach (var changed in _managedCertificatesCache.Values.Where(s => s.IsChanged))
                    {
                        using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO manageditem (id,parentid,json) VALUES (@id,@parentid, @json)", db))
                        {
                            cmd.Parameters.Add(new SQLiteParameter("@id", changed.Id));
                            cmd.Parameters.Add(new SQLiteParameter("@parentid", changed.ParentId));
                            cmd.Parameters.Add(new SQLiteParameter("@json", JsonConvert.SerializeObject(changed)));
                            await cmd.ExecuteNonQueryAsync();
                        }
                        changed.IsChanged = false;
                    }
                    tran.Commit();
                }
            }

            Debug.WriteLine($"StoreSettings[SQLite] took {watch.ElapsedMilliseconds}ms for {_managedCertificatesCache.Count} records");
        }

        private async Task UpgradeSchema()
        {
            // attempt column upgrades
            using (var db = new SQLiteConnection(_connectionString))
            {
                await db.OpenAsync();
                try
                {
                    using (var cmd = new SQLiteCommand("ALTER TABLE manageditem ADD COLUMN parentid TEXT", db))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                catch
                {
                    // column may already exist
                }
            }
        }

        public async Task DeleteAllManagedCertificates()
        {
            foreach (var site in _managedCertificatesCache.Values)
            {
                site.Deleted = true;
                await DeleteManagedCertificate(site);
            }
        }

        public async Task LoadAllManagedCertificates(bool skipIfLoaded = false)
        {
            if (skipIfLoaded && _managedCertificatesCache.Any()) return;

            await UpgradeSettings();

            var watch = Stopwatch.StartNew();
            
            // FIXME: this query should called only when absolutely required as the result set may be very large
            if (File.Exists(_dbPath))
            {
                var managedCertificates = new List<ManagedCertificate>();
                using (var db = new SQLiteConnection(_connectionString))
                using (var cmd = new SQLiteCommand("SELECT id, json FROM manageditem", db))
                {
                    await db.OpenAsync();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string itemId = (string)reader["id"];

                            var managedCertificate = JsonConvert.DeserializeObject<ManagedCertificate>((string)reader["json"]);

                            // in some cases users may have previously manipulated the id, causing
                            // duplicates. Correct the ID here (database Id is unique):
                            if (managedCertificate.Id != itemId)
                            {
                                managedCertificate.Id = itemId;
                                Debug.WriteLine("LoadSettings: Corrected managed site id: " + managedCertificate.Name);
                            }

                            managedCertificates.Add(managedCertificate);
                        }
                        reader.Close();
                    }
                }


                _managedCertificatesCache.Clear();

                foreach (var site in managedCertificates) {
                    site.IsChanged = false;
                    _managedCertificatesCache.AddOrUpdate(site.Id, site, (key, oldValue) => site);
                }

            }
            else
            {
                _managedCertificatesCache.Clear();
            }

            Debug.WriteLine($"LoadSettings[SQLite] took {watch.ElapsedMilliseconds}ms for {_managedCertificatesCache.Count} records");
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
                    var managedCertificateList = serializer.Deserialize<List<ManagedCertificate>>(reader);

                    //safety check, if any dupe id's exists (which they shouldn't but the test data set did) make Id unique in the set.
                    var duplicateKeys = managedCertificateList.GroupBy(x => x.Id).Where(group => group.Count() > 1).Select(group => group.Key);
                    foreach (var dupeKey in duplicateKeys)
                    {
                        var count = 0;
                        foreach (var i in managedCertificateList.Where(m => m.Id == dupeKey))
                        {
                            i.Id = i.Id + "_" + count;
                            count++;
                        }
                    }

                    // update cache
                    _managedCertificatesCache.Clear();

                    foreach (var site in managedCertificateList)
                    {
                        site.IsChanged = false;
                        _managedCertificatesCache.AddOrUpdate(site.Id, site, (key, oldValue) => site);
                    }
                }

                await StoreSettings(); // upgrade to SQLite db storage
                File.Delete($"{json}.bak");
                File.Move(json, $"{json}.bak");
                Debug.WriteLine($"UpgradeSettings[Json->SQLite] took {watch.ElapsedMilliseconds}ms for {_managedCertificatesCache.Count} records");
            }
            else
            {
                if (!File.Exists(db))
                {
                    // no setting to upgrade, create the empty database
                    await StoreSettings();
                }
                else
                {
                    // apply schema upgrades
                    await UpgradeSchema();
                }
            }
        }

        private async Task<ManagedCertificate> LoadManagedCertificate(string siteId)
        {
            ManagedCertificate managedCertificate = null;

            using (var db = new SQLiteConnection(_connectionString))
            using (var cmd = new SQLiteCommand("SELECT json FROM manageditem WHERE id=@id", db))
            {
                cmd.Parameters.Add(new SQLiteParameter("@id", siteId));

                await db.OpenAsync();
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        managedCertificate = JsonConvert.DeserializeObject<ManagedCertificate>((string)reader["json"]);
                        managedCertificate.IsChanged = false;

                        _managedCertificatesCache.AddOrUpdate(managedCertificate.Id, managedCertificate, (key, oldValue) => managedCertificate);
                    }
                    reader.Close();
                }
            }

            return managedCertificate;
        }

        public async Task<ManagedCertificate> GetManagedCertificate(string siteId)
        {
            ManagedCertificate result = null;
            if (_managedCertificatesCache == null || !_managedCertificatesCache.Any())
            {
                Debug.WriteLine("GetManagedCertificate: No managed sites loaded, will load item directly.");
            }
            else
            {
                // try to get cached version
                result = _managedCertificatesCache.TryGetValue(siteId, out var retval) ? retval : null;
            }

            // if we don't have cached copy of info, load it from db
            if (result == null)
            {
                result = await LoadManagedCertificate(siteId);
            }
            return result;
        }

        public async Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter = null, bool reloadAll = true)
        {
            // Don't reload settings unless we need to or we are unsure if any items have changed
            if (!_managedCertificatesCache.Any() || IsSingleInstanceMode == false || reloadAll) await LoadAllManagedCertificates();

            // filter and convert dictionary to list TODO: use db instead of in memory filter?
            var items = _managedCertificatesCache.Values.AsQueryable();
            if (filter != null)
            {
                if (!String.IsNullOrEmpty(filter.Keyword)) items = items.Where(i => i.Name.ToLowerInvariant().Contains(filter.Keyword.ToLowerInvariant()));

                if (!String.IsNullOrEmpty(filter.ChallengeType)) items = items.Where(i => i.RequestConfig.Challenges != null && i.RequestConfig.Challenges.Any(t => t.ChallengeType == filter.ChallengeType));

                if (!String.IsNullOrEmpty(filter.ChallengeProvider)) items = items.Where(i => i.RequestConfig.Challenges != null && i.RequestConfig.Challenges.Any(t => t.ChallengeProvider == filter.ChallengeProvider));

                if (!String.IsNullOrEmpty(filter.StoredCredentialKey)) items = items.Where(i => i.RequestConfig.Challenges != null && i.RequestConfig.Challenges.Any(t => t.ChallengeCredentialKey == filter.StoredCredentialKey));

                //TODO: IncludeOnlyNextAutoRenew
                if (filter.MaxResults > 0) items = items.Take(filter.MaxResults);
            }
            return new List<ManagedCertificate>(items);
        }

        public async Task<ManagedCertificate> UpdatedManagedCertificate(ManagedCertificate managedCertificate, bool saveAfterUpdate = true)
        {
            if (managedCertificate == null) return null;

            if (managedCertificate.Id == null) managedCertificate.Id = Guid.NewGuid().ToString();

            if (_managedCertificatesCache == null) _managedCertificatesCache = new ConcurrentDictionary<string, ManagedCertificate>();

            _managedCertificatesCache.AddOrUpdate(managedCertificate.Id, managedCertificate, (key, oldValue) => managedCertificate);

            if (saveAfterUpdate)
            {
                if (!System.IO.File.Exists(_dbPath)) await UpgradeSettings();

                using (var db = new SQLiteConnection(_connectionString))
                {
                    await db.OpenAsync();
                    using (var tran = db.BeginTransaction())
                    {
                        using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO manageditem (id, parentid, json) VALUES (@id,@parentid,@json)", db))
                        {
                            cmd.Parameters.Add(new SQLiteParameter("@id", managedCertificate.Id));
                            cmd.Parameters.Add(new SQLiteParameter("@parentid", managedCertificate.ParentId));
                            cmd.Parameters.Add(new SQLiteParameter("@json", JsonConvert.SerializeObject(managedCertificate)));
                            await cmd.ExecuteNonQueryAsync();
                        }
                        tran.Commit();
                    }
                }
            }

            return _managedCertificatesCache[managedCertificate.Id];
        }

        public async Task DeleteManagedCertificate(ManagedCertificate site)
        {
            // save modified items into settings database
            using (var db = new SQLiteConnection(_connectionString))
            {
                await db.OpenAsync();
                using (var tran = db.BeginTransaction())
                {
                    using (var cmd = new SQLiteCommand("DELETE FROM manageditem WHERE id=@id", db))
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@id", site.Id));
                        await cmd.ExecuteNonQueryAsync();
                    }
                    tran.Commit();
                    Debug.WriteLine($"DeleteManagedCertificate: Completed {site.Id}");
                    _managedCertificatesCache.TryRemove(site.Id, out var val);
                }
            }
        }
    }
}
