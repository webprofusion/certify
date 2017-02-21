using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACMESharp.Vault.Model;
using ACMESharp.Vault.Util;
using ACMESharp;
using System.Collections.ObjectModel;
using ACMESharp.POSH;
using ACMESharp.POSH.Util;
using System.Collections;
using ACMESharp.Vault.Profile;
using ACMESharp.Util;

namespace Certify.Management
{
    public class ACMEVaultManager : VaultManager
    {
        /// <summary>
        /// Adapted from ACMESharp.POSH
        /// </summary>
        /// 

        public ACMEVaultManager(string vaultFolderPath, string vaultFilename) : base(vaultFolderPath, vaultFilename) { }

        private void WriteVerbose(string msg)
        {
            System.Diagnostics.Debug.WriteLine(msg);
        }

        private void ThrowTerminatingError(System.Management.Automation.ErrorRecord err)
        {
            throw new Exception(err.ToString());
        }

        public void InitializeVault(string baseUri, string alias = null, string label = null, string memo = null, string baseService = ACMESharp.POSH.InitializeVault.WELL_KNOWN_LE, string vaultProfile = null, bool force = false)
        {

            if (string.IsNullOrEmpty(baseUri))
            {
                if (!string.IsNullOrEmpty(baseService) && ACMESharp.POSH.InitializeVault.WELL_KNOWN_BASE_SERVICES.ContainsKey(baseService))
                {
                    baseUri = ACMESharp.POSH.InitializeVault.WELL_KNOWN_BASE_SERVICES[baseService];
                    WriteVerbose($"Resolved Base URI from Base Service [{baseUri}]");
                }
                else
                {
                    throw new InvalidOperationException("either a base service or URI is required");
                }

            }
            using (var vlt = VaultHelper.GetVault(vaultProfile))
            {
                WriteVerbose("Initializing Vault Storage Backend");
                vlt.InitStorage(force);
                var v = new VaultInfo
                {
                    Id = EntityHelper.NewId(),
                    Alias = alias,
                    Label = label,
                    Memo = memo,
                    BaseService = baseService,
                    BaseUri = baseUri,
                    ServerDirectory = new AcmeServerDirectory()
                };

                vlt.SaveVault(v);
            }
        }

        public void NewRegistration(string[] contacts, bool acceptTos, string alias = null, string label = null, string memo = null, string signer = "RS256", string vaultProfile = null)
        {
            using (var vlt = VaultHelper.GetVault(vaultProfile))
            {
                vlt.OpenStorage();
                var v = vlt.LoadVault();

                AcmeRegistration r = null;
                var ri = new RegistrationInfo
                {
                    Id = EntityHelper.NewId(),
                    Alias = alias,
                    Label = label,
                    Memo = memo,
                    SignerProvider = signer
                };

                try
                {
                    using (var c = ClientHelper.GetClient(v, ri))
                    {
                        c.Init();
                        c.GetDirectory(true);

                        r = c.Register(contacts);
                        if (acceptTos)
                            r = c.UpdateRegistration(agreeToTos: true);

                        ri.Registration = r;

                        if (v.Registrations == null)
                            v.Registrations = new EntityDictionary<RegistrationInfo>();

                        v.Registrations.Add(ri);
                    }
                }
                catch (AcmeClient.AcmeWebException ex)
                {
                    ThrowTerminatingError(PoshHelper.CreateErrorRecord(ex, ri));
                    return;
                }

                vlt.SaveVault(v);
            }

        }

        public void NewIdentifier(string dns, string alias = null, string label = null, string memo = null, string vaultProfile = null)
        {
            using (var vlt = VaultHelper.GetVault(vaultProfile))
            {
                vlt.OpenStorage();
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
                    Label = label,
                    Memo = memo,
                    RegistrationRef = ri.Id,
                    Dns = dns
                };

                try
                {
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
                }
                catch (AcmeClient.AcmeWebException ex)
                {
                    ThrowTerminatingError(PoshHelper.CreateErrorRecord(ex, ii));
                    return;
                }

                vlt.SaveVault(v);
            }
        }

