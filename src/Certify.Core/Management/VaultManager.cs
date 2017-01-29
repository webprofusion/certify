using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACMESharp;
using ACMESharp.POSH;
using ACMESharp.POSH.Util;
using ACMESharp.Util;
using ACMESharp.Vault.Model;
using ACMESharp.Vault.Profile;
using ACMESharp.Vault.Providers;
using ACMESharp.WebServer;
using Certify.Models;

namespace Certify
{
    public class VaultManager
    {
        private VaultInfo vaultConfig;
        private PowershellManager powershellManager;
        private string vaultFolderPath;
        private string vaultFilename;
        public List<ActionLogItem> ActionLogs { get; }

        public string VaultFolderPath
        {
            get { return vaultFolderPath; }
        }

        public PowershellManager PowershellManager
        {
            get
            {
                return this.powershellManager;
            }
        }

        #region Vault

        public bool InitVault(bool staging = true)
        {
            //System.IO.Directory.CreateDirectory(this.vaultFolderPath);
            //this.powershellManager.SetWorkingDirectory(this.vaultFolderPath);

            string apiURI = InitializeVault.WELL_KNOWN_BASE_SERVICES[InitializeVault.WELL_KNOWN_LESTAGE];
            if (!staging)
            {
                //live api
                apiURI = InitializeVault.WELL_KNOWN_BASE_SERVICES[InitializeVault.WELL_KNOWN_LE];
            }
            powershellManager.InitializeVault(apiURI);

            this.vaultFolderPath = GetVaultPath();
            //create default manual http provider (challenge/response by placing answer in well known location on website for server to fetch);
            //powershellManager.NewProviderConfig("Manual", "manualHttpProvider");
            return true;
        }

        public VaultManager(string vaultFolderPath, string vaultFilename)
        {
            this.vaultFolderPath = vaultFolderPath;
            this.vaultFilename = vaultFilename;

            this.ActionLogs = new List<ActionLogItem>();

            powershellManager = new PowershellManager(vaultFolderPath, this.ActionLogs);
#if DEBUG
            this.InitVault(staging: true);
#else
            this.InitVault(staging: false);
#endif
            ReloadVaultConfig();
            /*
            if (System.IO.Directory.Exists(vaultFolderPath))
            {
                this.ReloadVaultConfig();
                powershellManager = new PowershellManager(vaultFolderPath, this.ActionLogs);
            }
            else
            {
            }*/
        }

        public VaultInfo LoadVaultFromFile()
        {
            using (var vlt = ACMESharp.POSH.Util.VaultHelper.GetVault())
            {
                vlt.OpenStorage(true);
                var v = vlt.LoadVault();
                return v;
            }
            /*LocalDiskVault.EntityMeta<VaultConfig> vaultDetails = null;
            if (File.Exists(path))
            {
                using (var s = new FileStream(path, FileMode.Open))
                {
                    vaultDetails = JsonHelper.Load<FileVaultProvider.EntityMeta<VaultConfig>>(s);
                }
            }

            if (vaultDetails != null)
            {
                return vaultDetails.Entity;
            }
            else
            {
                return null;
            }
            */
        }

        public VaultInfo GetVaultConfig()
        {
            if (vaultConfig != null)
            {
                return vaultConfig;
            }
            else return null;
        }

