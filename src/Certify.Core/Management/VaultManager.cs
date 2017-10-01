using ACMESharp;
using ACMESharp.Vault.Model;
using ACMESharp.Vault.Providers;
using ACMESharp.Vault.Util;
using Certify.ACMESharpCompat;
using Certify.Management;
using Certify.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Certify
{
    public class ProcessStepResult
    {
        public bool IsSuccess;
        public string ErrorMessage;
        public object Result;
    }

    public class VaultManager
    {
        private VaultInfo vaultConfig;
        internal string vaultFolderPath;
        private string vaultFilename;
        private string vaultProfile = null;
        public List<ActionLogItem> ActionLogs { get; }

        internal const string VAULT_LOCK = "CertifyVault";

        private readonly IdnMapping idnMapping = new IdnMapping();

        public bool UseEFSForSensitiveFiles { get; set; } = false;

        public string VaultFolderPath
        {
            get { return vaultFolderPath; }
        }

        #region Vault

        public VaultManager(string vaultFolderPath, string vaultFilename)
        {
            Certify.Management.Util.SetSupportedTLSVersions();
            this.vaultFolderPath = vaultFolderPath;
            this.vaultFilename = vaultFilename;

            this.ActionLogs = new List<ActionLogItem>();

#if DEBUG
            this.InitVault(staging: true);
#else
            this.InitVault(staging: false);
#endif
            ReloadVaultConfig();

            //register default PKI provider
            //ACMESharp.PKI.CertificateProvider.RegisterProvider<ACMESharp.PKI.Providers.OpenSslLibProvider>();
            ACMESharp.PKI.CertificateProvider.RegisterProvider<ACMESharp.PKI.Providers.BouncyCastleProvider>();
        }

        internal List<RegistrationInfo> GetRegistrations(bool reloadVaultConfig)
        {
            if (reloadVaultConfig)
            {
                ReloadVaultConfig();
            }

            if (vaultConfig != null && vaultConfig.Registrations != null)
            {
                return vaultConfig.Registrations.Values.ToList();
            }
            else
            {
                return new List<RegistrationInfo>();
            }
        }

        private void OpenVaultStorage(ACMESharp.Vault.IVault vlt, bool initOrOpen)
        {
            // vault store can have IO access errors due to AV products scanning files while we want
            // to use them, retry failed open attempts
            int maxAttempts = 3;
            while (maxAttempts > 0)
            {
                try
                {
                    vlt.OpenStorage(initOrOpen: initOrOpen);
                    return;
                }
                catch (System.IO.IOException)
                {
                    maxAttempts--;
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("Failed to open vault, retrying..");
#endif
                    Thread.Sleep(200);
                }
            }
        }

        public bool InitVault(bool staging = true)
        {
            string apiURI = ACMESharpUtils.WELL_KNOWN_BASE_SERVICES[ACMESharpUtils.WELL_KNOWN_LESTAGE];
            if (!staging)
            {
                //live api
                apiURI = ACMESharpUtils.WELL_KNOWN_BASE_SERVICES[ACMESharpUtils.WELL_KNOWN_LE];
            }

            bool vaultExists = false;
            lock (VAULT_LOCK)
            {
                using (var vlt = ACMESharpUtils.GetVault(this.vaultProfile))
                {
                    OpenVaultStorage(vlt, true);
                    var v = vlt.LoadVault(false);
                    if (v != null) vaultExists = true;
                }
            }

            if (!vaultExists)
            {
                var baseUri = apiURI;
                if (string.IsNullOrEmpty(baseUri))
                {
                    throw new InvalidOperationException("either a base service or URI is required");
                }

                lock (VAULT_LOCK)
                {
                    using (var vlt = ACMESharpUtils.GetVault(this.vaultProfile))
                    {
                        this.LogAction("InitVault", "Creating Vault");

                        OpenVaultStorage(vlt, true);

                        var v = new VaultInfo
                        {
                            Id = EntityHelper.NewId(),
                            BaseUri = baseUri,
                            ServerDirectory = new AcmeServerDirectory()
                        };

                        vlt.SaveVault(v);
                    }
                }
            }
            else
            {
                this.LogAction("InitVault", "Vault exists.");
            }

            this.vaultFolderPath = GetVaultPath();

            return true;
        }

        internal string GetACMEBaseURI()
        {
            var vaultConfig = GetVaultConfig();
            return vaultConfig.BaseUri;
        }

        private void LogAction(string command, string result = null)
        {
            if (this.ActionLogs != null)
            {
                this.ActionLogs.Add(new ActionLogItem { Command = command, Result = result, DateTime = DateTime.Now });
            }
        }

        public VaultInfo LoadVaultFromFile()
        {
            lock (VAULT_LOCK)
            {
                using (var vlt = ACMESharpUtils.GetVault(this.vaultProfile))
                {
                    OpenVaultStorage(vlt, true);
                    var v = vlt.LoadVault();
                    return v;
                }
            }
        }

        public VaultInfo GetVaultConfig()
        {
            if (vaultConfig != null)
            {
                return vaultConfig;
            }
            else return null;
        }

        public void CleanupVault(Guid? identifierToRemove = null, bool includeDupeIdentifierRemoval = false)
        {
            //remove duplicate identifiers etc

            lock (VAULT_LOCK)
            {
                using (var vlt = ACMESharpUtils.GetVault(this.vaultProfile))
                {
                    OpenVaultStorage(vlt, true);
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

                    //find and remove certificates with no valid identifier in vault or with empty settings
                    toBeRemoved = new List<Guid>();

                    if (v.Certificates != null)
                    {
                        foreach (var c in v.Certificates)
                        {
                            if (
                                String.IsNullOrEmpty(c.IssuerSerialNumber) //no valid issuer serial
                                ||
                                !v.Identifiers.ContainsKey(c.IdentifierRef) //no existing Identifier
                                )
                            {
                                toBeRemoved.Add(c.Id);
                            }
                        }

                        foreach (var i in toBeRemoved)
                        {
                            v.Certificates.Remove(i);
                        }
                    }

                    /*if (includeDupeIdentifierRemoval)
                    {
                        //remove identifiers where the dns occurs more than once
                        foreach (var i in v.Identifiers)
                        {
                            var count = v.Identifiers.Values.Where(l => l.Dns == i.Dns).Count();
                            if (count > 1)
                            {
                                //identify most recent Identifier (based on assigned, non-expired cert), delete all the others

                                toBeRemoved.Add(i.Id);
                            }
                        }
                    }*/

                    vlt.SaveVault(v);
                }
            }
        }

        public void ReloadVaultConfig()
        {
            lock (VAULT_LOCK)
            {
                this.vaultConfig = LoadVaultFromFile();
            }
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
            using (var vlt = (LocalDiskVault)ACMESharpUtils.GetVault(this.vaultProfile))
            {
                this.vaultFolderPath = vlt.RootPath;
            }
            return this.vaultFolderPath;
        }

        #endregion Vault

        #region Vault Operations

        public bool HasContacts(bool loadConfig = false)
        {
            if (loadConfig)
            {
                ReloadVaultConfig();
            }

            if (this.vaultConfig.Registrations != null && this.vaultConfig.Registrations.Count > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
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

        public APIResult NewCertificate(string domainIdentifierAlias, string certAlias, string[] subjectAlternativeNameIdentifiers)
        {
            //if (subjectAlternativeNameIdentifiers != null) cmd.AlternativeIdentifierRefs = subjectAlternativeNameIdentifiers;
            // cmd.Generate = new System.Management.Automation.SwitchParameter(true);

            try
            {
                var result = ACMESharpUtils.NewCertificate(certAlias, domainIdentifierAlias, subjectAlternativeNameIdentifiers);

                return new APIResult { IsOK = true, Result = result };
            }
            catch (Exception exp)
            {
                return new APIResult { IsOK = false, Message = exp.ToString(), Result = exp };
            }
        }

        public CertificateInfo GetCertificate(string reference, bool reloadVaultConfig = false)
        {
            if (reloadVaultConfig)
            {
                this.ReloadVaultConfig();
            }

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

        public APIResult SubmitCertificate(string certAlias)
        {
            try
            {
                var result = ACMESharpUtils.SubmitCertificate(certAlias, protectSensitiveFileStorage: UseEFSForSensitiveFiles);

                return new APIResult { IsOK = true, Result = result };
            }
            catch (Exception exp)
            {
                if (exp is ACMESharp.AcmeClient.AcmeWebException)
                {
                    var aex = (ACMESharp.AcmeClient.AcmeWebException)exp;
                    return new APIResult { IsOK = false, Message = aex.Message, Result = aex };
                }
                else
                {
                    return new APIResult { IsOK = false, Message = exp.Message, Result = exp };
                }
            }
        }

        public void UpdateCertificate(string certRef)
        {
            ACMESharpUtils.UpdateCertificate(certRef);
        }

        public void ExportCertificate(string certRef, bool pfxOnly = false)
        {
            GetVaultPath();
            if (!Directory.Exists(VaultFolderPath + "\\" + LocalDiskVault.ASSET))
            {
                Directory.CreateDirectory(VaultFolderPath + "\\" + LocalDiskVault.ASSET);
            }

            if (certRef.StartsWith("=")) certRef = certRef.Replace("=", "");

            if (pfxOnly)
            {
                var ExportPkcs12 = vaultFolderPath + "\\" + LocalDiskVault.ASSET + "\\" + certRef + "-all.pfx";
                ACMESharpUtils.GetCertificate(certRef, ExportPkcs12: ExportPkcs12, overwrite: true);
            }
            else
            {
                var ExportKeyPEM = vaultFolderPath + "\\" + LocalDiskVault.KEYPM + "\\" + certRef + "-key.pem";
                var ExportCsrPEM = vaultFolderPath + "\\" + LocalDiskVault.CSRPM + "\\" + certRef + "-csr.pem";
                var ExportCertificatePEM = vaultFolderPath + "\\" + LocalDiskVault.CRTPM + "\\" + certRef + "-crt.pem";
                var ExportCertificateDER = vaultFolderPath + "\\" + LocalDiskVault.CRTDR + "\\" + certRef + "-crt.der";
                var ExportPkcs12 = vaultFolderPath + "\\" + LocalDiskVault.ASSET + "\\" + certRef + "-all.pfx";

                ACMESharpUtils.GetCertificate(
                    certRef,
                    ExportKeyPEM: ExportKeyPEM,
                    ExportCsrPEM: ExportCsrPEM,
                    ExportCertificatePEM: ExportCertificatePEM,
                    ExportCertificateDER: ExportCertificateDER,
                    ExportPkcs12: ExportPkcs12,
                    overwrite: true
                    );
            }
        }

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

        public bool AddNewRegistrationAndAcceptTOS(string contact)
        {
            try
            {
                ACMESharpUtils.NewRegistration(null, new string[] { contact }, acceptTOS: true);
                return true;
            }
            catch (System.Net.WebException exp)
            {
                System.Diagnostics.Debug.WriteLine(exp.ToString());
                return false;
            }
        }

        public bool DeleteRegistrationInfo(Guid id)
        {
            using (var vlt = ACMESharpUtils.GetVault(this.vaultProfile))
            {
                lock (VAULT_LOCK)
                {
                    try
                    {
                        OpenVaultStorage(vlt, true);
                        vaultConfig.Registrations.Remove(id);
                        vlt.SaveVault(vaultConfig);
                        return true;
                    }
                    catch (Exception e)
                    {
                        // TODO: Logging of errors.
                        System.Diagnostics.Debug.WriteLine(e.Message);
                        return false;
                    }
                }
            }
        }

        internal bool DeleteIdentifierByDNS(string dns)
        {
            using (var vlt = ACMESharpUtils.GetVault(this.vaultProfile))
            {
                try
                {
                    lock (VAULT_LOCK)
                    {
                        OpenVaultStorage(vlt, true);
                        if (vaultConfig.Identifiers != null)
                        {
                            var idsToRemove = vaultConfig.Identifiers.Values.Where(i => i.Dns == dns);
                            List<Guid> removing = new List<Guid>();
                            foreach (var identifier in idsToRemove)
                            {
                                removing.Add(identifier.Id);
                            }
                            foreach (var identifier in removing)
                            {
                                vaultConfig.Identifiers.Remove(identifier);
                            }

                            vlt.SaveVault(vaultConfig);
                        }
                    }

                    return true;
                }
                catch (Exception e)
                {
                    // TODO: Logging of errors.
                    System.Diagnostics.Debug.WriteLine(e.Message);
                    return false;
                }
            }
        }

        public IdentifierInfo GetIdentifier(string aliasOrDNS, bool reloadVaultConfig = false)
        {
            if (reloadVaultConfig)
            {
                ReloadVaultConfig();
            }

            lock (VAULT_LOCK)
            {
                var identifiers = GetIdentifiers();
                if (identifiers != null)
                {
                    //find best match for given alias/id
                    var result = identifiers.FirstOrDefault(i => i.Alias == aliasOrDNS);
                    if (result == null)
                    {
                        result = identifiers.FirstOrDefault(i => i.Dns == aliasOrDNS);
                    }
                    if (result == null)
                    {
                        result = identifiers.FirstOrDefault(i => i.Id.ToString() == aliasOrDNS);
                    }
                    return result;
                }
                else
                {
                    return null;
                }
            }
        }

        public List<IdentifierInfo> GetIdentifiers(bool reloadVaultConfig = false)
        {
            if (reloadVaultConfig)
            {
                ReloadVaultConfig();
            }

            if (vaultConfig != null && vaultConfig.Identifiers != null)
            {
                return vaultConfig.Identifiers.Values.ToList();
            }
            else
            {
                return new List<IdentifierInfo>();
            }
        }

        public List<CertificateInfo> GetCertificates(bool reloadVaultConfig = false)
        {
            if (reloadVaultConfig)
            {
                ReloadVaultConfig();
            }

            if (vaultConfig != null && vaultConfig.Certificates != null)
            {
                return vaultConfig.Certificates.Values.ToList();
            }
            else
            {
                return new List<CertificateInfo>();
            }
        }

        public void UpdateIdentifier(string domainIdentifierAlias)
        {
            ACMESharpUtils.UpdateIdentifier(domainIdentifierAlias);
        }

        public void SubmitChallenge(string alias, string challengeType = "http-01")
        {
            //well known challenge all ready to be read by server
            ACMESharpUtils.SubmitChallenge(alias, challengeType);
        }

        #endregion Vault Operations

        #region ACME Workflow Steps

        public PendingAuthorization BeginRegistrationAndValidation(CertRequestConfig requestConfig, string identifierAlias, string challengeType = "http-01", string domain = null)
        {
            //if no alternative domain specified, use the primary domains as the subject
            if (domain == null) domain = requestConfig.PrimaryDomain;

            // if (GetIdentifier(identifierAlias) == null)

            //if an identifier exists for the same dns in vault, remove it to avoid confusion
            this.DeleteIdentifierByDNS(domain);

            // ACME service requires international domain names in ascii mode, register the new
            // identifier with Lets Encrypt
            var authState = ACMESharpUtils.NewIdentifier(identifierAlias, idnMapping.GetAscii(domain));

            var identifier = this.GetIdentifier(identifierAlias, reloadVaultConfig: true);

            //FIXME: when validating subsequent SAN names in parallel request mode, the identifier is null?
            if (identifier != null && identifier.Authorization != null && identifier.Authorization.IsPending())
            {
                ACMESharpUtils.CompleteChallenge(identifier.Alias, challengeType, Handler: "manual", Regenerate: true, Repeat: true);

                //get challenge info
                ReloadVaultConfig();
                identifier = GetIdentifier(identifierAlias);
                var challengeInfo = identifier.Challenges.FirstOrDefault(c => c.Value.Type == challengeType).Value;

                //identifier challenege specification is now ready for use to prepare and answer for LetsEncrypt to check
                return new PendingAuthorization() { Challenge = GetAuthorizeChallengeItemFromAuthChallenge(challengeInfo), Identifier = GetDomainIdentifierItemFromIdentifierInfo(identifier), TempFilePath = "", ExtensionlessConfigCheckedOK = false };
            }
            else
            {
                //identifier is null or already valid (previously authorized)
                return new PendingAuthorization() { Challenge = null, Identifier = GetDomainIdentifierItemFromIdentifierInfo(identifier), TempFilePath = "", ExtensionlessConfigCheckedOK = false };
            }
        }

        public AuthorizeChallengeItem GetAuthorizeChallengeItemFromAuthChallenge(AuthorizeChallenge challenge)
        {
            return new AuthorizeChallengeItem
            {
                Status = challenge.Status,
                ChallengeData = challenge.Challenge
            };
        }

        public IdentifierItem GetDomainIdentifierItemFromIdentifierInfo(ACMESharp.Vault.Model.IdentifierInfo identifier)
        {
            var i = new IdentifierItem
            {
                Id = identifier.Id.ToString(),
                Alias = identifier.Alias,
                Name = identifier.Dns,
                Dns = identifier.Dns,
                Status = identifier.Authorization?.Status,
            };

            if (identifier.Authorization != null)
            {
                i.AuthorizationExpiry = identifier.Authorization.Expires;
                i.IsAuthorizationPending = identifier.Authorization.IsPending();
            }

            return i;
        }

        public PendingAuthorization PerformIISAutomatedChallengeResponse(IISManager iisManager, ManagedSite managedSite, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedSite.RequestConfig;
            bool extensionlessConfigOK = false;

            //if validation proxy enabled, access to the domain being validated is checked via our remote API rather than directly on the servers
            bool checkViaProxy = Certify.Properties.Settings.Default.EnableValidationProxyAPI;

            //if copying the file for the user, attempt that now
            if (pendingAuth.Challenge != null && requestConfig.PerformChallengeFileCopy)
            {
                var httpChallenge = (ACMESharp.ACME.HttpChallenge)pendingAuth.Challenge.ChallengeData;
                this.LogAction("Preparing challenge response for LetsEncrypt server to check at: " + httpChallenge.FileUrl);
                this.LogAction("If the challenge response file is not accessible at this exact URL the validation will fail and a certificate will not be issued.");

                // get website root path, expand environment variables if required
                string websiteRootPath = requestConfig.WebsiteRootPath;

                // if website root path not specified, determine it now
                if (String.IsNullOrEmpty(websiteRootPath))
                {
                    websiteRootPath = iisManager.GetSitePhysicalPath(managedSite);
                }

                if (!String.IsNullOrEmpty(websiteRootPath) && websiteRootPath.Contains("%"))
                {
                    // if websiteRootPath contains %websiteroot% variable, replace that with the
                    // current physical path for the site
                    if (websiteRootPath.Contains("%websiteroot%"))
                    {
                        // sets env variable for this process only
                        Environment.SetEnvironmentVariable("websiteroot", iisManager.GetSitePhysicalPath(managedSite));
                    }
                    // expand any environment variables present in site path
                    websiteRootPath = Environment.ExpandEnvironmentVariables(websiteRootPath);
                }

                if (String.IsNullOrEmpty(websiteRootPath) || !Directory.Exists(websiteRootPath))
                {
                    // our website no longer appears to exist on disk, continuing would potentially
                    // create unwanted folders, so it's time for us to give up
                    pendingAuth.ExtensionlessConfigCheckedOK = false;
                    this.LogAction($"The website root path for {managedSite.Name} could not be determined. Request cannot continue.");

                    return pendingAuth;
                }

                //copy temp file to path challenge expects in web folder
                var destFile = Path.Combine(websiteRootPath, httpChallenge.FilePath);
                var destPath = Path.GetDirectoryName(destFile);
                if (!Directory.Exists(destPath))
                {
                    Directory.CreateDirectory(destPath);
                }

                //copy challenge response to web folder /.well-known/acme-challenge
                System.IO.File.WriteAllText(destFile, httpChallenge.FileContent);

                var wellknownContentPath = httpChallenge.FilePath.Substring(0, httpChallenge.FilePath.LastIndexOf("/"));
                var testFilePath = Path.Combine(websiteRootPath, wellknownContentPath + "//configcheck");

                // write the config check file if it doesn't already exist
                if (!File.Exists(testFilePath))
                {
                    System.IO.File.WriteAllText(testFilePath, "Extensionless File Config Test - OK");
                }

                //create a web.config for extensionless files, then test it (make a request for the extensionless configcheck file over http)
                string webConfigContent = Core.Properties.Resources.IISWebConfig;

                if (!File.Exists(destPath + "\\web.config"))
                {
                    //no existing config, attempt auto config and perform test
                    System.IO.File.WriteAllText(destPath + "\\web.config", webConfigContent);
                    if (requestConfig.PerformExtensionlessConfigChecks)
                    {
                        if (CheckURL("http://" + requestConfig.PrimaryDomain + "/" + wellknownContentPath + "/configcheck", checkViaProxy))
                        {
                            extensionlessConfigOK = true;
                        }
                    }
                }
                else
                {
                    //web config already exists, don't overwrite it, just test it

                    if (requestConfig.PerformExtensionlessConfigChecks)
                    {
                        if (CheckURL("http://" + requestConfig.PrimaryDomain + "/" + wellknownContentPath + "/configcheck", checkViaProxy))
                        {
                            extensionlessConfigOK = true;
                        }
                        if (!extensionlessConfigOK && requestConfig.PerformAutoConfig)
                        {
                            //didn't work, try our default config
                            System.IO.File.WriteAllText(destPath + "\\web.config", webConfigContent);

                            if (CheckURL("http://" + requestConfig.PrimaryDomain + "/" + wellknownContentPath + "/configcheck", checkViaProxy))
                            {
                                extensionlessConfigOK = true;
                            }
                        }
                    }
                }

                if (requestConfig.PerformExtensionlessConfigChecks)
                {
                    if (!extensionlessConfigOK && requestConfig.PerformAutoConfig)
                    {
                        //if first attempt(s) at config failed, try an alternative config
                        webConfigContent = Properties.Resources.IISWebConfigAlt;

                        System.IO.File.WriteAllText(destPath + "\\web.config", webConfigContent);

                        if (CheckURL("http://" + requestConfig.PrimaryDomain + "/" + wellknownContentPath + "/configcheck", checkViaProxy))
                        {
                            //ready to complete challenge
                            extensionlessConfigOK = true;
                        }
                    }
                }
            }

            //configuration applied, ready to ask LE to validate our answer
            pendingAuth.ExtensionlessConfigCheckedOK = extensionlessConfigOK;
            return pendingAuth;
        }

        public bool CompleteIdentifierValidationProcess(string domainIdentifierAlias)
        {
            this.UpdateIdentifier(domainIdentifierAlias);
            var identiferStatus = this.GetIdentifier(domainIdentifierAlias, true);
            var attempts = 0;
            var maxAttempts = 3;

            while (identiferStatus.Authorization.Status == "pending" && attempts < maxAttempts)
            {
                System.Threading.Thread.Sleep(2000); //wait a couple of seconds before checking again
                this.UpdateIdentifier(domainIdentifierAlias);
                identiferStatus = this.GetIdentifier(domainIdentifierAlias, true);
                attempts++;
            }

            if (identiferStatus.Authorization.Status != "valid")
            {
                //still pending or failed
                System.Diagnostics.Debug.WriteLine("LE Authorization problem: " + identiferStatus.Authorization.Status);
                return false;
            }
            else
            {
                //ready to get cert
                return true;
            }
        }

        public ProcessStepResult PerformCertificateRequestProcess(string domainIdentifierRef, string[] alternativeIdentifierRefs)
        {
            //

            //all good, we can request a certificate
            //if authorizing a SAN we would need to repeat the above until all domains are valid, then we can request cert
            var certAlias = "cert_" + domainIdentifierRef;

            //register cert placeholder in vault

            var certRegResult = this.NewCertificate(domainIdentifierRef, certAlias, subjectAlternativeNameIdentifiers: alternativeIdentifierRefs);

            //ask LE to issue a certificate for our domain(s)
            //if this step fails we should quit and try again later
            var certRequestResult = this.SubmitCertificate(certAlias);

            if (certRequestResult.IsOK)
            {
                //LE may now have issued a certificate, this process may not be immediate
                var certDetails = this.GetCertificate(certAlias, reloadVaultConfig: true);
                var attempts = 0;
                var maxAttempts = 3;

                //cert not issued yet, wait and try again
                while ((certDetails == null || String.IsNullOrEmpty(certDetails.IssuerSerialNumber)) && attempts < maxAttempts)
                {
                    System.Threading.Thread.Sleep(2000); //wait a couple of seconds before checking again
                    this.UpdateCertificate(certAlias);
                    certDetails = this.GetCertificate(certAlias, reloadVaultConfig: true);
                    attempts++;
                }

                if (certDetails != null && !String.IsNullOrEmpty(certDetails.IssuerSerialNumber))
                {
                    //we have an issued certificate, we can go ahead and install it as required
                    System.Diagnostics.Debug.WriteLine("Received certificate issued by LE." + JsonConvert.SerializeObject(certDetails));

                    //if using cert in IIS, we need to export the certificate PFX file, install it as a certificate and setup the site binding to map to this cert
                    string certFolderPath = this.GetCertificateFilePath(certDetails.Id, LocalDiskVault.ASSET);
                    string pfxFile = certAlias + "-all.pfx";
                    string pfxPath = System.IO.Path.Combine(certFolderPath, pfxFile);

                    //create folder to export PFX to, if required
                    if (!System.IO.Directory.Exists(certFolderPath))
                    {
                        System.IO.Directory.CreateDirectory(certFolderPath);
                    }

                    //if file already exists we want to delet the old one
                    if (System.IO.File.Exists(pfxPath))
                    {
                        //delete existing PFX (if any)
                        System.IO.File.Delete(pfxPath);
                    }

                    //export the PFX file
                    this.ExportCertificate(certAlias, pfxOnly: true);

                    if (!System.IO.File.Exists(pfxPath))
                    {
                        return new ProcessStepResult { IsSuccess = false, ErrorMessage = "Failed to export PFX. " + pfxPath, Result = pfxPath };
                    }
                    else
                    {
                        return new ProcessStepResult { IsSuccess = true, Result = pfxPath };
                    }
                }
                else
                {
                    return new ProcessStepResult { IsSuccess = false, ErrorMessage = "Failed to get new certificate from LetsEncrypt." };
                }
            }
            else
            {
                return new ProcessStepResult { IsSuccess = false, ErrorMessage = "Failed to get new certificate from LetsEncrypt :: " + certRequestResult.Message };
            }
        }

        #endregion ACME Workflow Steps

        #region Utils

        public string ComputeIdentifierAlias(string domain)
        {
            return "ident" + Guid.NewGuid().ToString().Substring(0, 8).Replace("-", "");
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

        private bool CheckURL(string url, bool useProxyAPI)
        {
            var checkUrl = url + "";
            if (useProxyAPI)
            {
                url = Properties.Resources.APIBaseURI + "testurlaccess?url=" + url;
            }
            //check http request to test path works
            bool checkSuccess = false;
            try
            {
                WebRequest request = WebRequest.Create(url);
                ServicePointManager.ServerCertificateValidationCallback = (obj, cert, chain, errors) =>
                {
                    // ignore all cert errors when validating URL response
                    return true;
                };
                var response = (HttpWebResponse)request.GetResponse();

                //if checking via proxy, examine result
                if (useProxyAPI)
                {
                    if ((int)response.StatusCode >= 200)
                    {
                        var encoding = ASCIIEncoding.UTF8;
                        using (var reader = new System.IO.StreamReader(response.GetResponseStream(), encoding))
                        {
                            string jsonText = reader.ReadToEnd();
                            this.LogAction("URL Check Result: " + jsonText);
                            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.API.URLCheckResult>(jsonText);
                            if (result.IsAccessible == true)
                            {
                                checkSuccess = true;
                            }
                            else
                            {
                                checkSuccess = false;
                            }
                        }
                    }
                }
                else
                {
                    //not checking via proxy, base result on status code
                    if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                    {
                        checkSuccess = true;
                    }
                }

                if (checkSuccess == false && useProxyAPI == true)
                {
                    //request failed using proxy api, request again using local http
                    checkSuccess = CheckURL(checkUrl, false);
                }
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("Failed to check url for access");
                checkSuccess = false;
            }
            finally
            {
                // reset callback for other requests to validate using default behavior
                ServicePointManager.ServerCertificateValidationCallback = null;
            }

            return checkSuccess;
        }

        #endregion Utils
    }
}