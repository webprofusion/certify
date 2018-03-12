using ACMESharp;
using ACMESharp.Vault.Model;
using ACMESharp.Vault.Providers;
using ACMESharp.Vault.Util;
using Certify.ACMESharpCompat;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Shared;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Certify
{
    public class ACMESharpManager : ActionLogCollector
    {
        private VaultInfo vaultConfig;
        internal string vaultFolderPath;

        private string vaultProfile = null;

        internal static object VAULT_LOCK = new Guid("d776b116-5695-4c3a-94c5-e67b7714fb28");

        private readonly IdnMapping idnMapping = new IdnMapping();

        public bool UseEFSForSensitiveFiles { get; set; } = false;

        public string VaultFolderPath
        {
            get { return vaultFolderPath; }
        }

        #region Vault

        public ACMESharpManager(string vaultFolderPath)
        {
            Management.Util.SetSupportedTLSVersions();
            this.vaultFolderPath = vaultFolderPath;

#if DEBUG
            this.InitVault(staging: true);
#else
            this.InitVault(staging: false);
#endif
            ReloadVaultConfig();

            //register default PKI provider (BouncyCastle)
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
                catch (System.IO.IOException exp)
                {
                    maxAttempts--;
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("Failed to open vault, retrying.." + exp.ToString());
#endif
                    Thread.Sleep(500);
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
                        //find all orphaned identified or identifiers with no certificate
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

                    // Remove VaultInfo.ServerDirectory.* where * value contains
                    // "adding-random-entries-to-the-directory" v.ServerDirectory.
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

        public StatusMessage NewCertificate(string domainIdentifierAlias, string certAlias, string[] subjectAlternativeNameIdentifiers)
        {
            //if (subjectAlternativeNameIdentifiers != null) cmd.AlternativeIdentifierRefs = subjectAlternativeNameIdentifiers;
            // cmd.Generate = new System.Management.Automation.SwitchParameter(true);

            try
            {
                var result = ACMESharpUtils.NewCertificate(certAlias, domainIdentifierAlias, subjectAlternativeNameIdentifiers);

                return new StatusMessage { IsOK = true, Result = result };
            }
            catch (Exception exp)
            {
                return new StatusMessage { IsOK = false, Message = exp.ToString(), Result = exp };
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

        public StatusMessage SubmitCertificate(string certAlias)
        {
            try
            {
                var result = ACMESharpUtils.SubmitCertificate(certAlias, protectSensitiveFileStorage: UseEFSForSensitiveFiles);

                return new StatusMessage { IsOK = true, Result = result };
            }
            catch (Exception exp)
            {
                if (exp is ACMESharp.AcmeClient.AcmeWebException)
                {
                    var aex = (ACMESharp.AcmeClient.AcmeWebException)exp;
                    return new StatusMessage { IsOK = false, Message = aex.Message, Result = aex };
                }
                else
                {
                    return new StatusMessage { IsOK = false, Message = exp.Message, Result = exp };
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

        public async Task<StatusMessage> RevokeCertificate(string pfxPath)
        {
            var fi = new FileInfo(pfxPath);
            string certAlias = fi.Name.Replace("-all.pfx", "");
            return await Task<StatusMessage>.Run(() =>
            {
                try
                {
                    return new StatusMessage()
                    {
                        IsOK = true,
                        Result = ACMESharpUtils.RevokeCertificate(certAlias)
                    };
                }
                catch (Exception ex)
                {
                    return new StatusMessage()
                    {
                        IsOK = false,
                        FailedItemSummary = new List<string>() { $"Certificate revocation error: {ex.Message}" },
                        Message = ex.Message
                    };
                }
            });
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
            catch (Exception exp)
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

        public AuthorizationState UpdateIdentifier(string domainIdentifierAlias)
        {
            return ACMESharpUtils.UpdateIdentifier(domainIdentifierAlias);
        }

        public AuthorizationState SubmitChallenge(string alias, string challengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP)
        {
            //well known challenge all ready to be read by server
            return ACMESharpUtils.SubmitChallenge(alias, challengeType);
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

            // ACME service requires international domain names in ascii mode, create new identifier
            // in vault
            try
            {
                var authState = ACMESharpUtils.NewIdentifier(identifierAlias, idnMapping.GetAscii(domain));
            }
            catch (ACMESharp.AcmeClient.AcmeWebException exp)
            {
                //if we don't know the problem details, report the whole exception
                if (exp.Response?.ProblemDetail == null) throw exp;

                // failed to register the domain identifier with LE (invalid, rate limit or CAA fail?)
                LogAction("NewIdentifier [" + domain + "]", exp.Response.ProblemDetail.OrignalContent);
                return new PendingAuthorization { AuthorizationError = $"{exp.Response.ProblemDetail.Detail} : {exp.Response.ProblemDetail.Type}" };
            }
            catch (Exception exp)
            {
                // failed to register the domain identifier with LE (rate limit or CAA fail?)
                LogAction("NewIdentifier [" + domain + "]", exp.ToString());
                return new PendingAuthorization { AuthorizationError = exp.ToString() };
            }

            Thread.Sleep(200);

            var identifier = this.GetIdentifier(identifierAlias, reloadVaultConfig: true);

            //FIXME: when validating subsequent SAN names in parallel request mode, the identifier is null?
            if (identifier != null && identifier.Authorization != null && identifier.Authorization.IsPending())
            {
                var authState = ACMESharpUtils.CompleteChallenge(identifier.Alias, challengeType, Handler: "manual", Regenerate: true, Repeat: true);
                LogAction("CompleteChallenge", authState.Status);

                //get challenge info for this identifier
                identifier = GetIdentifier(identifierAlias, reloadVaultConfig: true);
                try
                {
                    //identifier challenge specification is now ready for use to prepare and answer for LetsEncrypt to check

                    var challenges = new List<AuthorizationChallengeItem>();
                    foreach (var c in identifier.Challenges)
                    {
                        if (c.Value.Type == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                        {
                            var httpChallenge = (ACMESharp.ACME.HttpChallenge)c.Value.Challenge;

                            challenges.Add(new AuthorizationChallengeItem
                            {
                                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
                                ChallengeData = c.Value,
                                ResourcePath = httpChallenge.FilePath,
                                ResourceUri = httpChallenge.FileUrl,
                                Key = c.Value.Token,
                                Value = httpChallenge.FileContent
                            });
                        }

                        if (c.Value.Type == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
                        {
                            var tlsSniChallenge = (ACMESharp.ACME.TlsSniChallenge)c.Value.Challenge;
                            var tlsSniAnswer = (ACMESharp.ACME.TlsSniChallengeAnswer)tlsSniChallenge.Answer;

                            challenges.Add(new AuthorizationChallengeItem
                            {
                                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_SNI,
                                ChallengeData = tlsSniChallenge,
                                Key = tlsSniChallenge.Token,
                                Value = tlsSniAnswer.KeyAuthorization,
                                HashIterationCount = tlsSniChallenge.IterationCount
                            });
                        }

                        //TODO: dns
                        if (c.Value.Type == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                        {
                            var dnsChallenge = (ACMESharp.ACME.DnsChallenge)c.Value.Challenge;

                            challenges.Add(new AuthorizationChallengeItem
                            {
                                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                ChallengeData = dnsChallenge,
                                Key = dnsChallenge.RecordName,
                                Value = dnsChallenge.RecordValue
                            });
                        }
                    }

                    return new PendingAuthorization()
                    {
                        Challenges = challenges,
                        Identifier = GetDomainIdentifierItemFromIdentifierInfo(identifier)
                    };
                }
                catch (Exception exp)
                {
                    //identifier challenge could not be requested this time

                    LogAction("GetIdentifier", exp.ToString());

                    return null;
                }
            }
            else
            {
                //identifier is null or already valid (previously authorized)
                return new PendingAuthorization()
                {
                    Challenges = null,
                    Identifier = GetDomainIdentifierItemFromIdentifierInfo(identifier),
                    LogItems = this.GetActionLogSummary()
                };
            }
        }

        public IdentifierItem GetDomainIdentifierItemFromIdentifierInfo(ACMESharp.Vault.Model.IdentifierInfo identifier)
        {
            if (identifier == null) return null;

            var i = new IdentifierItem
            {
                Id = identifier.Id.ToString(),
                Alias = identifier.Alias,
                Name = identifier.Dns,
                Dns = identifier.Dns,
                Status = identifier.Authorization?.Status
            };

            if (identifier.Authorization != null)
            {
                i.AuthorizationExpiry = identifier.Authorization.Expires;
                i.IsAuthorizationPending = identifier.Authorization.IsPending();

                if (identifier.Authorization.Status == "invalid")
                {
                    var failedChallenge = identifier.Authorization.Challenges?.FirstOrDefault(c => c.ChallengePart?.Error != null);
                    if (failedChallenge != null)
                    {
                        i.ValidationError = String.Join("\r\n", failedChallenge.ChallengePart.Error);
                        i.ValidationErrorType = failedChallenge.ChallengePart.Error["type"];
                    }
                }
            }
            return i;
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

                try
                {
                    var failedChallenge = identiferStatus.Authorization.Challenges.FirstOrDefault(c => c.ChallengePart.Error != null);
                    // throw new Exception(String.Join("\r\n", failedChallenge.ChallengePart.Error));

                    if (failedChallenge != null)
                    {
                        LogAction(String.Join("\r\n", failedChallenge.ChallengePart.Error));
                    }
                    else
                    {
                        LogAction("Challenge could not complete.");
                    }
                }
                catch (Exception exp)
                {
                    LogAction(exp.ToString());
                }

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
            //given a validated primary domain identifier and optionally a set of subject alternative names, we can begin to request a certificate
            var certAlias = "cert_" + domainIdentifierRef;

            //register cert placeholder in vault

            var certRegResult = this.NewCertificate(domainIdentifierRef, certAlias, subjectAlternativeNameIdentifiers: alternativeIdentifierRefs);

            if (!certRegResult.IsOK)
            {
                return new ProcessStepResult { IsSuccess = false, ErrorMessage = "Failed to begin request for new certificate from LetsEncrypt :: " + certRegResult.Message };
            }

            //ask LE to issue a certificate for our domain(s)
            //if this step fails we should quit and try again later
            var certRequestResult = this.SubmitCertificate(certAlias);

            if (certRequestResult.IsOK)
            {
                //LE may now have issued a certificate, this process may not be immediate
                var certDetails = this.GetCertificate(certAlias, reloadVaultConfig: true);
                var attempts = 0;
                var maxAttempts = 5;

                //cert not issued yet, wait and try again
                while ((certDetails == null || String.IsNullOrEmpty(certDetails.IssuerSerialNumber)) && attempts < maxAttempts)
                {
                    System.Threading.Thread.Sleep(3000); //wait a couple of seconds before checking again
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

                    //if file already exists we want to delete the old one
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

        #endregion Utils
    }
}