        public void CompleteChallenge(string identifierRef, string ChallengeType, Hashtable handlerParameters, string handler, string handlerProfileRef, bool force = false, bool cleanUp = false, bool regenerate=false, bool repeat=false, string vaultProfile = null)
        {
            using (var vlt = VaultHelper.GetVault(vaultProfile))
            {
                vlt.OpenStorage();
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

                if (handlerParameters?.Count > 0)
                    cliHandlerParams = (IReadOnlyDictionary<string, object>
                                    )PoshHelper.Convert<string, object>(handlerParameters);

                if (!force && !cleanUp)
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

                if (!string.IsNullOrEmpty(handlerProfileRef))
                {
                    var ppi = v.ProviderProfiles.GetByRef(handlerProfileRef, throwOnMissing: false);
                    if (ppi == null)
                        throw new ArgumentException("no Handler profile found for the given reference");
                    //   .With(nameof(HandlerProfileRef), HandlerProfileRef);

                    var ppAsset = vlt.GetAsset(ACMESharp.Vault.VaultAssetType.ProviderConfigInfo,
                            ppi.Id.ToString());
                    ProviderProfile pp;
                    using (var s = vlt.LoadAsset(ppAsset))
                    {
                        pp = JsonHelper.Load<ProviderProfile>(s);
                    }
                    if (pp.ProviderType != ProviderType.CHALLENGE_HANDLER)
                        throw new InvalidOperationException("referenced profile does not resolve to a Challenge Handler")
                                .With(nameof(handlerProfileRef), handlerProfileRef)
                                .With("actualProfileProviderType", pp.ProviderType.ToString());

                    if (!pp.ProfileParameters.ContainsKey(nameof(ChallengeType)))
                        throw new InvalidOperationException("handler profile is incomplete; missing Challenge Type")
                                .With(nameof(handlerProfileRef), handlerProfileRef);

                    challengeType = (string)pp.ProfileParameters[nameof(ChallengeType)];
                    handlerName = pp.ProviderName;
                    handlerParams = pp.InstanceParameters;
                    if (cliHandlerParams != null)
                    {
                        WriteVerbose("Override Handler parameters specified");
                        if (handlerParams == null || handlerParams.Count == 0)
                        {
                            WriteVerbose("Profile does not define any parameters, using override parameters only");
                            handlerParams = cliHandlerParams;
                        }
                        else
                        {
                            WriteVerbose("Merging Handler override parameters with profile");
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
                    handlerName = handler;
                    handlerParams = cliHandlerParams;
                }

                AuthorizeChallenge challenge = null;
                DateTime? challengeCompleted = null;
                DateTime? challengeCleanedUp = null;
                ii.Challenges.TryGetValue(challengeType, out challenge);
                ii.ChallengeCompleted.TryGetValue(challengeType, out challengeCompleted);
                ii.ChallengeCleanedUp.TryGetValue(challengeType, out challengeCleanedUp);

                try
                {
                    if (challenge == null || regenerate)
                    {
                        using (var c = ClientHelper.GetClient(v, ri))
                        {
                            c.Init();
                            c.GetDirectory(true);

                            challenge = c.DecodeChallenge(authzState, challengeType);
                            ii.Challenges[challengeType] = challenge;
                        }
                    }

                    if (cleanUp && (repeat || challengeCleanedUp == null))
                    {
                        using (var c = ClientHelper.GetClient(v, ri))
                        {
                            c.Init();
                            c.GetDirectory(true);

                            challenge = c.HandleChallenge(authzState, challengeType,
                                    handlerName, handlerParams, cleanUp);
                            ii.ChallengeCleanedUp[challengeType] = DateTime.Now;
                        }
                    }
                    else if (repeat || challengeCompleted == null)
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
                }
                catch (AcmeClient.AcmeWebException ex)
                {
                    ThrowTerminatingError(PoshHelper.CreateErrorRecord(ex, ii));
                    return;
                }

                vlt.SaveVault(v);
            }
        }

    }
}