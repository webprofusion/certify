using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Certify.Core.Management.Challenges;
using Certify.Models.Config;
using Newtonsoft.Json;

namespace Certify.Management
{


    public class CredentialsManager : ICredentialsManager
    {
        public const string CREDENTIALSTORE = "cred";

        public string StorageSubfolder = "credentials"; //if specified will be appended to AppData path as subfolder to load/save to
        private const string PROTECTIONENTROPY = "Certify.Credentials";
        private bool _useWindowsNativeFeatures = true;

        public CredentialsManager(bool useWindowsNativeFeatures = true)
        {
            _useWindowsNativeFeatures = useWindowsNativeFeatures;
        }
        private string GetDbPath()
        {
            var appDataPath = Util.GetAppDataFolder(StorageSubfolder);
            return Path.Combine(appDataPath, $"{CREDENTIALSTORE}.db");
        }

        /// <summary>
        /// Delete credential by key. This will fail if the credential is currently in use. 
        /// </summary>
        /// <param name="storageKey"></param>
        /// <returns></returns>
        public async Task<bool> Delete(string storageKey)
        {
            var inUse = await IsCredentialInUse(storageKey);

            if (!inUse)
            {
                //delete credential in database
                var path = GetDbPath();

                if (File.Exists(path))
                {
                    using (var db = new SQLiteConnection($"Data Source={path}"))
                    {
                        await db.OpenAsync();
                        using (var tran = db.BeginTransaction())
                        {
                            using (var cmd = new SQLiteCommand("DELETE FROM credential WHERE id=@id", db))
                            {
                                cmd.Parameters.Add(new SQLiteParameter("@id", storageKey));
                                await cmd.ExecuteNonQueryAsync();
                            }
                            tran.Commit();
                        }
                        db.Close();
                    }
                }

                return true;
            }
            else
            {
                //could not delete
                return false;
            }
        }

        public async Task<ActionResult> TestCredentials(string storageKey)
        {
            // create instance of provider type then test credentials
            var storedCredential = await GetCredential(storageKey);

            if (storedCredential == null)
            {
                return new ActionResult { IsSuccess = false, Message = "No credentials found." };
            }

            var credentials = await GetUnlockedCredentialsDictionary(storedCredential.StorageKey);

            if (credentials == null)
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to retrieve decrypted credentials." };
            }

            if (storedCredential.ProviderType.StartsWith("DNS"))
            {
                try
                {
                    var dnsProvider = await ChallengeProviders.GetDnsProvider(storedCredential.ProviderType, credentials, new Dictionary<string, string> { });

                    if (dnsProvider == null)
                    {
                        return new ActionResult { IsSuccess = false, Message = "Could not create DNS provider API. Invalid or unrecognised." };
                    }

                    return await dnsProvider.Test();
                }
                catch (Exception exp)
                {
                    return new ActionResult { IsSuccess = false, Message = "Failed to init DNS Provider " + storedCredential.ProviderType + " :: " + exp.Message };
                }
            }
            else
            {
                return new ActionResult { IsSuccess = true, Message = "No test available." };
            }
        }

        public async Task<bool> IsCredentialInUse(string storageKey)
        {
            // TODO: inject item manager or move this check out to certify manager
            var managedCertificates = await new ItemManager().GetAll(new Models.ManagedCertificateFilter { StoredCredentialKey = storageKey });
            if (managedCertificates.Any())
            {
                // credential is in use
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Get protected version of a secret 
        /// </summary>
        /// <param name="clearText"></param>
        /// <param name="optionalEntropy"></param>
        /// <param name="scope"></param>
        /// <returns></returns>
        private string Protect(
                string clearText,
                string optionalEntropy = null,
                DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            // https://www.thomaslevesque.com/2013/05/21/an-easy-and-secure-way-to-store-a-password-using-data-protection-api/

            if (clearText == null)
            {
                return null;
            }

            if (_useWindowsNativeFeatures)
            {

                var clearBytes = Encoding.UTF8.GetBytes(clearText);
                var entropyBytes = string.IsNullOrEmpty(optionalEntropy)
                    ? null
                    : Encoding.UTF8.GetBytes(optionalEntropy);
                var encryptedBytes = ProtectedData.Protect(clearBytes, entropyBytes, scope);
                return Convert.ToBase64String(encryptedBytes);
            }
            else
            {
                // TODO: dummy implementation, require alternative implementation for non-windows
               return  Convert.ToBase64String(Encoding.UTF8.GetBytes(clearText).Reverse().ToArray());
            }
        }

        /// <summary>
        /// Get unprotected version of a secret 
        /// </summary>
        /// <param name="encryptedText"></param>
        /// <param name="optionalEntropy"></param>
        /// <param name="scope"></param>
        /// <returns></returns>
        private string Unprotect(
            string encryptedText,
            string optionalEntropy = null,
            DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            // https://www.thomaslevesque.com/2013/05/21/an-easy-and-secure-way-to-store-a-password-using-data-protection-api/

            if (encryptedText == null)
            {
                throw new ArgumentNullException("encryptedText");
            }

            if (_useWindowsNativeFeatures)
            {
                var encryptedBytes = Convert.FromBase64String(encryptedText);
                var entropyBytes = string.IsNullOrEmpty(optionalEntropy)
                    ? null
                    : Encoding.UTF8.GetBytes(optionalEntropy);
                var clearBytes = ProtectedData.Unprotect(encryptedBytes, entropyBytes, scope);
                return Encoding.UTF8.GetString(clearBytes);
            } else
            {

                // TODO: dummy implementation, implement alternative implementation for non-windows
                var bytes = Convert.FromBase64String(encryptedText);
                return Encoding.UTF8.GetString(bytes.Reverse().ToArray());
            }
        }

        /// <summary>
        /// Return summary list of stored credentials (excluding secrets) for given type 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public async Task<List<StoredCredential>> GetCredentials(string type = null)
        {
            var path = GetDbPath();

            if (File.Exists(path))
            {
                var credentials = new List<StoredCredential>();

                using (var db = new SQLiteConnection($"Data Source={path}"))
                {
                    await db.OpenAsync();
                    using (var cmd = new SQLiteCommand("SELECT id, json FROM credential", db))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var storedCredential = JsonConvert.DeserializeObject<StoredCredential>((string)reader["json"]);
                            if (string.IsNullOrEmpty(type) || type == storedCredential.ProviderType)
                            {
                                credentials.Add(storedCredential);
                            }
                        }
                    }
                    db.Close();
                }

                return credentials;
            }
            else
            {
                return new List<StoredCredential>();
            }
        }

