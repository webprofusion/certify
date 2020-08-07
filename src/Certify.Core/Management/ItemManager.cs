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
using Polly;
using Polly.Retry;

namespace Certify.Management
{


    /// <summary>
    /// SiteManager encapsulates settings and operations on the list of Sites we manage certificates
    /// for using Certify and is additional to the ACMESharp Vault. These could be Local IIS,
    /// Manually Configured, DNS driven etc
    /// </summary>
    public class ItemManager : IItemManager
    {
        public const string ITEMMANAGERCONFIG = "manageditems";

        public string _storageSubFolder = ""; //if specified will be appended to AppData path as subfolder to load/save to
        public bool IsSingleInstanceMode { get; set; } = true; //if true, access to this resource is centralised so we can make assumptions about when reload of settings is required etc

        // TODO: make db path configurable on service start
        private readonly string _dbPath = $"C:\\programdata\\certify\\{ITEMMANAGERCONFIG}.db";
        private readonly string _connectionString;

        private AsyncRetryPolicy _retryPolicy = Policy.Handle<SQLiteException>().WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(1));

        public ItemManager(string storageSubfolder = null)
        {
            if (!string.IsNullOrEmpty(storageSubfolder))
            {
                _storageSubFolder = storageSubfolder;
            }

            _dbPath = GetDbPath();

            _connectionString = $"Data Source={_dbPath};PRAGMA temp_store=MEMORY;";

            if (File.Exists(_dbPath))
            {
                // upgrade schema if db exists
                var upgraded = UpgradeSchema().Result;
            }
            else
            {
                // upgrade from JSON storage if db doesn't exist yet
                var settingsUpgraded = UpgradeSettings().Result;
            }
        }

        private string GetDbPath()
        {
            var appDataPath = Util.GetAppDataFolder(_storageSubFolder);
            return Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.db");
        }

        private async Task CreateManagedItemsSchema()
        {
            try
            {
                using (var db = new SQLiteConnection(_connectionString))
                {
                    await db.OpenAsync();
                    using (var cmd = new SQLiteCommand("CREATE TABLE manageditem (id TEXT NOT NULL UNIQUE PRIMARY KEY, parentid TEXT NULL, json TEXT NOT NULL)", db))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    db.Close();
                }
            }
            catch { }
        }

