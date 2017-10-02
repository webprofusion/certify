using ACMESharp;
using ACMESharp.HTTP;
using ACMESharp.JOSE;
using ACMESharp.PKI;
using ACMESharp.PKI.RSA;
using ACMESharp.POSH.Util;
using ACMESharp.Util;
using ACMESharp.Vault;
using ACMESharp.Vault.Model;
using ACMESharp.Vault.Profile;
using ACMESharp.Vault.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

/*
 * Port of powershell methods from ACMESharp.POSH: https://github.com/ebekker/ACMESharp
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

namespace Certify.ACMESharpCompat
{
    /// <summary>
    /// Port of powershell methods from ACMESharp.POSH
    /// </summary>
    public static class ACMESharpUtils
    {
        /// <summary>
        /// Identifier validation challenge type indicator for
        /// <see cref="https://tools.ietf.org/html/draft-ietf-acme-acme-01#section-7.5">DNS</see>.
        /// </summary>
        public static readonly string CHALLENGE_TYPE_DNS = "dns-01";
        /// <summary>
        /// Identifier validation challenge type indicator for
        /// <see cref="https://tools.ietf.org/html/draft-ietf-acme-acme-01#section-7.2">HTTP (non-SSL/TLS)</see>.
        /// </summary>
        public const string CHALLENGE_TYPE_HTTP = "http-01";
        /// <summary>
        /// Identifier validation challenge type indicator for
        /// <see cref="https://tools.ietf.org/html/draft-ietf-acme-acme-01#section-7.3">TLS SNI</see>.
        /// </summary>
        public const string CHALLENGE_TYPE_SNI = "tls-sni-01";

        public const string WELL_KNOWN_LE = "LetsEncrypt";

        public const string WELL_KNOWN_LESTAGE = "LetsEncrypt-STAGING";

        public static readonly IReadOnlyDictionary<string, string> WELL_KNOWN_BASE_SERVICES =
                new ReadOnlyDictionary<string, string>(new IndexedDictionary<string, string>
                {
                    { WELL_KNOWN_LE, "https://acme-v01.api.letsencrypt.org/" },
                    { WELL_KNOWN_LESTAGE, "https://acme-staging.api.letsencrypt.org/"},
                });

        public static IVault GetVault(string profileName = null)
        {
            profileName = VaultProfileManager.ResolveProfileName(profileName);
            if (string.IsNullOrEmpty(profileName))
                throw new InvalidOperationException("unable to resolve effective profile name");

            var profile = VaultProfileManager.GetProfile(profileName);
            if (profile == null)
                throw new InvalidOperationException("unable to resolve effective profile")
                        .With(nameof(profileName), profileName);

            var provider = VaultExtManager.GetProvider(profile.ProviderName, null);
            if (provider == null)
                throw new InvalidOperationException("unable to resolve Vault Provider")
                        .With(nameof(profileName), profileName)
                        .With(nameof(profile.ProviderName), profile.ProviderName);

            return provider.GetVault(profile.VaultParameters);
        }

        public static IPkiTool GetPkiTool(string name)
        {
            return string.IsNullOrEmpty(name)
                ? PkiToolExtManager.GetPkiTool()
                : PkiToolExtManager.GetPkiTool(name);
        }

        private static void OpenVaultStorage(ACMESharp.Vault.IVault vlt, bool initOrOpen = false)
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
                    System.Threading.Thread.Sleep(200);
                }
            }
        }

        internal static AcmeRegistration NewRegistration(string alias, string[] contacts, bool acceptTOS, string Signer = "RS256", string vaultProfile = null)
        {
            lock (VaultManager.VAULT_LOCK)
            {
                using (var vlt = GetVault(vaultProfile))
                {
                    OpenVaultStorage(vlt);

                    var v = vlt.LoadVault();

                    AcmeRegistration r = null;
                    var ri = new RegistrationInfo
                    {
                        Id = EntityHelper.NewId(),
                        Alias = alias,
                        SignerProvider = Signer,
                    };

                    using (var c = ClientHelper.GetClient(v, ri))
                    {
                        c.Init();
                        c.GetDirectory(true);

                        r = c.Register(contacts);
                        if (acceptTOS)
                            r = c.UpdateRegistration(agreeToTos: true);

                        ri.Registration = r;

                        if (v.Registrations == null)
                            v.Registrations = new EntityDictionary<RegistrationInfo>();

                        v.Registrations.Add(ri);
                    }

                    vlt.SaveVault(v);
                    return r;
                }
            }
        }

        public static CertificateInfo SubmitCertificate(string certificateRef, string pkiTool = null, string vaultProfile = null, bool protectSensitiveFileStorage = false)
        {
            bool force = false;

            lock (VaultManager.VAULT_LOCK)
            {
                using (var vlt = GetVault(vaultProfile))
                {
                    OpenVaultStorage(vlt);
                    var v = vlt.LoadVault();

                    if (v.Registrations == null || v.Registrations.Count < 1)
                        throw new InvalidOperationException("No registrations found");

                    var ri = v.Registrations[0];
                    var r = ri.Registration;

                    if (v.Certificates == null || v.Certificates.Count < 1)
                        throw new InvalidOperationException("No certificates found");

                    var ci = v.Certificates.GetByRef(certificateRef, throwOnMissing: false);
                    if (ci == null)
                        throw new Exception("Unable to find a Certificate for the given reference");

                    using (var cp = GetPkiTool(
                                StringHelper.IfNullOrEmpty(pkiTool, v.PkiTool)))
                    {
                        if (!string.IsNullOrEmpty(ci.GenerateDetailsFile))
                        {
                            // Generate a private key and CSR:
                            // Key:  RSA 2048-bit
                            // MD:   SHA 256
                            // CSR:  Details pulled from CSR Details JSON file

                            CsrDetails csrDetails;
                            var csrDetailsAsset = vlt.GetAsset(VaultAssetType.CsrDetails, ci.GenerateDetailsFile);
                            using (var s = vlt.LoadAsset(csrDetailsAsset))
                            {
                                csrDetails = JsonHelper.Load<CsrDetails>(s);
                            }

                            var keyGenFile = $"{ci.Id}-gen-key.json";
                            var keyPemFile = $"{ci.Id}-key.pem";
                            var csrGenFile = $"{ci.Id}-gen-csr.json";
                            var csrPemFile = $"{ci.Id}-csr.pem";

                            var keyGenAsset = vlt.CreateAsset(VaultAssetType.KeyGen, keyGenFile, getOrCreate: force);
                            var keyPemAsset = vlt.CreateAsset(VaultAssetType.KeyPem, keyPemFile, isSensitive: protectSensitiveFileStorage, getOrCreate: force);
                            var csrGenAsset = vlt.CreateAsset(VaultAssetType.CsrGen, csrGenFile, getOrCreate: force);
                            var csrPemAsset = vlt.CreateAsset(VaultAssetType.CsrPem, csrPemFile, getOrCreate: force);

                            var genKeyParams = new RsaPrivateKeyParams();

                            var genKey = cp.GeneratePrivateKey(genKeyParams);
                            using (var s = vlt.SaveAsset(keyGenAsset))
                            {
                                cp.SavePrivateKey(genKey, s);
                            }
                            using (var s = vlt.SaveAsset(keyPemAsset))
                            {
                                cp.ExportPrivateKey(genKey, EncodingFormat.PEM, s);
                            }

                            // TODO: need to surface details of the CSR params up higher
                            var csrParams = new CsrParams
                            {
                                Details = csrDetails
                            };
                            var genCsr = cp.GenerateCsr(csrParams, genKey, Crt.MessageDigest.SHA256);
                            using (var s = vlt.SaveAsset(csrGenAsset))
                            {
                                cp.SaveCsr(genCsr, s);
                            }
                            using (var s = vlt.SaveAsset(csrPemAsset))
                            {
                                cp.ExportCsr(genCsr, EncodingFormat.PEM, s);
                            }

                            ci.KeyPemFile = keyPemFile;
                            ci.CsrPemFile = csrPemFile;
                        }

                        byte[] derRaw;

                        var asset = vlt.GetAsset(VaultAssetType.CsrPem, ci.CsrPemFile);
                        // Convert the stored CSR in PEM format to DER
                        using (var source = vlt.LoadAsset(asset))
                        {
                            var csr = cp.ImportCsr(EncodingFormat.PEM, source);
                            using (var target = new MemoryStream())
                            {
                                cp.ExportCsr(csr, EncodingFormat.DER, target);
                                derRaw = target.ToArray();
                            }
                        }

                        var derB64u = JwsHelper.Base64UrlEncode(derRaw);

                        /*try
                        {*/
                        using (var c = ClientHelper.GetClient(v, ri))
                        {
                            c.Init();
                            c.GetDirectory(true);

                            ci.CertificateRequest = c.RequestCertificate(derB64u);
                        }
                        /*}
                        catch (AcmeClient.AcmeWebException ex)
                        {
                            //throw new Exce(PoshHelper.CreateErrorRecord(ex, ci));
                            //TODO: parse exception
                            throw ex;
                            //return;
                        }*/

                        if (!string.IsNullOrEmpty(ci.CertificateRequest.CertificateContent))
                        {
                            var crtDerFile = $"{ci.Id}-crt.der";
                            var crtPemFile = $"{ci.Id}-crt.pem";

                            var crtDerBytes = ci.CertificateRequest.GetCertificateContent();

                            var crtDerAsset = vlt.CreateAsset(VaultAssetType.CrtDer, crtDerFile);
                            var crtPemAsset = vlt.CreateAsset(VaultAssetType.CrtPem, crtPemFile);

                            using (Stream source = new MemoryStream(crtDerBytes),
                                    derTarget = vlt.SaveAsset(crtDerAsset),
                                    pemTarget = vlt.SaveAsset(crtPemAsset))
                            {
                                var crt = cp.ImportCertificate(EncodingFormat.DER, source);

                                cp.ExportCertificate(crt, EncodingFormat.DER, derTarget);
                                ci.CrtDerFile = crtDerFile;

                                cp.ExportCertificate(crt, EncodingFormat.PEM, pemTarget);
                                ci.CrtPemFile = crtPemFile;
                            }

                            // Extract a few pieces of info from the issued cert that we like to have
                            // quick access to
                            var x509 = new X509Certificate2(ci.CertificateRequest.GetCertificateContent());
                            ci.SerialNumber = x509.SerialNumber;
                            ci.Thumbprint = x509.Thumbprint;
                            ci.SignatureAlgorithm = x509.SignatureAlgorithm?.FriendlyName;
                            ci.Signature = x509.GetCertHashString();
                        }
                    }

                    vlt.SaveVault(v);
                    return ci;
                }
            }
        }

        internal static AuthorizationState NewIdentifier(string alias, string dns, string vaultProfile = null)
        {
            lock (VaultManager.VAULT_LOCK)
            {
                using (var vlt = GetVault(vaultProfile))
                {
                    OpenVaultStorage(vlt);
                    var v = vlt.LoadVault();

                    if (v.Registrations == null || v.Registrations.Count < 1)
                        throw new InvalidOperationException("No registrations found");

                    var ri = v.Registrations[0];
                    var r = ri.Registration;

                    AuthorizationState authzState = null;
                    var ii = new IdentifierInfo
                    {
                        Id = EntityHelper.NewId(),
                        Alias = alias,
                        RegistrationRef = ri.Id,
                        Dns = dns,
                    };

                    using (var c = ClientHelper.GetClient(v, ri))
                    {
                        c.Init();
                        c.GetDirectory(true);

                        authzState = c.AuthorizeIdentifier(dns);
                        ii.Authorization = authzState;

                        if (v.Identifiers == null)
                            v.Identifiers = new EntityDictionary<IdentifierInfo>();

                        v.Identifiers.Add(ii);
                    }

                    vlt.SaveVault(v);

                    return authzState;
                }
            }
        }

        internal static AuthorizationState UpdateIdentifier(string domainIdentifierAlias, string challengeType = null, string vaultProfile = null)
        {
            var IdentifierRef = domainIdentifierAlias;

            lock (VaultManager.VAULT_LOCK)
            {
                using (var vlt = GetVault(vaultProfile))
                {
                    OpenVaultStorage(vlt);
                    var v = vlt.LoadVault();

                    if (v.Registrations == null || v.Registrations.Count < 1)
                        throw new InvalidOperationException("No registrations found");

                    var ri = v.Registrations[0];
                    var r = ri.Registration;

                    if (v.Identifiers == null || v.Identifiers.Count < 1)
                        throw new InvalidOperationException("No identifiers found");

                    var ii = v.Identifiers.GetByRef(IdentifierRef, throwOnMissing: false);
                    if (ii == null)
                        throw new Exception("Unable to find an Identifier for the given reference");

                    var authorizationState = ii.Authorization;

                    //try
                    //{
                    using (var c = ClientHelper.GetClient(v, ri))
                    {
                        c.Init();
                        c.GetDirectory(true);

                        if (string.IsNullOrEmpty(challengeType))
                        {
                            authorizationState = c.RefreshIdentifierAuthorization(authorizationState);
                        }
                        else
                        {
                            c.RefreshAuthorizeChallenge(authorizationState, challengeType);
                        }
                    }

                    ii.Authorization = authorizationState;
                    /*}
                    catch (AcmeClient.AcmeWebException ex)
                    {
                        throw new (PoshHelper.CreateErrorRecord(ex, ii));
                        return;
                    }*/

                    vlt.SaveVault(v);

                    return authorizationState;
                }
            }
        }

        internal static AuthorizationState SubmitChallenge(string identifierRef, string challengeType, string vaultProfile = null)
        {
            var Force = false;
            var UseBaseUri = false;

            lock (VaultManager.VAULT_LOCK)
            {
                using (var vlt = GetVault(vaultProfile))
                {
                    OpenVaultStorage(vlt);
                    var v = vlt.LoadVault();

                    if (v.Registrations == null || v.Registrations.Count < 1)
                        throw new InvalidOperationException("No registrations found");

                    var ri = v.Registrations[0];
                    var r = ri.Registration;

                    if (v.Identifiers == null || v.Identifiers.Count < 1)
                        throw new InvalidOperationException("No identifiers found");

                    var ii = v.Identifiers.GetByRef(identifierRef, throwOnMissing: false);
                    if (ii == null)
                        throw new Exception("Unable to find an Identifier for the given reference");

                    var authzState = ii.Authorization;

                    if (!Force)
                    {
                        if (!authzState.IsPending())
                            throw new InvalidOperationException(
                                    "authorization is not in pending state;"
                                    + " use Force flag to override this validation");

                        if (authzState.Challenges.Any(_ => _.IsInvalid()))
                            throw new InvalidOperationException(
                                    "authorization already contains challenges in an invalid state;"
                                    + " use Force flag to override this validation");
                    }

                    using (var c = ClientHelper.GetClient(v, ri))
                    {
                        c.Init();
                        c.GetDirectory(true);

                        var challenge = c.SubmitChallengeAnswer(authzState, challengeType, UseBaseUri);
                        ii.Challenges[challengeType] = challenge;
                    }

                    vlt.SaveVault(v);

                    return authzState;
                }
            }
        }

        internal static AuthorizationState CompleteChallenge(
            string identifierRef,
            string ChallengeType,
            string Handler,
            string HandlerProfileRef = null,
            Hashtable HandlerParameters = null,
            bool Force = false,
            bool CleanUp = false,
            bool Regenerate = false,
            bool Repeat = false,
            string vaultProfile = null
            )
        {
            lock (VaultManager.VAULT_LOCK)
            {
                using (var vlt = GetVault(vaultProfile))
                {
                    OpenVaultStorage(vlt);
                    var v = vlt.LoadVault();

                    if (v.Registrations == null || v.Registrations.Count < 1)
                        throw new InvalidOperationException("No registrations found");

                    var ri = v.Registrations[0];
                    var r = ri.Registration;

                    if (v.Identifiers == null || v.Identifiers.Count < 1)
                        throw new InvalidOperationException("No identifiers found");

                    var ii = v.Identifiers.GetByRef(identifierRef, throwOnMissing: false);
                    if (ii == null)
                        throw new Exception("Unable to find an Identifier for the given reference");

                    var authzState = ii.Authorization;

                    if (ii.Challenges == null)
                        ii.Challenges = new Dictionary<string, AuthorizeChallenge>();

                    if (ii.ChallengeCompleted == null)
                        ii.ChallengeCompleted = new Dictionary<string, DateTime?>();

                    if (ii.ChallengeCleanedUp == null)
                        ii.ChallengeCleanedUp = new Dictionary<string, DateTime?>();

                    // Resolve details from inline or profile attributes
                    string challengeType = null;
                    string handlerName = null;
                    IReadOnlyDictionary<string, object> handlerParams = null;
                    IReadOnlyDictionary<string, object> cliHandlerParams = null;

                    if (HandlerParameters?.Count > 0)
                        cliHandlerParams = (IReadOnlyDictionary<string, object>)Convert<string, object>(HandlerParameters);

                    if (!Force && !CleanUp)
                    {
                        if (!authzState.IsPending())
                            throw new InvalidOperationException(
                                    "authorization is not in pending state;"
                                    + " use Force flag to override this validation");

                        if (authzState.Challenges.Any(_ => _.IsInvalid()))
                            throw new InvalidOperationException(
                                    "authorization already contains challenges in an invalid state;"
                                    + " use Force flag to override this validation");
                    }

                    if (!string.IsNullOrEmpty(HandlerProfileRef))
                    {
                        var ppi = v.ProviderProfiles.GetByRef(HandlerProfileRef, throwOnMissing: false);
                        if (ppi == null)
                            throw new ArgumentException("no Handler profile found for the given reference")
                                    .With(nameof(HandlerProfileRef), HandlerProfileRef);

                        var ppAsset = vlt.GetAsset(ACMESharp.Vault.VaultAssetType.ProviderConfigInfo,
                                ppi.Id.ToString());
                        ProviderProfile pp;
                        using (var s = vlt.LoadAsset(ppAsset))
                        {
                            pp = JsonHelper.Load<ProviderProfile>(s);
                        }
                        if (pp.ProviderType != ProviderType.CHALLENGE_HANDLER)
                            throw new InvalidOperationException("referenced profile does not resolve to a Challenge Handler")
                                    .With(nameof(HandlerProfileRef), HandlerProfileRef)
                                    .With("actualProfileProviderType", pp.ProviderType.ToString());

                        if (!pp.ProfileParameters.ContainsKey(nameof(ChallengeType)))
                            throw new InvalidOperationException("handler profile is incomplete; missing Challenge Type")
                                    .With(nameof(HandlerProfileRef), HandlerProfileRef);

                        challengeType = (string)pp.ProfileParameters[nameof(ChallengeType)];
                        handlerName = pp.ProviderName;
                        handlerParams = pp.InstanceParameters;
                        if (cliHandlerParams != null)
                        {
                            //WriteVerbose("Override Handler parameters specified");
                            if (handlerParams == null || handlerParams.Count == 0)
                            {
                                // WriteVerbose("Profile does not define any parameters, using
                                // override parameters only");
                                handlerParams = cliHandlerParams;
                            }
                            else
                            {
                                // WriteVerbose("Merging Handler override parameters with profile");
                                var mergedParams = new Dictionary<string, object>();

                                foreach (var kv in pp.InstanceParameters)
                                    mergedParams[kv.Key] = kv.Value;
                                foreach (var kv in cliHandlerParams)
                                    mergedParams[kv.Key] = kv.Value;

                                handlerParams = mergedParams;
                            }
                        }
                    }
                    else
                    {
                        challengeType = ChallengeType;
                        handlerName = Handler;
                        handlerParams = cliHandlerParams;
                    }

                    AuthorizeChallenge challenge = null;
                    DateTime? challengeCompleted = null;
                    DateTime? challengeCleanedUp = null;
                    ii.Challenges.TryGetValue(challengeType, out challenge);
                    ii.ChallengeCompleted.TryGetValue(challengeType, out challengeCompleted);
                    ii.ChallengeCleanedUp.TryGetValue(challengeType, out challengeCleanedUp);

                    if (challenge == null || Regenerate)
                    {
                        using (var c = ClientHelper.GetClient(v, ri))
                        {
                            c.Init();
                            c.GetDirectory(true);

                            challenge = c.DecodeChallenge(authzState, challengeType);
                            ii.Challenges[challengeType] = challenge;
                        }
                    }

                    if (CleanUp && (Repeat || challengeCleanedUp == null))
                    {
                        using (var c = ClientHelper.GetClient(v, ri))
                        {
                            c.Init();
                            c.GetDirectory(true);

                            challenge = c.HandleChallenge(authzState, challengeType,
                                    handlerName, handlerParams, CleanUp);
                            ii.ChallengeCleanedUp[challengeType] = DateTime.Now;
                        }
                    }
                    else if (Repeat || challengeCompleted == null)
                    {
                        using (var c = ClientHelper.GetClient(v, ri))
                        {
                            c.Init();
                            c.GetDirectory(true);

                            challenge = c.HandleChallenge(authzState, challengeType,
                                    handlerName, handlerParams);
                            ii.ChallengeCompleted[challengeType] = DateTime.Now;
                        }
                    }

                    vlt.SaveVault(v);

                    return authzState;
                }
            }
        }

        internal static CertificateInfo NewCertificate(
                            string certAlias,
            string identifierRef,
            string[] subjectAlternativeNameIdentifiers,
            Hashtable CsrDetails = null,
            string KeyPemFile = null,
            string CsrPemFile = null,
            bool generate = true,
            string vaultProfile = null
            )
        {
            lock (VaultManager.VAULT_LOCK)
            {
                var AlternativeIdentifierRefs = subjectAlternativeNameIdentifiers;

                using (var vlt = GetVault(vaultProfile))
                {
                    OpenVaultStorage(vlt);
                    var v = vlt.LoadVault();

                    if (v.Registrations == null || v.Registrations.Count < 1)
                        throw new InvalidOperationException("No registrations found");

                    var ri = v.Registrations[0];
                    var r = ri.Registration;

                    if (v.Identifiers == null || v.Identifiers.Count < 1)
                        throw new InvalidOperationException("No identifiers found");

                    var ii = v.Identifiers.GetByRef(identifierRef, throwOnMissing: false);
                    if (ii == null)
                        throw new Exception("Unable to find an Identifier for the given reference");

                    var ci = new CertificateInfo
                    {
                        Id = EntityHelper.NewId(),
                        Alias = certAlias,
                        IdentifierRef = ii.Id,
                        IdentifierDns = ii.Dns,
                    };

                    if (generate)
                    {
                        Func<string, string> csrDtlValue = x => null;
                        Func<string, IEnumerable<string>> csrDtlValues = x => null;

                        if (CsrDetails != null)
                        {
                            csrDtlValue = x => CsrDetails.ContainsKey(x)
                                    ? CsrDetails[x] as string : null;
                            csrDtlValues = x => !string.IsNullOrEmpty(csrDtlValue(x))
                                    ? Regex.Split(csrDtlValue(x).Trim(), "[\\s,;]+") : null;
                        }

                        var csrDetails = new CsrDetails
                        {
                            // Common Name is always pulled from associated Identifier
                            CommonName = ii.Dns,

                            // Remaining elements will be used if defined
                            AlternativeNames /**/ = csrDtlValues(nameof(ACMESharp.PKI.CsrDetails.AlternativeNames)),
                            Country          /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.Country)),
                            Description      /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.Description)),
                            Email            /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.Email)),
                            GivenName        /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.GivenName)),
                            Initials         /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.Initials)),
                            Locality         /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.Locality)),
                            Organization     /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.Organization)),
                            OrganizationUnit /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.OrganizationUnit)),
                            SerialNumber     /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.SerialNumber)),
                            StateOrProvince  /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.StateOrProvince)),
                            Surname          /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.Surname)),
                            Title            /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.Title)),
                            UniqueIdentifier /**/ = csrDtlValue(nameof(ACMESharp.PKI.CsrDetails.UniqueIdentifier)),
                        };

                        if (AlternativeIdentifierRefs != null)
                        {
                            if (csrDetails.AlternativeNames != null)
                                throw new Exception("Alternative names already specified manually")
                                        .With(nameof(csrDetails.AlternativeNames),
                                                string.Join(",", csrDetails.AlternativeNames));

                            csrDetails.AlternativeNames = AlternativeIdentifierRefs.Select(alternativeIdentifierRef =>
                            {
                                var altId = v.Identifiers.GetByRef($"{alternativeIdentifierRef}", throwOnMissing: false);
                                if (altId == null)
                                    throw new Exception("Unable to find an Identifier for the given Alternative Identifier reference")
                                            .With(nameof(alternativeIdentifierRef), alternativeIdentifierRef)
                                            .With(nameof(AlternativeIdentifierRefs),
                                                    string.Join(",", AlternativeIdentifierRefs));
                                return altId.Dns;
                            });

                            ci.AlternativeIdentifierDns = csrDetails.AlternativeNames.ToArray();
                        }

                        ci.GenerateDetailsFile = $"{ci.Id}-gen.json";
                        var asset = vlt.CreateAsset(VaultAssetType.CsrDetails, ci.GenerateDetailsFile);
                        using (var s = vlt.SaveAsset(asset))
                        {
                            JsonHelper.Save(s, csrDetails);
                        }
                    }
                    else
                    {
                        if (!File.Exists(KeyPemFile))
                            throw new FileNotFoundException("Missing specified RSA Key file path");
                        if (!File.Exists(CsrPemFile))
                            throw new FileNotFoundException("Missing specified CSR details file path");

                        var keyPemFile = $"{ci.Id}-key.pem";
                        var csrPemFile = $"{ci.Id}-csr.pem";

                        var keyAsset = vlt.CreateAsset(VaultAssetType.KeyPem, keyPemFile, true);
                        var csrAsset = vlt.CreateAsset(VaultAssetType.CsrPem, csrPemFile);

                        using (Stream fs = new FileStream(KeyPemFile, FileMode.Open),
                                s = vlt.SaveAsset(keyAsset))
                        {
                            fs.CopyTo(s);
                        }
                        using (Stream fs = new FileStream(CsrPemFile, FileMode.Open),
                                s = vlt.SaveAsset(csrAsset))
                        {
                            fs.CopyTo(s);
                        }

                        ci.KeyPemFile = keyPemFile;
                        ci.CsrPemFile = csrPemFile;
                    }

                    if (v.Certificates == null)
                        v.Certificates = new EntityDictionary<CertificateInfo>();

                    v.Certificates.Add(ci);

                    vlt.SaveVault(v);

                    return ci;
                }
            }
        }

        internal static CertificateInfo UpdateCertificate(string certificateRef, string PkiTool = null, string vaultProfile = null)
        {
            bool UseBaseUri = false;
            bool Repeat = false;

            lock (VaultManager.VAULT_LOCK)
            {
                using (var vlt = GetVault(vaultProfile))
                {
                    OpenVaultStorage(vlt);
                    var v = vlt.LoadVault();

                    if (v.Registrations == null || v.Registrations.Count < 1)
                        throw new InvalidOperationException("No registrations found");

                    var ri = v.Registrations[0];
                    var r = ri.Registration;

                    if (v.Certificates == null || v.Certificates.Count < 1)
                        throw new InvalidOperationException("No certificates found");

                    var ci = v.Certificates.GetByRef(certificateRef, throwOnMissing: false);
                    if (ci == null)
                        throw new Exception("Unable to find a Certificate for the given reference");

                    if (ci.CertificateRequest == null)
                        throw new Exception("Certificate has not been submitted yet; cannot update status");

                    using (var c = ClientHelper.GetClient(v, ri))
                    {
                        c.Init();
                        c.GetDirectory(true);

                        c.RefreshCertificateRequest(ci.CertificateRequest, UseBaseUri);
                    }

                    if ((Repeat || string.IsNullOrEmpty(ci.CrtPemFile))
                            && !string.IsNullOrEmpty(ci.CertificateRequest.CertificateContent))
                    {
                        var crtDerFile = $"{ci.Id}-crt.der";
                        var crtPemFile = $"{ci.Id}-crt.pem";

                        var crtDerAsset = vlt.ListAssets(crtDerFile, VaultAssetType.CrtDer).FirstOrDefault();
                        var crtPemAsset = vlt.ListAssets(crtPemFile, VaultAssetType.CrtPem).FirstOrDefault();

                        if (crtDerAsset == null)
                            crtDerAsset = vlt.CreateAsset(VaultAssetType.CrtDer, crtDerFile);
                        if (crtPemAsset == null)
                            crtPemAsset = vlt.CreateAsset(VaultAssetType.CrtPem, crtPemFile);

                        using (var cp = PkiHelper.GetPkiTool(
                            StringHelper.IfNullOrEmpty(PkiTool, v.PkiTool)))
                        {
                            var bytes = ci.CertificateRequest.GetCertificateContent();

                            using (Stream source = new MemoryStream(bytes),
                                    derTarget = vlt.SaveAsset(crtDerAsset),
                                    pemTarget = vlt.SaveAsset(crtPemAsset))
                            {
                                var crt = cp.ImportCertificate(EncodingFormat.DER, source);

                                // We're saving the DER format cert "through" the CP in order to
                                // validate its content
                                cp.ExportCertificate(crt, EncodingFormat.DER, derTarget);
                                ci.CrtDerFile = crtDerFile;

                                cp.ExportCertificate(crt, EncodingFormat.PEM, pemTarget);
                                ci.CrtPemFile = crtPemFile;
                            }
                        }

                        var x509 = new X509Certificate2(ci.CertificateRequest.GetCertificateContent());
                        ci.SerialNumber = x509.SerialNumber;
                        ci.Thumbprint = x509.Thumbprint;
                        ci.SignatureAlgorithm = x509.SignatureAlgorithm?.FriendlyName;
                        ci.Signature = x509.GetCertHashString();
                    }

                    if (Repeat || string.IsNullOrEmpty(ci.IssuerSerialNumber))
                    {
                        var linksEnum = ci.CertificateRequest.Links;
                        if (linksEnum != null)
                        {
                            var links = new LinkCollection(linksEnum);
                            var upLink = links.GetFirstOrDefault("up");
                            if (upLink != null)
                            {
                                // We need to save the ICA certificate to a local temp file so that
                                // we can read it in and store it properly as a vault asset through a stream
                                var tmp = Path.GetTempFileName();
                                try
                                {
                                    using (var web = new WebClient())
                                    {
                                        if (v.Proxy != null)
                                            web.Proxy = v.Proxy.GetWebProxy();

                                        var uri = new Uri(new Uri(v.BaseUri), upLink.Uri);
                                        web.DownloadFile(uri, tmp);
                                    }

                                    var cacert = new X509Certificate2(tmp);
                                    var sernum = cacert.GetSerialNumberString();
                                    var tprint = cacert.Thumbprint;
                                    var sigalg = cacert.SignatureAlgorithm?.FriendlyName;
                                    var sigval = cacert.GetCertHashString();

                                    if (v.IssuerCertificates == null)
                                        v.IssuerCertificates = new OrderedNameMap<IssuerCertificateInfo>();
                                    if (Repeat || !v.IssuerCertificates.ContainsKey(sernum))
                                    {
                                        var cacertDerFile = $"ca-{sernum}-crt.der";
                                        var cacertPemFile = $"ca-{sernum}-crt.pem";
                                        var issuerDerAsset = vlt.ListAssets(cacertDerFile,
                                                VaultAssetType.IssuerDer).FirstOrDefault();
                                        var issuerPemAsset = vlt.ListAssets(cacertPemFile,
                                                VaultAssetType.IssuerPem).FirstOrDefault();

                                        if (Repeat || issuerDerAsset == null)
                                        {
                                            if (issuerDerAsset == null)
                                                issuerDerAsset = vlt.CreateAsset(VaultAssetType.IssuerDer, cacertDerFile);
                                            using (Stream fs = new FileStream(tmp, FileMode.Open),
                                                s = vlt.SaveAsset(issuerDerAsset))
                                            {
                                                fs.CopyTo(s);
                                            }
                                        }
                                        if (Repeat || issuerPemAsset == null)
                                        {
                                            if (issuerPemAsset == null)
                                                issuerPemAsset = vlt.CreateAsset(VaultAssetType.IssuerPem, cacertPemFile);

                                            using (var cp = PkiHelper.GetPkiTool(
                                                StringHelper.IfNullOrEmpty(PkiTool, v.PkiTool)))
                                            {
                                                using (Stream source = vlt.LoadAsset(issuerDerAsset),
                                                    target = vlt.SaveAsset(issuerPemAsset))
                                                {
                                                    var crt = cp.ImportCertificate(EncodingFormat.DER, source);
                                                    cp.ExportCertificate(crt, EncodingFormat.PEM, target);
                                                }
                                            }
                                        }

                                        v.IssuerCertificates[sernum] = new IssuerCertificateInfo
                                        {
                                            SerialNumber = sernum,
                                            Thumbprint = tprint,
                                            SignatureAlgorithm = sigalg,
                                            Signature = sigval,
                                            CrtDerFile = cacertDerFile,
                                            CrtPemFile = cacertPemFile,
                                        };
                                    }

                                    ci.IssuerSerialNumber = sernum;
                                }
                                finally
                                {
                                    if (File.Exists(tmp))
                                        File.Delete(tmp);
                                }
                            }
                        }
                    }

                    vlt.SaveVault(v);

                    return ci;
                }
            }
        }

        internal static void GetCertificate(
                    string certRef,
            string ExportKeyPEM = null,
            string ExportCsrPEM = null,
            string ExportCertificatePEM = null,
            string ExportCertificateDER = null,
            string ExportIssuerPEM = null,
            string ExportIssuerDER = null,
            string ExportPkcs12 = null,
            string CertificatePassword = null,
            string PkiTool = null,
            bool overwrite = false,
            string vaultProfile = null
            )
        {
            lock (VaultManager.VAULT_LOCK)
            {
                using (var vlt = GetVault(vaultProfile))
                {
                    OpenVaultStorage(vlt);
                    var v = vlt.LoadVault();

                    if (v.Registrations == null || v.Registrations.Count < 1)
                        throw new InvalidOperationException("No registrations found");

                    var ri = v.Registrations[0];
                    var r = ri.Registration;

                    /*  if (string.IsNullOrEmpty(certRef))
                      {
                          int seq = 0;
                          WriteObject(v.Certificates.Values.Select(x => new
                          {
                              Seq = seq++,
                              x.Id,
                              x.Alias,
                              x.Label,
                              x.IdentifierDns,
                              x.Thumbprint,
                              x.SerialNumber,
                              x.IssuerSerialNumber,
                              x.CertificateRequest,
                              x.CertificateRequest?.StatusCode,
                          }), true);
                      }
                      else
                      {*/
                    if (v.Certificates == null || v.Certificates.Count < 1)
                        throw new InvalidOperationException("No certificates found");

                    var ci = v.Certificates.GetByRef(certRef, throwOnMissing: false);
                    if (ci == null)
                        throw new ArgumentOutOfRangeException("Unable to find a Certificate for the given reference");

                    var mode = overwrite ? FileMode.Create : FileMode.CreateNew;

                    if (!string.IsNullOrEmpty(ExportKeyPEM))
                    {
                        if (string.IsNullOrEmpty(ci.KeyPemFile))
                            throw new InvalidOperationException("Cannot export private key; it hasn't been imported or generated");
                        CopyTo(vlt, VaultAssetType.KeyPem, ci.KeyPemFile, ExportKeyPEM, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportCsrPEM))
                    {
                        if (string.IsNullOrEmpty(ci.CsrPemFile))
                            throw new InvalidOperationException("Cannot export CSR; it hasn't been imported or generated");
                        CopyTo(vlt, VaultAssetType.CsrPem, ci.CsrPemFile, ExportCsrPEM, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportCertificatePEM))
                    {
                        if (ci.CertificateRequest == null || string.IsNullOrEmpty(ci.CrtPemFile))
                            throw new InvalidOperationException("Cannot export CRT; CSR hasn't been submitted or CRT hasn't been retrieved");
                        CopyTo(vlt, VaultAssetType.CrtPem, ci.CrtPemFile, ExportCertificatePEM, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportCertificateDER))
                    {
                        if (ci.CertificateRequest == null || string.IsNullOrEmpty(ci.CrtDerFile))
                            throw new InvalidOperationException("Cannot export CRT; CSR hasn't been submitted or CRT hasn't been retrieved");
                        CopyTo(vlt, VaultAssetType.CrtDer, ci.CrtDerFile, ExportCertificateDER, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportIssuerPEM))
                    {
                        if (ci.CertificateRequest == null || string.IsNullOrEmpty(ci.CrtPemFile))
                            throw new InvalidOperationException("Cannot export CRT; CSR hasn't been submitted or CRT hasn't been retrieved");
                        if (string.IsNullOrEmpty(ci.IssuerSerialNumber) || !v.IssuerCertificates.ContainsKey(ci.IssuerSerialNumber))
                            throw new InvalidOperationException("Issuer certificate hasn't been resolved");
                        CopyTo(vlt, VaultAssetType.IssuerPem,
                                v.IssuerCertificates[ci.IssuerSerialNumber].CrtPemFile,
                                ExportIssuerPEM, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportIssuerDER))
                    {
                        if (ci.CertificateRequest == null || string.IsNullOrEmpty(ci.CrtDerFile))
                            throw new InvalidOperationException("Cannot export CRT; CSR hasn't been submitted or CRT hasn't been retrieved");
                        if (string.IsNullOrEmpty(ci.IssuerSerialNumber) || !v.IssuerCertificates.ContainsKey(ci.IssuerSerialNumber))
                            throw new InvalidOperationException("Issuer certificate hasn't been resolved");
                        CopyTo(vlt, VaultAssetType.IssuerDer,
                                v.IssuerCertificates[ci.IssuerSerialNumber].CrtDerFile,
                                ExportIssuerDER, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportPkcs12))
                    {
                        if (string.IsNullOrEmpty(ci.KeyPemFile))
                            throw new InvalidOperationException("Cannot export PKCS12; private hasn't been imported or generated");
                        if (string.IsNullOrEmpty(ci.CrtPemFile))
                            throw new InvalidOperationException("Cannot export PKCS12; CSR hasn't been submitted or CRT hasn't been retrieved");
                        if (string.IsNullOrEmpty(ci.IssuerSerialNumber) || !v.IssuerCertificates.ContainsKey(ci.IssuerSerialNumber))
                            throw new InvalidOperationException("Cannot export PKCS12; Issuer certificate hasn't been resolved");

                        var keyPemAsset = vlt.GetAsset(VaultAssetType.KeyPem, ci.KeyPemFile);
                        var crtPemAsset = vlt.GetAsset(VaultAssetType.CrtPem, ci.CrtPemFile);
                        var isuPemAsset = vlt.GetAsset(VaultAssetType.IssuerPem,
                                v.IssuerCertificates[ci.IssuerSerialNumber].CrtPemFile);

                        using (var cp = PkiHelper.GetPkiTool(
                            StringHelper.IfNullOrEmpty(PkiTool, v.PkiTool)))
                        {
                            using (Stream keyStream = vlt.LoadAsset(keyPemAsset),
                                crtStream = vlt.LoadAsset(crtPemAsset),
                                isuStream = vlt.LoadAsset(isuPemAsset),
                                fs = new FileStream(ExportPkcs12, mode))
                            {
                                var pk = cp.ImportPrivateKey<RsaPrivateKey>(EncodingFormat.PEM, keyStream);
                                var crt = cp.ImportCertificate(EncodingFormat.PEM, crtStream);
                                var isu = cp.ImportCertificate(EncodingFormat.PEM, isuStream);

                                var certs = new[] { crt, isu };

                                var password = string.IsNullOrWhiteSpace(CertificatePassword)
                                    ? string.Empty
                                    : CertificatePassword;

                                cp.ExportArchive(pk, certs, ArchiveFormat.PKCS12, fs, password);
                            }
                        }
                    }

                    /*  WriteObject(ci);
                  }*/
                }
            }
        }

        internal static IDictionary<K, V> Convert<K, V>(this Hashtable h, IDictionary<K, V> d = null)
        {
            if (h == null)
                return d;

            if (d == null)
                d = new Dictionary<K, V>();

            foreach (var k in h.Keys)
                d.Add((K)k, (V)h[k]);

            return d;
        }

        internal static void CopyTo(IVault vlt, VaultAssetType vat, string van, string target, FileMode mode)
        {
            var asset = vlt.GetAsset(vat, van);
            using (Stream s = vlt.LoadAsset(asset),
                    fs = new FileStream(target, mode))
            {
                s.CopyTo(fs);
            }
        }
    }
}