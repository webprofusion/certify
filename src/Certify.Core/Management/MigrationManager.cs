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
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Providers;
using Certify.Providers;

namespace Certify.Core.Management
{
    /// <summary>
    /// Perform/preview import and export
    /// </summary>
    public class MigrationManager
    {
        private const string EncryptionScheme = "default";
        private IManagedItemStore _itemManager;
        private ICredentialsManager _credentialsManager;
        private List<ITargetWebServer> _targetServers;
        private string _encryptionScheme = "default";

        public MigrationManager(IManagedItemStore itemManager, ICredentialsManager credentialsManager, List<ITargetWebServer> targetServers)
        {
            _itemManager = itemManager;
            _credentialsManager = credentialsManager;
            _targetServers = targetServers;
        }

        /// <summary>
        /// Export the managed certificates and related settings for the given filter
        /// </summary>
        /// <param name="filter">Filter to determine which certificates to export</param>
        /// <param name="settings">Export settings including encryption password</param>
        /// <param name="isPreview">If true, perform preview only without actual export</param>
        /// <returns>Package of exported settings</returns>
        public async Task<ImportExportPackage> PerformExport(ManagedCertificateFilter filter, ExportSettings settings, bool isPreview)
        {
            var salt = Guid.NewGuid().ToString();

            var export = new ImportExportPackage
            {
                SourceName = Environment.MachineName,
                ExportDate = DateTime.Now,
                SystemVersion = new SerializableVersion(Certify.Management.Util.GetAppVersion()),
                EncryptionSalt = salt,
                EncryptionValidation = new EncryptedContent
                {
                    Content = EncryptBytes(Encoding.UTF8.GetBytes("Secret"), settings.EncryptionSecret, salt),
                    Scheme = EncryptionScheme
                }
            };

            var managedCerts = await _itemManager.Find(filter);

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
            var usedCredentialsDict = new Dictionary<string, StoredCredential>();

            if (settings.ExportAllStoredCredentials)
            {
                foreach (var cred in allCredentials)
                {
                    if (cred.StorageKey != null && !usedCredentialsDict.ContainsKey(cred.StorageKey))
                    {
                        usedCredentialsDict[cred.StorageKey] = cred;
                    }
                }
            }
            else
            {
                foreach (var c in managedCerts)
                {
                    // gather credentials used by cert 
                    if (c.CertificatePasswordCredentialId?.AsNullWhenBlank() != null)
                    {
                        if (!usedCredentialsDict.ContainsKey(c.CertificatePasswordCredentialId))
                        {
                            var usedCredential = allCredentials.FirstOrDefault(a => a.StorageKey == c.CertificatePasswordCredentialId);
                            if (usedCredential != null)
                            {
                                usedCredentialsDict[c.CertificatePasswordCredentialId] = usedCredential;
                            }
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
                        var usedTaskCredentials = allTasks
                            .Select(t => t.ChallengeCredentialKey)
                            .Where(k => !string.IsNullOrEmpty(k))
                            .Distinct();

                        foreach (var credKey in usedTaskCredentials)
                        {
                            if (!usedCredentialsDict.ContainsKey(credKey))
                            {
                                var usedCredential = allCredentials.FirstOrDefault(u => u.StorageKey == credKey);
                                if (usedCredential != null)
                                {
                                    usedCredentialsDict[credKey] = usedCredential;
                                }
                            }
                        }
                    }
                }
            }

            // decrypt each used stored credential, re-encrypt and base64 encode secret
            foreach (var c in usedCredentialsDict.Values)
            {
                try
                {
                    var decrypted = await _credentialsManager.GetUnlockedCredential(c.StorageKey);
                    if (decrypted != null)
                    {
                        var encBytes = EncryptBytes(Encoding.UTF8.GetBytes(decrypted), settings.EncryptionSecret, export.EncryptionSalt);
                        c.Secret = Convert.ToBase64String(encBytes);
                    }
                }
                catch (Exception)
                {
                    // decryption failed - add to errors list without modifying the credential title
                    var originalTitle = c.Title;
                    export.Errors.Add($"Stored Credential [{originalTitle}] could not be decrypted for export. It may be owned by a different user.");
                    c.Secret = "";
                }
            }

            export.Content.StoredCredentials = usedCredentialsDict.Values.ToList();

            // Export custom certificate authorities if requested
            if (settings.ExportCustomCertificateAuthorities)
            {
                try
                {
                    var customCAs = SettingsManager.GetCustomCertificateAuthorities();
                    export.Content.CertificateAuthorities = customCAs.Where(ca => ca.IsCustom).ToList();
                }
                catch (Exception)
                {
                    export.Errors.Add("Failed to export custom Certificate Authorities. They may not be available on this system.");
                }
            }

            return export;
        }

        private IEnumerable<EncryptedContent> GetTaskScriptsAndContent(ObservableCollection<DeploymentTaskConfig> tasks, string secret, string salt)
        {
            var scriptsAndContent = new List<EncryptedContent>();
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                                if (File.Exists(p.Value) && processedFiles.Add(p.Value))
                                {
                                    try
                                    {
                                        var encryptedBytes = EncryptBytes(File.ReadAllBytes(p.Value), secret, salt);
                                        var content = new EncryptedContent { Filename = p.Value, Scheme = EncryptionScheme, Content = encryptedBytes };
                                        scriptsAndContent.Add(content);
                                    }
                                    catch (Exception exp)
                                    {
                                        // TODO: log errors and inform user - one or more script or file assets exists but is not readable
                                        System.Diagnostics.Debug.WriteLine("GetTaskScriptsAndContent: file content is not accessible - " + exp);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return scriptsAndContent;
        }

        private Aes GetAlg(string secret, string salt)
        {
#if NET9_0_OR_GREATER
            var saltBytes = Encoding.ASCII.GetBytes(salt);

            if (_encryptionScheme == "default")
            {
                // Legacy mode for backward compatibility with existing packages
#pragma warning disable SYSLIB0041 // Type or member is obsolete
                var key = new Rfc2898DeriveBytes(secret, saltBytes);
#pragma warning restore SYSLIB0041 // Type or member is obsolete

                var aesAlg = Aes.Create();
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);
                aesAlg.IV = key.GetBytes(aesAlg.BlockSize / 8);

                return aesAlg;
            }
            else
            {
                // Modern encryption with higher iteration count
                var key = new Rfc2898DeriveBytes(secret, saltBytes, 600000, HashAlgorithmName.SHA256);

                var aesAlg = Aes.Create();
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);
                aesAlg.IV = key.GetBytes(aesAlg.BlockSize / 8);

                return aesAlg;
            }
#else
            var saltBytes = Encoding.ASCII.GetBytes(salt);
            
            // Use stronger iteration count even on older .NET versions where possible
            var key = new Rfc2898DeriveBytes(secret, saltBytes, 100000, HashAlgorithmName.SHA256);

            var aesAlg = Aes.Create();
            aesAlg.Mode = CipherMode.CBC;
            aesAlg.Padding = PaddingMode.PKCS7;

            aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);
            aesAlg.IV = key.GetBytes(aesAlg.BlockSize / 8);

            return aesAlg;
#endif
        }