        public void CleanupVault(Guid? identifierToRemove = null)
        {
            //remove duplicate identifiers etc

            using (var vlt = ACMESharp.POSH.Util.VaultHelper.GetVault())
            {
                vlt.OpenStorage();
                var v = vlt.LoadVault();

                List<Guid> toBeRemoved = new List<Guid>();
                if (identifierToRemove != null)
                {
                    if (v.Identifiers.Keys.Any(i => i == (Guid)identifierToRemove))
                    {
                        toBeRemoved.Add((Guid)identifierToRemove);
                    }
                }
                else
                {
                    //find all orphaned identified
                    if (v.Identifiers != null)
                    {
                        foreach (var k in v.Identifiers.Keys)
                        {
                            var identifier = v.Identifiers[k];

                            var certs = v.Certificates.Values.Where(c => c.IdentifierRef == identifier.Id);
                            if (!certs.Any())
                            {
                                toBeRemoved.Add(identifier.Id);
                            }
                        }
                    }
                }

                foreach (var i in toBeRemoved)
                {
                    v.Identifiers.Remove(i);
                }
                //

                //find and remove certificatess with no valid identifier in vault
                toBeRemoved = new List<Guid>();

                if (v.Certificates != null)
                {
                    foreach (var c in v.Certificates)
                    {
                        if (!v.Identifiers.ContainsKey(c.IdentifierRef))
                        {
                            toBeRemoved.Add(c.Id);
                        }
                    }

                    foreach (var i in toBeRemoved)
                    {
                        v.Certificates.Remove(i);
                    }
                }

                vlt.SaveVault(v);
            }
        }

        public void ReloadVaultConfig()
        {
            this.vaultConfig = LoadVaultFromFile();
        }

