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
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        public void SubmitChallenge(string alias, string challengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP)
        {
            //well known challenge all ready to be read by server
            ACMESharpUtils.SubmitChallenge(alias, challengeType);
        }

        #endregion Vault Operations

        #region ACME Workflow Steps

        public PendingAuthorization BeginRegistrationAndValidation(CertRequestConfig requestConfig, string identifierAlias, string challengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP, string domain = null)
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

        /// <summary>
        /// Simulates responding to a challenge, performs a sample configuration and attempts to verify it.
        /// </summary>
        /// <param name="iisManager"></param>
        /// <param name="managedSite"></param>
        /// <returns>APIResult</returns>
        /// <remarks>
        /// The purpose of this method is to test the options (permissions, configuration) before submitting
        /// a request to the ACME server, to avoid creating failed requests and hitting usage limits.
        /// </remarks>
        public async Task<APIResult> TestChallengeResponse(IISManager iisManager, ManagedSite managedSite)
        {
            return await Task.Run(() =>
            {
                ActionLogs.Clear(); // reset action logs
                var requestConfig = managedSite.RequestConfig;
                var result = new APIResult();
                var simulatedAuthorization = new PendingAuthorization(); // create simulated challenge
                // example KeyAuthorization (from https://tools.ietf.org/html/draft-ietf-acme-acme-01#section-7.2)
                string KA = "evaGxfADs6pSRb2LAv9IZf17Dt3juxGJ-PCt92wr-oA.nP1qzpXGymHBrUEepNY9HCsQk7K8KhOypzEt62jcerQ";
                try
                {
                    if (requestConfig.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP)
                    {
                        simulatedAuthorization.Challenge = new AuthorizeChallengeItem
                        {
                            ChallengeData = new ACMESharp.ACME.HttpChallenge(ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP,
                                new ACMESharp.ACME.HttpChallengeAnswer { KeyAuthorization = KA })
                            {
                                FilePath = ".well-known/acme-challenge/configcheck",
                                FileContent = "Extensionless File Config Test - OK",
                                FileUrl = $"http://{managedSite.RequestConfig.PrimaryDomain}/.well-known/acme-challenge/configcheck"
                            }
                        };
                        result.IsOK = PrepareChallengeResponse_Http01(iisManager, managedSite, simulatedAuthorization)();
                    }
                    else if (requestConfig.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI)
                    {
                        if (iisManager.GetIisVersion().Major < 8)
                        {
                            result.IsOK = false;
                            result.Message = $"The {ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI} challenge is only available for IIS versions 8+.";
                            return result;
                        }
                        // create simulated challenge
                        simulatedAuthorization.Challenge = new AuthorizeChallengeItem()
                        {
                            ChallengeData = new ACMESharp.ACME.TlsSniChallenge(ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI,
                                new ACMESharp.ACME.TlsSniChallengeAnswer { KeyAuthorization = KA })
                            {
                                IterationCount = 1
                            }
                        };
                        result.IsOK = PrepareChallengeResponse_TlsSni01(iisManager, managedSite, simulatedAuthorization).All(check => check());
                    }
                    else
                    {
                        throw new NotSupportedException($"ChallengeType not supported: {requestConfig.ChallengeType}");
                    }
                }
                finally
                {
                    result.Message = GetActionLogSummary();
                    simulatedAuthorization.Cleanup();
                }
                return result;
            });
        }

        public PendingAuthorization PerformIISAutomatedChallengeResponse(IISManager iisManager, ManagedSite managedSite, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedSite.RequestConfig;

            if (pendingAuth.Challenge != null)
            {
                if (pendingAuth.Challenge.ChallengeData is ACMESharp.ACME.HttpChallenge 
                    && requestConfig.PerformChallengeFileCopy /* is this needed? */)
                {
                    var check = PrepareChallengeResponse_Http01(iisManager, managedSite, pendingAuth);
                    if (requestConfig.PerformExtensionlessConfigChecks)
                    {
                        pendingAuth.ExtensionlessConfigCheckedOK = check();
                    }
                }
                if (pendingAuth.Challenge.ChallengeData is ACMESharp.ACME.TlsSniChallenge)
                {
                    var checks = PrepareChallengeResponse_TlsSni01(iisManager, managedSite, pendingAuth);
                    if (requestConfig.PerformTlsSniBindingConfigChecks)
                    {
                        // set config check OK if all checks return true
                        pendingAuth.TlsSniConfigCheckedOK = checks.All(check => check());
                    }
                }
            }
            return pendingAuth;
        }

        /// <summary>
        /// Prepares IIS to respond to a http-01 challenge
        /// </summary>
        /// <returns>A Boolean returning Func. Invoke the Func to test the challenge response locally.</returns>
        private Func<bool> PrepareChallengeResponse_Http01(IISManager iisManager, ManagedSite managedSite, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedSite.RequestConfig;
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
                this.LogAction($"The website root path for {managedSite.Name} could not be determined. Request cannot continue.");
                return () => false;
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

            // configure cleanup
            pendingAuth.Cleanup = () => File.Delete(destFile);

            //create a web.config for extensionless files, then test it (make a request for the extensionless configcheck file over http)
            string webConfigContent = Core.Properties.Resources.IISWebConfig;

            if (!File.Exists(destPath + "\\web.config"))
            {
                //no existing config, attempt auto config and perform test
                this.LogAction($"Config does not exist, writing default config to: {destPath}\\web.config");
                System.IO.File.WriteAllText(destPath + "\\web.config", webConfigContent);
                return () => CheckURL("http://" + requestConfig.PrimaryDomain + "/" + httpChallenge.FilePath);
            }
            else
            {
                //web config already exists, don't overwrite it, just test it
                return () =>
                {
                    if (CheckURL("http://" + requestConfig.PrimaryDomain + "/" + httpChallenge.FilePath))
                    {
                        return true;
                    }
                    if (requestConfig.PerformAutoConfig)
                    {
                        this.LogAction($"Pre-config check failed: Auto-config will overwrite existing config: {destPath}\\web.config");
                        //didn't work, try our default config
                        System.IO.File.WriteAllText(destPath + "\\web.config", webConfigContent);

                        if (CheckURL("http://" + requestConfig.PrimaryDomain + "/" + httpChallenge.FilePath))
                        {
                            return true;
                        }
                    }
                    return false;
                };
            }
        }

        /// <summary>
        /// Prepares IIS to respond to a tls-sni-01 challenge
        /// </summary>
        /// <returns>A List of Boolean-returning Funcs. Invoke the Funcs to test the challenge response locally.</returns>
        private List<Func<bool>> PrepareChallengeResponse_TlsSni01(IISManager iisManager, ManagedSite managedSite, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedSite.RequestConfig;
            var tlsSniChallenge = (ACMESharp.ACME.TlsSniChallenge)pendingAuth.Challenge.ChallengeData;
            var tlsSniAnswer = (ACMESharp.ACME.TlsSniChallengeAnswer)tlsSniChallenge.Answer;
            var token = Encoding.UTF8.GetBytes(tlsSniAnswer.KeyAuthorization);
            var sha256 = System.Security.Cryptography.SHA256.Create();
            var z = new byte[tlsSniChallenge.IterationCount][];

            // compute n sha256 hashes, where n=challengedata.iterationcount
            z[0] = sha256.ComputeHash(token);
            for (int i = 1; i < z.Length; i++)
            {
                z[i] = sha256.ComputeHash(z[i - 1]);
            }
            // generate certs and install iis bindings
            var cleanupQueue = new List<Action>();
            var checkQueue = new List<Func<bool>>();
            foreach (string hex in z.Select(b =>
                BitConverter.ToString(b).Replace("-", "").ToLower()))
            {
                string domain = $"{hex.Substring(0, 32)}.{hex.Substring(32)}.acme.invalid";
                this.LogAction($"Preparing binding at: https://{requestConfig.PrimaryDomain}, sni: {domain}");

                var x509 = CertificateManager.GenerateTlsSni01Certificate(domain);
                CertificateManager.StoreCertificate(x509);
                iisManager.InstallCertificateforBinding(managedSite, x509, domain);

                // add check to the queue
                checkQueue.Add(() => CheckSNI(requestConfig.PrimaryDomain, domain));

                // add cleanup actions to queue
                cleanupQueue.Add(() => iisManager.RemoveHttpsBinding(managedSite, domain));
                cleanupQueue.Add(() => CertificateManager.RemoveCertificate(x509));
            }

            // configure cleanup to execute the cleanup queue
            pendingAuth.Cleanup = () => cleanupQueue.ForEach(a => a());

            // perform our own config checks
            pendingAuth.TlsSniConfigCheckedOK = true;
            return checkQueue;
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

        /// <summary>
        /// Performs a simulated tls-sni-01 verification over HTTPS/SNI.
        /// </summary>
        /// <param name="host">The domain being verified</param>
        /// <param name="sni">The server name indication used for TLS server response</param>
        /// <param name="useProxyAPI">Whether to use the server proxy for verification</param>
        /// <returns>True if the verification is successful, False if not.</returns>
        private bool CheckSNI(string host, string sni, bool? useProxyAPI = null)
        {
            // if validation proxy enabled, access to the domain being validated is checked via
            // our remote API rather than directly on the servers
            bool useProxy = useProxyAPI ?? Certify.Properties.Settings.Default.EnableValidationProxyAPI;
            if (useProxy)
            {
                // TODO: check proxy here, needs server support. if successful "return true"; and "LogAction(...)"
                System.Diagnostics.Debug.WriteLine("ProxyAPI is not implemented for Checking SNI config, trying local");
                this.LogAction($"Proxy TLS SNI binding check error: {host}, {sni}"); 

                return CheckSNI(host, sni, false); // proxy failed, try local
            }
            var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"https://{sni}");
                ServicePointManager.ServerCertificateValidationCallback = (obj, cert, chain, errors) =>
                {
                    // verify SNI-selected certificate is correctly configured
                    return CertificateManager.VerifyCertificateSAN(cert, sni);
                };
                // modify the hosts file
                var ip = Dns.GetHostEntry(host).AddressList.First();
                string hostEntry = $"\n{ip}\t{sni}";
                using (StreamWriter writer = File.AppendText(hosts))
                {
                    writer.Write(hostEntry);
                }
                Thread.Sleep(250); // wait a bit for hostsfile to take effect
                try
                {
                    using (var client = new HttpClient())
                    {
                        var resp = client.SendAsync(req).Result;
                        // if the GET request succeeded, the Cert validation succeeded
                        this.LogAction($"Local TLS SNI binding check OK: {host}, {sni}"); ;
                    }
                }
                finally
                {
                    // clean up hosts
                    try
                    {
                        var txt = File.ReadAllText(hosts);
                        txt = txt.Substring(0, txt.Length - hostEntry.Length);
                        File.WriteAllText(hosts, txt);
                    }
                    catch
                    {
                        // if this fails the user will have to clean up manually
                        this.LogAction($"Error cleaning up hosts file: {hosts}");
                        throw;
                    }
                }
                return true; // success!
            }
            catch (Exception ex)
            {
                // eat the error that HttpClient throws, either cert validation failed
                // or the site is inaccessible via https://host name
                this.LogAction($"Local TLS SNI binding check error: {host}, {sni}\n{ex.GetType()}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
            finally
            {
                // reset the callback for other http requests
                ServicePointManager.ServerCertificateValidationCallback = null;
            }
        }

        private bool CheckURL(string url, bool? useProxyAPI = null)
        {
            // if validation proxy enabled, access to the domain being validated is checked via
            // our remote API rather than directly on the servers
            bool useProxy = useProxyAPI ?? Certify.Properties.Settings.Default.EnableValidationProxyAPI;

            //check http request to test path works
            try
            {
                var request = WebRequest.Create(!useProxy ? url : 
                    Properties.Resources.APIBaseURI + "testurlaccess?url=" + url);
                ServicePointManager.ServerCertificateValidationCallback = (obj, cert, chain, errors) =>
                {
                    // ignore all cert errors when validating URL response
                    return true;
                };
                var response = (HttpWebResponse)request.GetResponse();

                //if checking via proxy, examine result
                if (useProxy)
                {
                    if ((int)response.StatusCode >= 200)
                    {
                        var encoding = ASCIIEncoding.UTF8;
                        using (var reader = new System.IO.StreamReader(response.GetResponseStream(), encoding))
                        {
                            string jsonText = reader.ReadToEnd();
                            this.LogAction("Proxy URL Check Result: " + jsonText);
                            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.API.URLCheckResult>(jsonText);
                            if (result.IsAccessible == true)
                            {
                                return true;
                            }
                        }
                    }
                    //request failed using proxy api, request again using local http
                    return CheckURL(url, false);
                }
                else
                {
                    this.LogAction($"Local URL Check Result: HTTP {response.StatusCode}");
                    //not checking via proxy, base result on status code
                    return (int)response.StatusCode >= 200 && (int)response.StatusCode < 300;
                }
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("Failed to check url for access");
                return false;
            }
            finally
            {
                // reset callback for other requests to validate using default behavior
                ServicePointManager.ServerCertificateValidationCallback = null;
            }
        }

        #endregion Utils
    }
}