﻿using System;
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
using Certify.Providers;

namespace Certify.Core.Management
{
    /// <summary>
    /// Perform/preview import and export
    /// </summary>
    public class MigrationManager
    {
        private const string EncryptionScheme = "Default";
        private IManagedItemStore _itemManager;
        private ICredentialsManager _credentialsManager;
        private List<ITargetWebServer> _targetServers;

        public MigrationManager(IManagedItemStore itemManager, ICredentialsManager credentialsManager, List<ITargetWebServer> targetServers)
        {
            _itemManager = itemManager;
            _credentialsManager = credentialsManager;
            _targetServers = targetServers;
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
                SystemVersion = new SerializableVersion(Certify.Management.Util.GetAppVersion()),
                EncryptionSalt = salt,
                EncryptionValidation = new EncryptedContent
                {
                    Content = EncryptBytes(Encoding.ASCII.GetBytes("Secret"), settings.EncryptionSecret, salt),
                    Scheme = EncryptionScheme
                }
            };

            // export managed certs, related certificate files, stored credentials

            // deployment tasks with local script or path references will need to copy the scripts separately. Need a summary of items to copy.

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
                            var usedCredential = allCredentials.FirstOrDefault(a => a.StorageKey == c.CertificatePasswordCredentialId);
                            if (usedCredential != null)
                            {
                                usedCredentials.Add(usedCredential);
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
                            .Distinct()
                            .Where(t => allCredentials.Any(ac => ac.StorageKey == t));

                        foreach (var used in usedTaskCredentials)
                        {
                            if (used != null)
                            {
                                var usedCredential = allCredentials.FirstOrDefault(u => u.StorageKey == used);
                                if (usedCredential != null)
                                {
                                    if (!usedCredentials.Any(u => u.StorageKey == used))
                                    {
                                        usedCredentials.Add(usedCredential);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // decrypt each used stored credential, re-encrypt and base64 encode secret
            foreach (var c in usedCredentials)
            {
                try
                {
                    var decrypted = await _credentialsManager.GetUnlockedCredential(c?.StorageKey);
                    if (decrypted != null)
                    {
                        var encBytes = EncryptBytes(Encoding.UTF8.GetBytes(decrypted), settings.EncryptionSecret, export.EncryptionSalt);
                        c.Secret = Convert.ToBase64String(encBytes);
                    }
                }
                catch (Exception)
                {
                    // decryption failed
                    c.Title += " [Update Required. Decryption Failed]";
                    c.Secret = "";
                    export.Errors.Add($"Stored Credential [{c.Title}] could not be decrypted for export. It may be owned by a different user.");
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
            var saltBytes = Encoding.ASCII.GetBytes(salt);
            var key = new Rfc2898DeriveBytes(secret, saltBytes);

            var aesAlg = Aes.Create();
            aesAlg.Mode = CipherMode.CBC;
            aesAlg.Padding = PaddingMode.PKCS7;

            aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);
            aesAlg.IV = key.GetBytes(aesAlg.BlockSize / 8);

            return aesAlg;
        }

        public byte[] EncryptBytes(byte[] source, string secret, string salt)
        {
            using (var rmCrypto = GetAlg(secret, salt))
            {
                using (var memoryStream = new MemoryStream())
                using (var cryptoStream = new CryptoStream(memoryStream, rmCrypto.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cryptoStream.Write(source, 0, source.Length);
                    cryptoStream.FlushFinalBlock();
                    cryptoStream.Close();
                    return memoryStream.ToArray();
                }
            }
        }

        public byte[] DecryptBytes(byte[] source, string secret, string salt)
        {
            using (var rmCrypto = GetAlg(secret, salt))
            {
                using (var decryptor = rmCrypto.CreateDecryptor())
                {
                    using (var memoryStream = new MemoryStream(source))
                    {
                        using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                        {
                            var decryptedBytes = new byte[source.Length];

                            var totalRead = 0;
                            while (totalRead < source.Length)
                            {
                                var bytesRead = cryptoStream.Read(decryptedBytes, totalRead, source.Length - totalRead);
                                if (bytesRead == 0)
                                {
                                    break;
                                }

                                totalRead += bytesRead;
                            }

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
                var decryptionCheckString = Encoding.ASCII.GetString(decryptionCheckBytes).Trim('\0');
                if (decryptionCheckString != "Secret")
                {
                    // failed decryption
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
                catch (Exception)
                {
                    credentialImportSteps.Add(new ActionStep { Title = c.Title, Key = c.StorageKey, HasWarning = true, Description = $"Credential could not be decrypted. Any items relying on this credential will fail until the credential is replaced." });
                    c.Secret = "";
                }
            }

            steps.Add(new ActionStep { Title = "Import Stored Credentials", Category = "Import", Substeps = credentialImportSteps, Key = "StoredCredentials" });

            var targetSiteBindings = new List<BindingInfo>();
            foreach (var targetServer in _targetServers)
            {
                if (await targetServer?.IsAvailable() == true)
                {
                    targetSiteBindings.AddRange(await targetServer.GetSiteBindingList(false));
                }
            }

            // managed certs
            var managedCertImportSteps = new List<ActionStep>();
            foreach (var c in package.Content.ManagedCertificates)
            {
                var existing = await _itemManager.GetById(c.Id);
                if (existing == null || settings.OverwriteExisting)
                {

                    // check if item is auto deployment or single site, if single site warn if we don't have an exact match (convert to Auto)
                    var deploymentMode = c.RequestConfig.DeploymentSiteOption;
                    var hasUnmatchedTargets = false;
                    var siteIdChanged = false;

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
                            hasUnmatchedTargets = true;
                            warningMsg += $"IIS SiteID {c.ServerSiteId} could not be matched for Single Site deployment mode. Deployment switched to Auto mode.";
                            c.RequestConfig.DeploymentSiteOption = DeploymentOption.Auto;
                        }
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
                            try
                            {
                                cert = new X509Certificate2(pfxBytes, pfxPwd);
                            }
                            catch
                            {
                                // failed to load the provided cert, cert will remain null and failure will be reported in the import action step
                            }
                        }
                    }
                }

                if (cert != null)
                {

                    var isVerified = cert.Verify();

                    if (!System.IO.File.Exists(c.Filename) || settings.OverwriteExisting)
                    {

                        if (!isPreviewMode)
                        {
                            // perform actual import, TODO: re-map cert PFX storage location
                            try
                            {
                                // create path if we need to
                                var pathInfo = new System.IO.FileInfo(c.Filename);
                                pathInfo.Directory.Create();

                                // write cert file
                                System.IO.File.WriteAllBytes(c.Filename, pfxBytes);

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