        public byte[] EncryptBytes(byte[] source, string secret, string salt)
        {
            using var rmCrypto = GetAlg(secret, salt);
            using var memoryStream = new MemoryStream();
            using var cryptoStream = new CryptoStream(memoryStream, rmCrypto.CreateEncryptor(), CryptoStreamMode.Write);
            cryptoStream.Write(source, 0, source.Length);
            cryptoStream.FlushFinalBlock();
            return memoryStream.ToArray();
        }

        public byte[] DecryptBytes(byte[] source, string secret, string salt)
        {
            using var rmCrypto = GetAlg(secret, salt);
            using var decryptor = rmCrypto.CreateDecryptor();
            using var memoryStream = new MemoryStream(source);
            using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            using var resultStream = new MemoryStream();
            cryptoStream.CopyTo(resultStream);

            return resultStream.ToArray();
        }

        /// <summary>
        /// Import managed certificates and related settings from an export package
        /// </summary>
        /// <param name="package">The import/export package to import from</param>
        /// <param name="settings">Import settings including encryption password</param>
        /// <param name="isPreviewMode">If true, perform preview only without actual import</param>
        /// <returns>List of action steps describing what was imported</returns>
        public async Task<List<ActionStep>> PerformImport(ImportExportPackage package, ImportSettings settings, bool isPreviewMode)
        {
            var steps = new List<ActionStep>();

            var currentAppVersion = Certify.Management.Util.GetAppVersion();

            if (currentAppVersion != package?.SystemVersion?.ToVersion())
            {
                if (package.SystemVersion == null || AppVersion.IsOtherVersionNewer(AppVersion.FromVersion(package.SystemVersion.ToVersion()), AppVersion.FromVersion(currentAppVersion)))
                {
                    steps.Add(new ActionStep { Title = "Version Check", Category = "Import", Key = "Version", HasWarning = true, Description = "This import uses a different app/system version." });
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
                var decryptionCheckString = Encoding.UTF8.GetString(decryptionCheckBytes).Trim('\0');
                if (decryptionCheckString != "Secret")
                {
                    decryptionFailed = true;
                }
            }
            catch (Exception)
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
                try
                {
                    var decodedBytes = Convert.FromBase64String(c.Secret);
                    var decryptedBytes = DecryptBytes(decodedBytes, settings.EncryptionSecret, package.EncryptionSalt);

                    // convert decrypted bytes to UTF8 string and trim NUL 
                    c.Secret = UTF8Encoding.UTF8.GetString(decryptedBytes).Trim('\0');

                    var existing = await _credentialsManager.GetCredential(c.StorageKey);

                    if (existing == null || settings.OverwriteExisting)
                    {
                        if (!isPreviewMode)
                        {
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
                            credentialImportSteps.Add(new ActionStep { Title = c.Title, Key = c.StorageKey });
                        }
                    }
                    else
                    {
                        credentialImportSteps.Add(new ActionStep { Title = c.Title, Key = c.StorageKey, HasWarning = true, Description = $"Credential already exists, it will not be re-imported." });
                    }
                }
                catch (Exception)
                {
                    credentialImportSteps.Add(new ActionStep { Title = c.Title, Key = c.StorageKey, HasWarning = true, Description = $"Credential could not be decrypted. Any items relying on this credential will fail until the credential is replaced." });
                    c.Secret = "";
                }
            }

            steps.Add(new ActionStep { Title = "Import Stored Credentials", Category = "Import", Substeps = credentialImportSteps, Key = "StoredCredentials", HasError = credentialImportSteps.Any(i => i.HasError), HasWarning = credentialImportSteps.Any(i => i.HasWarning) });

            var targetSiteBindings = new List<BindingInfo>();

            if (_targetServers != null)
            {
                foreach (var targetServer in _targetServers)
                {
                    if (await targetServer?.IsAvailable() == true)
                    {
                        targetSiteBindings.AddRange(await targetServer.GetSiteBindingList(false));
                    }
                }
            }

            // managed certs
            var managedCertImportSteps = new List<ActionStep>();
            foreach (var c in package.Content.ManagedCertificates)
            {
                var existing = await _itemManager.GetById(c.Id);
                if (existing == null || settings.OverwriteExisting)
                {
                    var deploymentMode = c.RequestConfig.DeploymentSiteOption;
                    var hasUnmatchedTargets = false;

                    var warningMsg = "";
                    if (deploymentMode == DeploymentOption.SingleSite)
                    {
                        var targets = targetSiteBindings.Where(t => t.SiteId == c.ServerSiteId);

                        if (targets.Any())
                        {
                            var unmatchedDomains = new List<string>();
                            foreach (var d in c.GetCertificateDomains())
                            {
                                var t = targets.FirstOrDefault(ta => ta.Host == d);

                                if (t == null)
                                {
                                    unmatchedDomains.Add(d);
                                    hasUnmatchedTargets = true;
                                    warningMsg += (string.IsNullOrEmpty(warningMsg) ? "Unmatched domains:" : ",") + " " + d;
                                }
                            }
                        }
                        else
                        {
                            hasUnmatchedTargets = true;
                            warningMsg = $"IIS SiteID {c.ServerSiteId} could not be matched for Single Site deployment mode. Deployment switched to Auto mode.";
                            c.RequestConfig.DeploymentSiteOption = DeploymentOption.Auto;
                        }
                    }

                    if (!isPreviewMode)
                    {
                        try
                        {
                            var result = await _itemManager.Update(c);
                            if (result != null)
                            {
                                managedCertImportSteps.Add(new ActionStep
                                {
                                    Title = c.Name,
                                    Key = c.Id,
                                    HasWarning = hasUnmatchedTargets,
                                    Description = hasUnmatchedTargets ? warningMsg : null
                                });
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
                        managedCertImportSteps.Add(new ActionStep
                        {
                            Title = c.Name,
                            Key = c.Id,
                            HasWarning = hasUnmatchedTargets,
                            Description = hasUnmatchedTargets ? warningMsg : null
                        });
                    }
                }
                else
                {
                    managedCertImportSteps.Add(new ActionStep { Title = c.Name, Key = c.Id, HasWarning = true, Description = "Item already exists, it will not be re-imported." });
                }
            }

            steps.Add(new ActionStep { Title = "Import Managed Certificates", Category = "Import", Substeps = managedCertImportSteps, Key = "ManagedCerts", HasError = managedCertImportSteps.Any(i => i.HasError), HasWarning = managedCertImportSteps.Any(i => i.HasWarning) });

            // certificate files
            var certFileImportSteps = new List<ActionStep>();
            foreach (var c in package.Content.CertificateFiles)
            {
                var pfxBytes = DecryptBytes(c.Content, settings.EncryptionSecret, package.EncryptionSalt);

                X509Certificate2? cert = null;

                try
                {
                    cert = new X509Certificate2(pfxBytes);
                }
                catch (Exception)
                {
                    // maybe we need a password
                    var managedCert = package.Content.ManagedCertificates.FirstOrDefault(m => m.CertificatePath == c.Filename && m.CertificatePasswordCredentialId?.AsNullWhenBlank() != null);
                    if (managedCert != null)
                    {
                        var cred = await _credentialsManager.GetUnlockedCredentialsDictionary(managedCert.CertificatePasswordCredentialId);
                        if (cred != null)
                        {
                            var pfxPwd = cred["password"];
                            try
                            {
                                cert = new X509Certificate2(pfxBytes, pfxPwd);
                            }
                            catch
                            {
                                // failed to load the provided cert, cert will remain null
                            }
                        }
                    }
                }

                if (cert != null)
                {
                    using (cert)
                    {
                        var isVerified = cert.Verify();
                        var managedCert = package.Content.ManagedCertificates.FirstOrDefault(m => m.CertificatePath == c.Filename);
                        string? pfxPath = null;

                        if (managedCert != null)
                        {
                            var primaryIdentifierPath = CertificateManager.GetPrimaryIdentifierAsPath(managedCert.RequestConfig, managedCert.Id);
                            var storePath = Path.GetFullPath(Path.Combine(new string[] { EnvironmentUtil.EnsuredAppDataPath(), "assets", primaryIdentifierPath }));

                            if (!isPreviewMode && !System.IO.Directory.Exists(storePath))
                            {
                                System.IO.Directory.CreateDirectory(storePath);
                            }

                            // Extract just the filename from the original path, handling both Windows and Unix path separators
                            var pfxFile = Path.GetFileName(c.Filename.Replace('\\', Path.DirectorySeparatorChar));
                            pfxPath = Path.Combine(storePath, pfxFile);
                        }

                        if (pfxPath != null && (!System.IO.File.Exists(pfxPath) || settings.OverwriteExisting))
                        {
                            if (!isPreviewMode)
                            {
                                try
                                {
                                    System.IO.File.WriteAllBytes(pfxPath, pfxBytes);

                                    // update managed cert to point to new path
                                    var item = await _itemManager.GetById(managedCert.Id);
                                    if (item != null && item.CertificatePath != pfxPath)
                                    {
                                        item.CertificatePath = pfxPath;
                                        await _itemManager.Update(item);
                                    }

                                    certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX {cert.Subject}, expiring {cert.NotAfter}", Key = c.Filename, HasWarning = !isVerified, Description = isVerified ? null : "Certificate did not pass verify check." });
                                }
                                catch (Exception exp)
                                {
                                    certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX {cert.Subject}, expiring {cert.NotAfter}", Key = c.Filename, HasError = true, Description = $"Failed to write certificate to destination: {pfxPath} [{exp.Message}]" });
                                }
                            }
                            else
                            {
                                certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX {cert.Subject}, expiring {cert.NotAfter}", Key = c.Filename, HasWarning = !isVerified, Description = isVerified ? $"Source path {c.Filename} would import to " + pfxPath : "Certificate did not pass verify check." });
                            }
                        }
                        else
                        {
                            certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX {cert.Subject}, expiring {cert.NotAfter}", Key = c.Filename, HasWarning = true, Description = $"Output file [{pfxPath}] already exists, it will not be re-imported" });
                        }
                    }
                }
                else
                {
                    certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX Failed", Key = c.Filename, HasWarning = true, Description = "Could not create PFX from bytes. Password may be incorrect." });
                }
            }

            steps.Add(new ActionStep { Title = "Import Certificate Files", Category = "Import", Substeps = certFileImportSteps, Key = "CertFiles", HasError = certFileImportSteps.Any(i => i.HasError), HasWarning = certFileImportSteps.Any(i => i.HasWarning) });

            // Import custom certificate authorities
            var caImportSteps = new List<ActionStep>();
            if (package.Content.CertificateAuthorities?.Any() == true)
            {
                try
                {
                    var existingCustomCAs = SettingsManager.GetCustomCertificateAuthorities();

                    foreach (var ca in package.Content.CertificateAuthorities)
                    {
                        // Only import custom CAs
                        if (!ca.IsCustom)
                        {
                            caImportSteps.Add(new ActionStep { Title = ca.Title, Key = ca.Id, HasWarning = true, Description = "Built-in Certificate Authority cannot be imported." });
                            continue;
                        }

                        var existingCA = existingCustomCAs.FirstOrDefault(c => c.Id == ca.Id);

                        if (existingCA == null || settings.OverwriteExisting)
                        {
                            if (!isPreviewMode)
                            {
                                try
                                {
                                    if (existingCA != null)
                                    {
                                        existingCustomCAs.Remove(existingCA);
                                    }

                                    existingCustomCAs.Add(ca);

                                    if (SettingsManager.SaveCustomCertificateAuthorities(existingCustomCAs))
                                    {
                                        caImportSteps.Add(new ActionStep { Title = ca.Title, Key = ca.Id, Description = existingCA != null ? "Updated existing CA" : null });
                                    }
                                    else
                                    {
                                        caImportSteps.Add(new ActionStep { Title = ca.Title, Key = ca.Id, HasError = true, Description = "Failed to save Certificate Authority." });
                                    }
                                }
                                catch (Exception exp)
                                {
                                    caImportSteps.Add(new ActionStep { Title = ca.Title, Key = ca.Id, HasError = true, Description = $"Failed to import Certificate Authority: {exp.Message}" });
                                }
                            }
                            else
                            {
                                caImportSteps.Add(new ActionStep { Title = ca.Title, Key = ca.Id, Description = existingCA != null ? "Would update existing CA" : null });
                            }
                        }
                        else
                        {
                            caImportSteps.Add(new ActionStep { Title = ca.Title, Key = ca.Id, HasWarning = true, Description = "Certificate Authority already exists, it will not be re-imported." });
                        }
                    }
                }
                catch (Exception exp)
                {
                    caImportSteps.Add(new ActionStep { Title = "Certificate Authorities", Key = "CAs", HasError = true, Description = $"Failed to import Certificate Authorities: {exp.Message}" });
                }
            }

            steps.Add(new ActionStep { Title = "Import Certificate Authorities", Category = "Import", Substeps = caImportSteps, Key = "CertificateAuthorities", HasError = caImportSteps.Any(i => i.HasError), HasWarning = caImportSteps.Any(i => i.HasWarning) });

            return steps;
        }
    }
}
