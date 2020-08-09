using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Config.Migration;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management
{


    /// <summary>
    /// Perform/preview import and export
    /// </summary>
    public class MigrationManager
    {
        private const string EncryptionScheme = "Default";
        private IItemManager _itemManager;
        private ICredentialsManager _credentialsManager;
        private ICertifiedServer _targetServer;

        public MigrationManager(IItemManager itemManager, ICredentialsManager credentialsManager, ICertifiedServer targetServer)
        {
            _itemManager = itemManager;
            _credentialsManager = credentialsManager;
            _targetServer = targetServer;
        }

        /// <summary>
        /// Export the managed certificates and related settings for the given filter
        /// </summary>
        /// <param name="filter"></param>
        /// <returns>Package of exported settings</returns>
        public async Task<ImportExportPackage> PerformExport(ManagedCertificateFilter filter, ExportSettings settings, bool isPreview)
        {
            var salt = Guid.NewGuid().ToString();

            var export = new ImportExportPackage
            {
                SourceName = Environment.MachineName,
                ExportDate = DateTime.Now,
                SystemVersion = Certify.Management.Util.GetAppVersion(),
                EncryptionSalt = salt,
                EncryptionValidation = new EncryptedContent
                {
                    Content = EncryptBytes(Encoding.ASCII.GetBytes("Secret"), settings.EncryptionSecret, salt),
                    Scheme = EncryptionScheme
                }
            };

            // export managed certs, related certificate files, stored credentials

            // deployment tasks with local script or path references will need to copy the scripts separately. Need a summary of items to copy.

            var managedCerts = await _itemManager.GetAll(filter);

            export.Content = new ImportExportContent
            {
                ManagedCertificates = managedCerts,
                CertificateFiles = new List<EncryptedContent>(),
                Scripts = new List<EncryptedContent>(),
                CertificateAuthorities = new List<CertificateAuthority>(),
                StoredCredentials = new List<StoredCredential>()
            };


            // for each managed cert, export the current certificate files (if present)
            foreach (var c in managedCerts)
            {
                if (!string.IsNullOrEmpty(c.CertificatePath) && System.IO.File.Exists(c.CertificatePath))
                {
                    var certBytes = System.IO.File.ReadAllBytes(c.CertificatePath);

                    var encryptedBytes = EncryptBytes(certBytes, settings.EncryptionSecret, export.EncryptionSalt);
                    var content = new EncryptedContent { Filename = c.CertificatePath, Scheme = EncryptionScheme, Content = encryptedBytes };

                    export.Content.CertificateFiles.Add(content);
                }

                if (c.PreRequestTasks?.Any() == true)
                {
                    export.Content.Scripts.AddRange(GetTaskScriptsAndContent(c.PreRequestTasks, settings.EncryptionSecret, export.EncryptionSalt));
                }

                if (c.PostRequestTasks?.Any() == true)
                {
                    export.Content.Scripts.AddRange(GetTaskScriptsAndContent(c.PostRequestTasks, settings.EncryptionSecret, export.EncryptionSalt));
                }
            }


            // for each managed cert, check used stored credentials (DNS challenges or deployment tasks)
            var allCredentials = await _credentialsManager.GetCredentials();
            var usedCredentials = new List<StoredCredential>();

            if (settings.ExportAllStoredCredentials)
            {
                usedCredentials.AddRange(allCredentials);
            }
            else
            {
                foreach (var c in managedCerts)
                {
                    // gather credentials used by cert 
                    if (c.CertificatePasswordCredentialId != null)
                    {
                        if (!usedCredentials.Any(u => u.StorageKey == c.CertificatePasswordCredentialId))
                        {
                            usedCredentials.Add(allCredentials.Find(a => a.StorageKey == c.CertificatePasswordCredentialId));
                        }
                    }

                    // gather credentials used by tasks
                    var allTasks = new List<Config.DeploymentTaskConfig>();

                    if (c.PreRequestTasks != null)
                    {
                        allTasks.AddRange(c.PreRequestTasks);
                    }

                    if (c.PostRequestTasks != null)
                    {
                        allTasks.AddRange(c.PostRequestTasks);
                    }

                    if (allTasks.Any())
                    {

                        /*var usedTaskCredentials = allTasks
                            .SelectMany(t => t.Parameters?.Select(p => p.Value))
                            .Distinct()
                            .Where(t => allCredentials.Any(ac => ac.StorageKey == t))
                            .ToList();*/
                        var usedTaskCredentials = allTasks
                            .Select(t => t.ChallengeCredentialKey)
                            .Distinct()
                            .Where(t => allCredentials.Any(ac => ac.StorageKey == t));

                        foreach (var used in usedTaskCredentials)
                        {
                            if (!usedCredentials.Any(u => u.StorageKey == used))
                            {
                                usedCredentials.Add(allCredentials.FirstOrDefault(u => u.StorageKey == used));
                            }
                        }
                    }
                }
            }

            // decrypt each used stored credential, re-encrypt and base64 encode secret
            foreach (var c in usedCredentials)
            {
                var decrypted = await _credentialsManager.GetUnlockedCredential(c.StorageKey);
                if (decrypted != null)
                {
                    var encBytes = EncryptBytes(Encoding.UTF8.GetBytes(decrypted), settings.EncryptionSecret, export.EncryptionSalt);
                    c.Secret = Convert.ToBase64String(encBytes);
                }
            }

            export.Content.StoredCredentials = usedCredentials;

            // for each managed cert, check and summarise used local scripts

            // copy acme-dns settings

            // export acme accounts?
            return export;
        }

        private IEnumerable<EncryptedContent> GetTaskScriptsAndContent(ObservableCollection<DeploymentTaskConfig> tasks, string secret, string salt)
        {
            var scriptsAndContent = new List<EncryptedContent>();
            if (tasks?.Any() == true)
            {
                foreach (var t in tasks)
                {
                    foreach (var p in t.Parameters)
                    {
                        if (!string.IsNullOrEmpty(p.Value))
                        {
                            if (p.Value.IndexOfAny(Path.GetInvalidPathChars()) == -1)
                            {
                                if (File.Exists(p.Value))
                                {
                                    var encryptedBytes = EncryptBytes(File.ReadAllBytes(p.Value), secret, salt);
                                    var content = new EncryptedContent { Filename = p.Value, Scheme = EncryptionScheme, Content = encryptedBytes };
                                    scriptsAndContent.Add(content);
                                }
                            }
                        }
                    }
                }
            }
            return scriptsAndContent;
        }

        private RijndaelManaged GetAlg(string secret, string salt)
        {
            var saltBytes = Encoding.ASCII.GetBytes(salt);
            var key = new Rfc2898DeriveBytes(secret, saltBytes);

            var aesAlg = new RijndaelManaged();
            aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);
            aesAlg.IV = key.GetBytes(aesAlg.BlockSize / 8);

            return aesAlg;
        }

        private byte[] EncryptBytes(byte[] source, string secret, string salt)
        {
            var rmCrypto = GetAlg(secret, salt);

            rmCrypto.Padding = PaddingMode.PKCS7;
            using (var memoryStream = new MemoryStream())
            using (var cryptoStream = new CryptoStream(memoryStream, rmCrypto.CreateEncryptor(rmCrypto.Key, rmCrypto.IV), CryptoStreamMode.Write))
            {
                cryptoStream.Write(source, 0, source.Length);
                cryptoStream.FlushFinalBlock();
                cryptoStream.Close();
                return memoryStream.ToArray();
            }
        }

        private byte[] DecryptBytes(byte[] source, string secret, string salt)
        {
            using (var rmCrypto = GetAlg(secret, salt))
            {
                rmCrypto.Padding = PaddingMode.PKCS7;

                using (var decryptor = rmCrypto.CreateDecryptor(rmCrypto.Key, rmCrypto.IV))
                {
                    using (var memoryStream = new MemoryStream(source))
                    {
                        using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                        {
                            var decryptedBytes = new byte[source.Length];
                            var decryptedByteCount = cryptoStream.Read(decryptedBytes, 0, decryptedBytes.Length);
                            memoryStream.Close();
                            cryptoStream.Close();

                            return decryptedBytes;
                        }
                    }
                }

            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="package"></param>
        /// <param name="isPreviewMode"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> PerformImport(ImportExportPackage package, ImportSettings settings, bool isPreviewMode)
        {
            // apply import
            var steps = new List<ActionStep>();

            // import managed certs, certificate files, stored credentials, CAs
            var currentAppVersion = Certify.Management.Util.GetAppVersion();

            if (currentAppVersion != package.SystemVersion)
            {
                if (package.SystemVersion == null || AppVersion.IsOtherVersionNewer(AppVersion.FromVersion(package.SystemVersion), AppVersion.FromVersion(currentAppVersion)))
                {
                    steps.Add(new ActionStep { Title = "Version Check", Category = "Import", Key = "Version", HasWarning = true, Description = "Migration to an older app version is not supported. Results may be unreliable." });

                }
            }
            else
            {
                steps.Add(new ActionStep { Title = "Version Check", Category = "Import", Key = "Version", Description = "Source is from the same version or a supported app version." });
            }

            // check encryption
            var decryptionFailed = false;
            try
            {
                var decryptionCheckBytes = DecryptBytes(package.EncryptionValidation.Content, settings.EncryptionSecret, package.EncryptionSalt);
                var decryptionCheckString = Encoding.ASCII.GetString(decryptionCheckBytes).Trim('\0');
                if (decryptionCheckString != "Secret")
                {
                    // failed decryption
                    decryptionFailed = true;
                }
            }
            catch (Exception exp)
            {
                decryptionFailed = true;
            }

            if (decryptionFailed)
            {
                steps.Add(new ActionStep { HasError = true, Title = "Decryption Check", Category = "Import", Key = "Decrypt", Description = "Secrets cannot be decrypted using the provided password." });
                return steps;
            }
            else
            {
                steps.Add(new ActionStep { Title = "Decryption Check", Category = "Import", Key = "Decrypt", Description = "Secrets can be decrypted OK using the provided password." });

            }


            // stored credentials
            var credentialImportSteps = new List<ActionStep>();
            foreach (var c in package.Content.StoredCredentials)
            {
                var decodedBytes = Convert.FromBase64String(c.Secret);
                var decryptedBytes = DecryptBytes(decodedBytes, settings.EncryptionSecret, package.EncryptionSalt);

                // convert decrypted bytes to UTF8 string and trim NUL 
                c.Secret = UTF8Encoding.UTF8.GetString(decryptedBytes).Trim('\0');

                var existing = await _credentialsManager.GetCredential(c.StorageKey);

                if (existing == null)
                {
                    if (!isPreviewMode)
                    {
                        // perform import
                        var result = await _credentialsManager.Update(c);
                        if (result != null)
                        {
                            credentialImportSteps.Add(new ActionStep { Title = c.Title, Key = c.StorageKey });
                        }
                        else
                        {
                            credentialImportSteps.Add(new ActionStep { Title = c.Title, Key = c.StorageKey, HasWarning = true, Description = $"Failed to store this credential. Items which depend on it may not function." });
                        }
                    }
                    else
                    {
                        // preview only
                        credentialImportSteps.Add(new ActionStep { Title = c.Title, Key = c.StorageKey });
                    }
                }
                else
                {
                    // credential already exists
                    credentialImportSteps.Add(new ActionStep { Title = c.Title, Key = c.StorageKey, HasWarning = true, Description = $"Credential already exists, it will not be re-imported." });
                }

            }

            steps.Add(new ActionStep { Title = "Import Stored Credentials", Category = "Import", Substeps = credentialImportSteps, Key = "StoredCredentials" });


            var targetSiteBindings = new List<BindingInfo>();
            if (await _targetServer?.IsAvailable() == true)
            {
                targetSiteBindings = await _targetServer.GetSiteBindingList(false);
            }

            // managed certs
            var managedCertImportSteps = new List<ActionStep>();
            foreach (var c in package.Content.ManagedCertificates)
            {
                var existing = await _itemManager.GetById(c.Id);
                if (existing == null)
                {

                    // check if item is auto deployment or single site, if single site warn if we don't have an exact match (convert to Auto)
                    DeploymentOption deploymentMode = c.RequestConfig.DeploymentSiteOption;
                    bool hasUnmatchedTargets = false;
                    bool siteIdChanged = false;
                    bool deploymentModeChanged = false;

                    var warningMsg = "";
                    if (deploymentMode == DeploymentOption.SingleSite)
                    {
                        var targets = targetSiteBindings.Where(t => t.SiteId == c.ServerSiteId);

                        if (targets.Any())
                        {
                            //exact match on site id, check domains

                            var unmatchedDomains = new List<string>();
                            foreach (var d in c.GetCertificateDomains())
                            {
                                var t = targets.FirstOrDefault(ta => ta.Host == d);

                                if (t == null)
                                {
                                    unmatchedDomains.Add(d);
                                    hasUnmatchedTargets = true;
                                    warningMsg += " " + d;
                                }
                            }
                        }
                        else
                        {
                            // no exact site id match, check if a different site is an exact match, if so migrate site id

                            // if no exact match, change to auto 
                        }
                    }
                    else
                    {
                        // auto deploy, site id only used for IIS site selection in UI

                    }

                    if (!isPreviewMode)
                    {
                        // perform actual import
                        try
                        {
                            // TODO : re-map certificate pfx path, could be a different location on this instance
                            // warn if deployment task script paths don't match an existing file?

                            // TODO : warn if Certificate Authority ID does not match one we have (cert renewal will fail)

                            var result = await _itemManager.Update(c);
                            if (result != null)
                            {
                                managedCertImportSteps.Add(new ActionStep { Title = c.Name, Key = c.Id, HasWarning = (hasUnmatchedTargets || siteIdChanged) });
                            }
                            else
                            {
                                managedCertImportSteps.Add(new ActionStep { Title = c.Name, Key = c.Id, HasError = true, Description = $"Failed to import item." });
                            }
                        }
                        catch (Exception exp)
                        {
                            managedCertImportSteps.Add(new ActionStep { Title = c.Name, Key = c.Id, HasError = true, Description = $"Failed to import item: {exp.Message}" });
                        }
                    }
                    else
                    {
                        // preview only
                        managedCertImportSteps.Add(new ActionStep { Title = c.Name, Key = c.Id });
                    }
                }
                else
                {
                    managedCertImportSteps.Add(new ActionStep { Title = c.Name, Key = c.Id, HasWarning = true, Description = "Item already exists, it will not be re-imported." });
                }

            }

            steps.Add(new ActionStep { Title = "Import Managed Certificates", Category = "Import", Substeps = managedCertImportSteps, Key = "ManagedCerts" });

            // certificate files
            var certFileImportSteps = new List<ActionStep>();
            foreach (var c in package.Content.CertificateFiles)
            {
                var pfxBytes = DecryptBytes(c.Content, settings.EncryptionSecret, package.EncryptionSalt);

                X509Certificate2 cert = null;

                try
                {
                    cert = new X509Certificate2(pfxBytes);
                }
                catch (Exception)
                {
                    // maybe we need a password
                    var managedCert = package.Content.ManagedCertificates.FirstOrDefault(m => m.CertificatePath == c.Filename && m.CertificatePasswordCredentialId != null);
                    if (managedCert != null)
                    {
                        //get stored cred
                        var cred = await _credentialsManager.GetUnlockedCredentialsDictionary(managedCert.CertificatePasswordCredentialId);
                        if (cred != null)
                        {
                            var pfxPwd = cred["password"];
                            cert = new X509Certificate2(pfxBytes, pfxPwd);
                        }
                    }
                }

                if (cert != null)
                {

                    bool isVerified = cert.Verify();

                    if (!System.IO.File.Exists(c.Filename))
                    {

                        if (!isPreviewMode)
                        {
                            // perform actual import
                            try
                            {
                                System.IO.File.WriteAllBytes(c.Filename, c.Content);
                                certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX {cert.Subject}, expiring {cert.NotAfter}", Key = c.Filename, HasWarning = !isVerified, Description = isVerified ? null : "Certificate did not pass verify check." });
                            }
                            catch (Exception exp)
                            {
                                certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX {cert.Subject}, expiring {cert.NotAfter}", Key = c.Filename, HasError = true, Description = $"Failed to write certificate to destination: {c.Filename} [{exp.Message}]" });
                            }
                        }
                        else
                        {
                            // preview only
                            certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX {cert.Subject}, expiring {cert.NotAfter}", Key = c.Filename, HasWarning = !isVerified, Description = isVerified ? "Would import to " + c.Filename : "Certificate did not pass verify check." });
                        }
                    }
                    else
                    {
                        certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX {cert.Subject}, expiring {cert.NotAfter}", Key = c.Filename, HasWarning = true, Description = "Output file already exists, it will not be re-imported" });
                    }
                }
                else
                {
                    certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX Failed", Key = c.Filename, HasWarning = true, Description = "Could not create PFX from bytes. Password may be incorrect." });

                }

            }

            steps.Add(new ActionStep { Title = "Import Certificate Files", Category = "Import", Substeps = certFileImportSteps, Key = "CertFiles" });

            
            return steps;
        }
    }
}