        public bool IsValidVaultPath(string vaultPathFolder)
        {
            string vaultFile = vaultPathFolder + "\\" + LocalDiskVault.VAULT;
            if (File.Exists(vaultFile))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public string GetVaultPath()
        {
            using (var vlt = (LocalDiskVault)ACMESharp.POSH.Util.VaultHelper.GetVault())
            {
                this.vaultFolderPath = vlt.RootPath;
            }
            return this.vaultFolderPath;
        }

        public bool HasContacts()
        {
            if (this.vaultConfig.Registrations != null && this.vaultConfig.Registrations.Count > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public IdentifierInfo GetIdentifier(string alias, bool reloadVaultConfig = false)
        {
            if (reloadVaultConfig)
            {
                ReloadVaultConfig();
            }

            var identifiers = GetIdentifiers();
            if (identifiers != null)
            {
                //find best match for given alias/id
                var result = identifiers.FirstOrDefault(i => i.Alias == alias);
                if (result == null)
                {
                    result = identifiers.FirstOrDefault(i => i.Dns == alias);
                }
                if (result == null)
                {
                    result = identifiers.FirstOrDefault(i => i.Id.ToString() == alias);
                }
                return result;
            }
            else
            {
                return null;
            }
        }

        public List<IdentifierInfo> GetIdentifiers()
        {
            if (vaultConfig != null && vaultConfig.Identifiers != null)
            {
                return vaultConfig.Identifiers.Values.ToList();
            }
            else return null;
        }

        public ProviderProfileInfo GetProviderConfig(string alias)
        {
            var vaultConfig = this.GetVaultConfig();
            if (vaultConfig.ProviderProfiles != null)
            {
                return vaultConfig.ProviderProfiles.Values.FirstOrDefault(p => p.Alias == alias);
            }
            else return null;
        }

        #endregion Vault

        #region Registration

        public void AddNewRegistration(string contacts)
        {
            powershellManager.NewRegistration(contacts);

            powershellManager.AcceptRegistrationTOS();
        }

        internal bool DeleteRegistrationInfo(Guid id)
        {
            using (var vlt = ACMESharp.POSH.Util.VaultHelper.GetVault())
            {
                try
                {
                    vlt.OpenStorage(true);
                    vaultConfig.Registrations.Remove(id);
                    vlt.SaveVault(vaultConfig);
                    return true;
                }
                catch (Exception e)
                {
                    // TODO: Logging of errors.
                    System.Windows.Forms.MessageBox.Show(e.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    return false;
                }
            }
        }

        public bool DeleteRegistrationInfo(string Id)
        {
            return false;
        }

        #endregion Registration

        #region Certificates

        public bool CertExists(string domainAlias)
        {
            var certRef = "cert_" + domainAlias;

            if (vaultConfig.Certificates != null && vaultConfig.Certificates.Values.Any(c => c.Alias == certRef))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public void UpdateAndExportCertificate(string domainAlias)
        {
            var certRef = "cert_" + domainAlias;
            try
            {
                powershellManager.UpdateCertificate(certRef);

                if (CertExists(domainAlias)) // if the cert exists after the update, export it
                {
                    var certInfo = GetCertificate(certRef);
                    string certId = "=" + certInfo.Id.ToString();

                    // if we have our first cert files, lets export the pfx as well
                    ExportCertificate(certId, pfxOnly: true);
                }
            }
            catch (Exception exp)
            {
                System.Diagnostics.Debug.WriteLine(exp.ToString());
            }
        }

        public bool RenewCertificate(Guid identifierRef)
        {
            var identifier = vaultConfig.Identifiers[identifierRef];

            try
            {
                this.CreateCertificate(identifier.Alias);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string CreateCertificate(string domainAlias)
        {
            var certRef = "cert_" + domainAlias;
            //New-ACMECertificate -Identifier dns1 -Alias cert1 -Generate
            bool certExists = CertExists(domainAlias);

            if (!certExists)
            {
                powershellManager.NewCertificate(domainAlias, certRef);
            }
            ReloadVaultConfig();

            //return certRef;

            try
            {
                var apiResult = powershellManager.SubmitCertificate(certRef, force: true);

                //give LE time to generate cert before fetching fresh status info
                Thread.Sleep(1000);
            }
            catch (Exception exp)
            {
                System.Diagnostics.Debug.WriteLine(exp.ToString());
            }

            ReloadVaultConfig();

            UpdateAndExportCertificate(domainAlias);

            return certRef;
        }

        public string GetCertificateFilePath(Guid id, string assetTypeFolder = LocalDiskVault.CRTDR)
        {
            GetVaultPath();
            var cert = vaultConfig.Certificates[id];
            if (cert != null)
            {
                return this.VaultFolderPath + "\\" + assetTypeFolder;
            }
            return null;
        }

        public CertificateInfo GetCertificate(string reference)
        {
            //var cert = powershellManager.GetCertificateByRef(reference);
            if (vaultConfig.Certificates != null)
            {
                var cert = vaultConfig.Certificates.Values.FirstOrDefault(c => c.Alias == reference);
                if (cert == null)
                {
                    cert = vaultConfig.Certificates.Values.FirstOrDefault(c => c.Id.ToString() == reference);
                }
                return cert;
            }
            return null;
        }

        public void ExportCertificate(string certRef, bool pfxOnly = false)
        {
            GetVaultPath();
            if (!Directory.Exists(VaultFolderPath + "\\" + LocalDiskVault.ASSET))
            {
                Directory.CreateDirectory(VaultFolderPath + "\\" + LocalDiskVault.ASSET);
            }
            powershellManager.ExportCertificate(certRef, this.VaultFolderPath, pfxOnly);
        }

        public PendingAuthorization DomainInitAndRegistration(CertRequestConfig requestConfig, string identifierAlias)
        {
            /*
            //need to manipulate file created above to set file path or request key sshould be written too.

            */
            //perform domain cert requests

            string domain = requestConfig.Domain;
            // powershellManager.SetWorkingDirectory(this.vaultFolderPath);

            if (GetIdentifier(identifierAlias) == null)
            {
                var result = powershellManager.NewIdentifier(domain, identifierAlias, "Identifier:" + domain);
                if (!result.IsOK) return null;
            }
            ReloadVaultConfig();

            var identifier = this.GetIdentifier(identifierAlias);

            /*
            //config file now has a temp path to write to, begin challenge (writes to temp file with challenge content)
            */
            var ccrResult = powershellManager.CompleteChallenge(identifier.Alias, regenerate: true);

            if (ccrResult.IsOK)
            {
                bool extensionlessConfigOK = false;
                //get challenge info
                ReloadVaultConfig();
                identifier = GetIdentifier(identifierAlias);
                var challengeInfo = identifier.Challenges.FirstOrDefault(c => c.Value.Type == "http-01").Value;

                //if copying the file for the user, attempt that now
                if (challengeInfo != null && requestConfig.PerformChallengeFileCopy)
                {
                    var httpChallenge = (ACMESharp.ACME.HttpChallenge)challengeInfo.Challenge;
                    //copy temp file to path challenge expects in web folder
                    var destFile = Path.Combine(requestConfig.WebsiteRootPath, httpChallenge.FilePath);
                    var destPath = Path.GetDirectoryName(destFile);
                    if (!Directory.Exists(destPath))
                    {
                        Directory.CreateDirectory(destPath);
                    }

                    //copy challenge response to web folder /.well-known/acme-challenge
                    System.IO.File.WriteAllText(destFile, httpChallenge.FileContent);

                    var wellknownContentPath = httpChallenge.FilePath.Substring(0, httpChallenge.FilePath.LastIndexOf("/"));
                    var testFilePath = Path.Combine(requestConfig.WebsiteRootPath, wellknownContentPath + "//configcheck");
                    System.IO.File.WriteAllText(testFilePath, "Extensionless File Config Test - OK");

                    //create a web.config for extensionless files
                    string webConfigContent = Properties.Resources.IISWebConfig;
                    if (!File.Exists(destPath + "\\web.config"))
                    {
                        System.IO.File.WriteAllText(destPath + "\\web.config", webConfigContent);
                    }

                    if (CheckURL("http://" + domain + "/" + wellknownContentPath + "/configcheck"))
                    {
                        extensionlessConfigOK = true;
                    }

                    if (!extensionlessConfigOK)
                    {
                        webConfigContent = Properties.Resources.IISWebConfigAlt;

                        System.IO.File.WriteAllText(destPath + "\\web.config", webConfigContent);
                    }

                    if (CheckURL("http://" + domain + "/" + wellknownContentPath + "/configcheck"))
                    {
                        extensionlessConfigOK = true;
                    }
                    //ready to complete challenge
                }
                return new PendingAuthorization() { Challenge = challengeInfo, Identifier = identifier, TempFilePath = "", ExtensionlessConfigCheckedOK = extensionlessConfigOK };
            }
            else
            {
                return null;
            }
        }

        #endregion Certificates

        public bool IsCompatiblePowershell()
        {
            int version = powershellManager.GetPowershellVersion();
            if (version < 4)
            {
                return false;
            }
            return true;
        }

        public string ComputeIdentifierAlias(string domain)
        {
            var domainAlias = domain.Replace(".", "_");
            domainAlias += DateTime.UtcNow.Ticks;
            return domainAlias;
        }

        private bool CheckURL(string url)
        {
            //check http request to test path works
            try
            {
                WebRequest request = WebRequest.Create(url);
                var response = (HttpWebResponse)request.GetResponse();
                if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("Failed to check url for access");
                return false;
            }
        }

        public void SubmitChallenge(string alias, string challengeType = "http-01")
        {
            //well known challenge all ready to be read by server
            powershellManager.SubmitChallenge(alias, challengeType);

            UpdateIdentifierStatus(alias, challengeType);
        }

        public void UpdateIdentifierStatus(string alias, string challengeType = "http-01")
        {
            powershellManager.UpdateIdentifier(alias, challengeType);
        }

        public string GetActionLogSummary()
        {
            string output = "";
            if (this.ActionLogs != null)
            {
                foreach (var a in this.ActionLogs)
                {
                    output += a.ToString() + "\r\n";
                }
            }
            return output;
        }

        public void PermissionTest()
        {
            if (IisSitePathProvider.IsAdministrator())
            {
                System.Diagnostics.Debug.WriteLine("User is administrator");

                var iisPathProvider = new IisSitePathProvider();
                iisPathProvider.WebSiteRoot = @"C:\inetpub\wwwroot\";
                using (var fs = File.OpenRead(@"C:\temp\log.txt"))
                {
                    var fileURI = new System.Uri(iisPathProvider.WebSiteRoot + "/.temp/test/test123");
                    iisPathProvider.UploadFile(fileURI, fs);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("User is not administrator");
            }
        }
    }
}