        /// <summary>
        /// Perform a full backup and save of the current set of managed sites
        /// </summary>
        public async Task StoreAll(IEnumerable<ManagedCertificate> list)
        {
            var watch = Stopwatch.StartNew();

            // create database if it doesn't exist
            if (!File.Exists(_dbPath))
            {
                await this.CreateManagedItemsSchema();
            }

            // save all new/modified items into settings database
            using (var db = new SQLiteConnection(_connectionString))
            {
                await db.OpenAsync();
                using (var tran = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {
                        using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO manageditem (id,parentid,json) VALUES (@id,@parentid, @json)", db))
                        {
                            cmd.Parameters.Add(new SQLiteParameter("@id", item.Id));
                            cmd.Parameters.Add(new SQLiteParameter("@parentid", item.ParentId));
                            cmd.Parameters.Add(new SQLiteParameter("@json", JsonConvert.SerializeObject(item)));
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    tran.Commit();
                }
            }

            Debug.WriteLine($"StoreSettings[SQLite] took {watch.ElapsedMilliseconds}ms for {list.Count()} records");
        }

        private async Task<bool> UpgradeSchema()
        {
            // attempt column upgrades
            var cols = new List<string>();

            using (var db = new SQLiteConnection(_connectionString))
            {
                await db.OpenAsync();
                try
                {
                    using (var cmd = new SQLiteCommand("PRAGMA table_info(manageditem);", db))
                    {

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {

                            while (await reader.ReadAsync())
                            {
                                var colname = (string)reader["name"];
                                cols.Add(colname);
                            }
                        }
                    }

                    if (!cols.Contains("parentid"))
                    {
                        // upgrade schema
                        using (var cmd = new SQLiteCommand("ALTER TABLE manageditem ADD COLUMN parentid TEXT", db))
                        {
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch
                {
                    // error checking for upgrade, ensure table exists
                    await CreateManagedItemsSchema();

                    return false;
                }
            }

            return true;
        }
        public async Task DeleteAll()
        {
            var items = await GetAll();
            foreach (var item in items)
            {
                await Delete(item);
            }

        }

        private async Task<IQueryable<ManagedCertificate>> LoadAllManagedCertificates(ManagedCertificateFilter filter)
        {
            var managedCertificates = new List<ManagedCertificate>();

            var watch = Stopwatch.StartNew();

            // FIXME: this query should called only when absolutely required as the result set may be very large
            if (File.Exists(_dbPath))
            {
                var sql = "SELECT id, json FROM manageditem";

                if (filter?.PageIndex != null && filter?.PageSize != null)
                {
                    sql += $" LIMIT {filter.PageSize} OFFSET {filter.PageIndex}";
                    //sql += $" WHERE id NOT IN (SELECT id FROM manageditem ORDER BY id ASC LIMIT {filter.PageSize * filter.PageIndex}) ORDER BY id ASC LIMIT {filter.PageIndex}";
                }

                using (var db = new SQLiteConnection(_connectionString))
                using (var cmd = new SQLiteCommand(sql, db))
                {
                    await db.OpenAsync();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var itemId = (string)reader["id"];

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

                foreach (var site in managedCertificates)
                {
                    site.IsChanged = false;
                }

            }

            Debug.WriteLine($"LoadAllManagedCertificates[SQLite] took {watch.ElapsedMilliseconds}ms for {managedCertificates.Count} records");
            return managedCertificates.AsQueryable();
        }

        private async Task<bool> UpgradeSettings()
        {
            var appDataPath = Util.GetAppDataFolder(_storageSubFolder);

            var json = Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.json");
            var db = Path.Combine(appDataPath, $"{ITEMMANAGERCONFIG}.db");

            var managedCertificateList = new List<ManagedCertificate>();

            if (File.Exists(json) && !File.Exists(db))
            {
                var watch = Stopwatch.StartNew();

                // read managed sites using tokenize stream, this is useful for large files
                var serializer = new JsonSerializer();
                using (var sr = new StreamReader(json))
                using (var reader = new JsonTextReader(sr))
                {
                    managedCertificateList = serializer.Deserialize<List<ManagedCertificate>>(reader);

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

                    foreach (var site in managedCertificateList)
                    {
                        site.IsChanged = true;
                    }
                }

                await StoreAll(managedCertificateList); // upgrade to SQLite db storage
                File.Delete($"{json}.bak");
                File.Move(json, $"{json}.bak");
                Debug.WriteLine($"UpgradeSettings[Json->SQLite] took {watch.ElapsedMilliseconds}ms for {managedCertificateList.Count} records");
            }
            else
            {
                if (!File.Exists(db))
                {
                    // no setting to upgrade, create the empty database
                    await StoreAll(managedCertificateList);
                }
            }

            return true;
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
                    }
                    reader.Close();
                }
            }

            return managedCertificate;
        }

        public async Task<ManagedCertificate> GetById(string siteId)
        {
            return await LoadManagedCertificate(siteId);
        }

        public async Task<List<ManagedCertificate>> GetAll(ManagedCertificateFilter filter = null)
        {

            var items = await LoadAllManagedCertificates(filter);

            if (filter != null)
            {
                if (!string.IsNullOrEmpty(filter.Id))
                {
                    items = items.Where(i => i.Id.ToLowerInvariant().Trim() == filter.Id.ToLowerInvariant().Trim());
                }

                if (!string.IsNullOrEmpty(filter.Name))
                {
                    items = items.Where(i => i.Name.ToLowerInvariant().Trim() == filter.Name.ToLowerInvariant().Trim());
                }

                if (!string.IsNullOrEmpty(filter.Keyword))
                {
                    items = items.Where(i => i.Name.ToLowerInvariant().Contains(filter.Keyword.ToLowerInvariant()));
                }

                if (!string.IsNullOrEmpty(filter.ChallengeType))
                {
                    items = items.Where(i => i.RequestConfig.Challenges != null && i.RequestConfig.Challenges.Any(t => t.ChallengeType == filter.ChallengeType));
                }

                if (!string.IsNullOrEmpty(filter.ChallengeProvider))
                {
                    items = items.Where(i => i.RequestConfig.Challenges != null && i.RequestConfig.Challenges.Any(t => t.ChallengeProvider == filter.ChallengeProvider));
                }

                if (!string.IsNullOrEmpty(filter.StoredCredentialKey))
                {
                    items = items.Where(i => i.RequestConfig.Challenges != null && i.RequestConfig.Challenges.Any(t => t.ChallengeCredentialKey == filter.StoredCredentialKey));
                }

                //TODO: IncludeOnlyNextAutoRenew
                if (filter.MaxResults > 0)
                {
                    items = items.Take(filter.MaxResults);
                }
            }

            return items.ToList();
        }

        public async Task<ManagedCertificate> Update(ManagedCertificate managedCertificate)
        {
            if (managedCertificate == null)
            {
                return null;
            }

            if (managedCertificate.Id == null)
            {
                managedCertificate.Id = Guid.NewGuid().ToString();
            }

            await _retryPolicy.ExecuteAsync(async () =>
            {
                using (var db = new SQLiteConnection(_connectionString))
                {
                    await db.OpenAsync();
                    using (var tran = db.BeginTransaction())
                    {
                        using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO manageditem (id, json) VALUES (@id,@json)", db))
                        {
                            cmd.Parameters.Add(new SQLiteParameter("@id", managedCertificate.Id));
                            cmd.Parameters.Add(new SQLiteParameter("@json", JsonConvert.SerializeObject(managedCertificate, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore })));
           
                            await cmd.ExecuteNonQueryAsync();
                        }
                        tran.Commit();
                    }
                }

            });

            return managedCertificate;
        }

        public async Task Delete(ManagedCertificate site)
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
                }
            }
        }

        public async Task DeleteByName(string nameStartsWith)
        {
            var items = await LoadAllManagedCertificates(new ManagedCertificateFilter { Name = nameStartsWith });

            items = items.Where(i => i.Name.StartsWith(nameStartsWith));

            foreach (var item in items)
            {
                await Delete(item);
            }
        }
    }
}