        public async Task<StoredCredential> GetCredential(string storageKey)
        {
            var credentials = await GetCredentials();
            return credentials.FirstOrDefault(c => c.StorageKey == storageKey);
        }

        public async Task<string> GetUnlockedCredential(string storageKey)
        {
            string protectedString = null;

            var path = GetDbPath();

            //load protected string from db
            if (File.Exists(path))
            {
                using (var db = new SQLiteConnection($"Data Source={path}"))
                using (var cmd = new SQLiteCommand("SELECT json, protectedvalue FROM credential WHERE id=@id", db))
                {
                    cmd.Parameters.Add(new SQLiteParameter("@id", storageKey));

                    db.Open();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var storedCredential = JsonConvert.DeserializeObject<StoredCredential>((string)reader["json"]);
                            protectedString = (string)reader["protectedvalue"];
                        }
                    }
                    db.Close();
                }
            }
            try
            {
                return Unprotect(protectedString, PROTECTIONENTROPY, DataProtectionScope.CurrentUser);
            }
            catch (Exception exp)
            {
                throw new AggregateException($"Failed to decrypt Credential [{storageKey}] - it was most likely created by a different user account.", exp);
            }
        }


        public async Task<Dictionary<string, string>> GetUnlockedCredentialsDictionary(string storageKey)
        {
            try
            {
                var val = await GetUnlockedCredential(storageKey);

                return JsonConvert.DeserializeObject<Dictionary<string, string>>(val);
            }
            catch (Exception)
            {
                // failed to decrypt or credential inaccessible
                return null;
            }
        }

        public async Task<StoredCredential> Update(StoredCredential credentialInfo)
        {
            if (credentialInfo.Secret == null)
            {
                return null;
            }

            credentialInfo.DateCreated = DateTime.Now;

            var protectedContent = Protect(credentialInfo.Secret, PROTECTIONENTROPY, DataProtectionScope.CurrentUser);

            credentialInfo.Secret = "protected";

            var path = GetDbPath();

            //create database if it doesn't exist
            if (!File.Exists(path))
            {
                try
                {
                    using (var db = new SQLiteConnection($"Data Source={path}"))
                    {
                        await db.OpenAsync();
                        using (var cmd = new SQLiteCommand("CREATE TABLE credential (id TEXT NOT NULL UNIQUE PRIMARY KEY, json TEXT NOT NULL, protectedvalue TEXT NOT NULL)", db))
                        {
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch (SQLiteException)
                {
                    // already exists
                }
            }

            // save new/modified item into credentials database
            using (var db = new SQLiteConnection($"Data Source={path}"))
            {
                await db.OpenAsync();
                using (var tran = db.BeginTransaction())
                {
                    using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO credential (id, json, protectedvalue) VALUES (@id, @json, @protectedvalue)", db))
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@id", credentialInfo.StorageKey));
                        cmd.Parameters.Add(new SQLiteParameter("@json", JsonConvert.SerializeObject(credentialInfo)));
                        cmd.Parameters.Add(new SQLiteParameter("@protectedvalue", protectedContent));
                        await cmd.ExecuteNonQueryAsync();
                    }

                    tran.Commit();
                }
                db.Close();
            }
            return credentialInfo;
        }
    }
}
