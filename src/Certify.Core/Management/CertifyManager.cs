using Certify.Locales;
using Certify.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Management
{
    public interface ICertifyManager
    {
        Task<bool> IsServerTypeAvailable(StandardServerTypes serverType);

        Task<Version> GetServerTypeVersion(StandardServerTypes serverType);

        Task<bool> LoadSettingsAsync(bool skipIfLoaded);

        Task<ManagedSite> GetManagedSite(string id);

        Task<List<ManagedSite>> GetManagedSites(ManagedSiteFilter filter = null);

        Task<ManagedSite> UpdateManagedSite(ManagedSite site);

        Task DeleteManagedSite(string id);

        List<RegistrationItem> GetContactRegistrations();

        List<IdentifierItem> GetDomainIdentifiers();

        List<CertificateItem> GetCertificates();

        void PerformVaultCleanup();

        bool HasRegisteredContacts();

        Task<APIResult> TestChallenge(ManagedSite managedSite, bool isPreviewMode);

        Task<APIResult> RevokeCertificate(ManagedSite managedSite);

        Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedSite managedSite, IProgress<RequestProgressState> progress = null);

        Task<bool> AddRegisteredContact(ContactRegistration reg);

        void RemoveExtraContacts(string email);

        void RemoveAllContacts();

        List<SiteBindingItem> GetPrimaryWebSites(bool ignoreStoppedSites);

        string GetAcmeSummary();

        string GetVaultSummary();

        void BeginTrackingProgress(RequestProgressState state);

        Task<CertificateRequestResult> ReapplyCertificateBindings(ManagedSite managedSite, IProgress<RequestProgressState> progress = null);

        Task<CertificateRequestResult> PerformCertificateRequest(ManagedSite managedSite, IProgress<RequestProgressState> progress = null);

        List<DomainOption> GetDomainOptionsFromSite(string siteId);

        List<ManagedSite> ImportManagedSitesFromVault(bool mergeSitesAsSan = false);

        Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null);

        Task<List<ManagedSite>> PreviewManagedSites(StandardServerTypes serverType);

        RequestProgressState GetRequestProgressState(string managedSiteId);

        Task<bool> PerformPeriodicTasks();

        event Action<RequestProgressState> OnRequestProgressStateUpdated;

        event Action<ManagedSite> OnManagedSiteUpdated;
    }

    public class CertifyManager : ICertifyManager
    {
        private ItemManager _siteManager = null;
        private IACMEClientProvider _acmeClientProvider = null;
        private IVaultProvider _vaultProvider = null;
        private IISManager _iisManager = null;
        public bool IsSingleInstanceMode { get; set; } = true; //if true we make assumptions about how often to load settings etc
        private ObservableCollection<RequestProgressState> _progressResults { get; set; }

        public event Action<RequestProgressState> OnRequestProgressStateUpdated;

        public event Action<ManagedSite> OnManagedSiteUpdated;

        private bool _isRenewAllInProgress { get; set; }

        public CertifyManager()
        {
            Certify.Management.Util.SetSupportedTLSVersions();

            var acmeSharp = new Certify.Management.APIProviders.ACMESharpProvider();
            // ACME Sharp is both a vault (config storage) provider and ACME client provider
            _acmeClientProvider = acmeSharp;
            _vaultProvider = acmeSharp;
            _siteManager = new ItemManager();
            _iisManager = new IISManager();

            _progressResults = new ObservableCollection<RequestProgressState>();
        }

        public void BeginTrackingProgress(RequestProgressState state)
        {
            var existing = _progressResults.FirstOrDefault(p => p.ManagedItem.Id == state.ManagedItem.Id);
            if (existing != null)
            {
                _progressResults.Remove(existing);
            }
            _progressResults.Add(state);
        }

        public async Task<bool> LoadSettingsAsync(bool skipIfLoaded)
        {
            await _siteManager.LoadAllManagedItems(skipIfLoaded);
            return true;
        }

        public async Task<ManagedSite> GetManagedSite(string id)
        {
            return await _siteManager.GetManagedSite(id);
        }

        public async Task<ManagedSite> UpdateManagedSite(ManagedSite site)
        {
            site = await _siteManager.UpdatedManagedSite(site);

            // report request state to status hub clients
            OnManagedSiteUpdated?.Invoke(site);
            return site;
        }

        public async Task<List<ManagedSite>> GetManagedSites(ManagedSiteFilter filter = null)
        {
            return await this._siteManager.GetManagedSites(filter, true);
        }

        public List<RegistrationItem> GetContactRegistrations()
        {
            return _vaultProvider.GetContactRegistrations();
        }

        public List<IdentifierItem> GetDomainIdentifiers()
        {
            return _vaultProvider.GetDomainIdentifiers();
        }

        public List<CertificateItem> GetCertificates()
        {
            return _vaultProvider.GetCertificates();
        }

        public void PerformVaultCleanup()
        {
            _vaultProvider.PerformVaultCleanup();
        }

        public bool HasRegisteredContacts()
        {
            return _vaultProvider.HasRegisteredContacts();
        }

        /// <summary>
        /// Perform set of test challenges and configuration checks to determine if site appears
        /// valid for certificate requests
        /// </summary>
        /// <param name="managedSite"> managed site to check </param>
        /// <param name="isPreviewMode">
        /// If true, perform full set of checks (DNS etc), if false performs minimal/basic checks
        /// </param>
        /// <returns></returns>
        public async Task<APIResult> TestChallenge(ManagedSite managedSite, bool isPreviewMode)
        {
            return await _vaultProvider.TestChallengeResponse(_iisManager, managedSite, isPreviewMode);
        }

        public async Task<APIResult> RevokeCertificate(ManagedSite managedSite)
        {
            var result = await _vaultProvider.RevokeCertificate(managedSite);
            if (result.IsOK)
            {
                managedSite.CertificateRevoked = true;
            }
            return result;
        }

        /// <summary>
        /// Test dummy method for async UI testing etc 
        /// </summary>
        /// <param name="vaultManager"></param>
        /// <param name="managedSite"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedSite managedSite, IProgress<RequestProgressState> progress = null)
        {
            return await Task<CertificateRequestResult>.Run<CertificateRequestResult>(async () =>
            {
                for (var i = 0; i < 6; i++)
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Running, "Step " + i, managedSite));
                    var time = new Random().Next(2000);
                    await Task.Delay(time);
                }

                await Task.Delay(500);

                ReportProgress(progress, new RequestProgressState(RequestState.Success, CoreSR.Finish, managedSite));

                return new CertificateRequestResult { };
            });
        }

        public async Task DeleteManagedSite(string id)
        {
            var site = await _siteManager.GetManagedSite(id);
            if (site != null)
            {
                await this._siteManager.DeleteManagedSite(site);
            }
        }

        public async Task<bool> AddRegisteredContact(ContactRegistration reg)
        {
            // in practise only one registered contact is used, so remove alternatives to avoid cert
            // processing picking up the wrong one
            RemoveAllContacts();

            // now attempt to register the new contact
            if (reg.AgreedToTermsAndConditions)
            {
                // FIXME: not fully async

                return await Task.FromResult(_acmeClientProvider.AddNewRegistrationAndAcceptTOS(reg.EmailAddress));
            }
            else
            {
                // did not agree to terms
                return false;
            }
        }

        /// <summary>
        /// Remove other contacts which don't match the email address given 
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        public void RemoveExtraContacts(string email)
        {
            var regList = _vaultProvider.GetContactRegistrations();
            foreach (var reg in regList)
            {
                if (!reg.Contacts.Contains("mailto:" + email))
                {
                    _vaultProvider.DeleteContactRegistration(reg.Id);
                }
            }
        }

        public void RemoveAllContacts()
        {
            var regList = _vaultProvider.GetContactRegistrations();
            foreach (var reg in regList)
            {
                _vaultProvider.DeleteContactRegistration(reg.Id);
            }
        }

        public List<SiteBindingItem> GetPrimaryWebSites(bool ignoreStoppedSites)
        {
            return _iisManager.GetPrimarySites(ignoreStoppedSites);
        }

        public string GetAcmeSummary()
        {
            return _acmeClientProvider.GetAcmeBaseURI();
        }

        public string GetVaultSummary()
        {
            return _vaultProvider.GetVaultSummary();
        }

        private void ReportProgress(IProgress<RequestProgressState> progress, RequestProgressState state)
        {
            if (progress != null) progress.Report(state);

            // report request state to staus hub clients
            OnRequestProgressStateUpdated?.Invoke(state);

            if (state.ManagedItem != null)
            {
                LogMessage(state.ManagedItem.Id, state.Message, LogItemType.GeneralWarning);
            }
        }

        private void LogMessage(string managedSiteId, string msg, LogItemType logType = LogItemType.GeneralInfo)
        {
            ManagedSiteLog.AppendLog(managedSiteId, new ManagedSiteLogItem
            {
                EventDate = DateTime.UtcNow,
                LogItemType = LogItemType.GeneralInfo,
                Message = msg
            });
        }

        public async Task<CertificateRequestResult> ReapplyCertificateBindings(ManagedSite managedSite, IProgress<RequestProgressState> progress = null)
        {
            var result = new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = "" };
            var config = managedSite.RequestConfig;
            var pfxPath = managedSite.CertificatePath;

            if (managedSite.ItemType == ManagedItemType.SSL_LetsEncrypt_LocalIIS)
            {
                ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_AutoBinding, managedSite));

                // Install certificate into certificate store and bind to IIS site
                if (_iisManager.InstallCertForRequest(managedSite, pfxPath, cleanupCertStore: true))
                {
                    //all done
                    LogMessage(managedSite.Id, CoreSR.CertifyManager_CompleteRequestAndUpdateBinding, LogItemType.CertificateRequestSuccessful);

                    await UpdateManagedSiteStatus(managedSite, RequestState.Success);

                    result.IsSuccess = true;
                    result.Message = string.Format(CoreSR.CertifyManager_CertificateInstalledAndBindingUpdated, config.PrimaryDomain);
                    ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedSite));
                }
                else
                {
                    // something broke
                    result.Message = string.Format(CoreSR.CertifyManager_CertificateInstallFailed, pfxPath);
                    await UpdateManagedSiteStatus(managedSite, RequestState.Error, result.Message);

                    LogMessage(managedSite.Id, result.Message, LogItemType.GeneralError);
                }
            }
            return result;
        }

        public async Task<CertificateRequestResult> PerformCertificateRequest(ManagedSite managedSite, IProgress<RequestProgressState> progress = null)
        {
            // FIXME: refactor into different concerns, there's way too much being done here
            if (managedSite.RequestConfig.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP && managedSite.RequestConfig.PerformExtensionlessConfigChecks)
            {
                ReportProgress(progress,
                    new RequestProgressState(RequestState.Running, Certify.Locales.CoreSR.CertifyManager_PerformingConfigTests, managedSite)
                );

                var testResult = await TestChallenge(managedSite, isPreviewMode: false);
                if (!testResult.IsOK)
                {
                    return new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = String.Join("; ", testResult.FailedItemSummary), Result = testResult.Result };
                }
            }

            return await Task.Run(async () =>
            {
                // start with a failure result, set to success when succeeding
                var result = new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = "" };

                var config = managedSite.RequestConfig;
                try
                {
                    // run pre-request script, if set
                    if (!string.IsNullOrEmpty(config.PreRequestPowerShellScript))
                    {
                        try
                        {
                            string scriptOutput = await PowerShellManager.RunScript(result, config.PreRequestPowerShellScript);
                            LogMessage(managedSite.Id, $"Pre-Request Script output: \n{scriptOutput}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage(managedSite.Id, $"Pre-Request Script error:\n{ex.Message}");
                        }
                    }

                    // if the script has requested the certificate request to be aborted, skip the request
                    if (result.Abort)
                    {
                        LogMessage(managedSite.Id, $"Certificate Request Aborted: {managedSite.Name}");
                        result.Message = Certify.Locales.CoreSR.CertificateRequestWasAbortedByPSScript;
                        goto CertRequestAborted;
                    }

                    LogMessage(managedSite.Id, $"Beginning Certificate Request Process: {managedSite.Name}");

                    //enable or disable EFS flag on private key certs based on preference
                    if (CoreAppSettings.Current.EnableEFS)
                    {
                        _vaultProvider.EnableSensitiveFileEncryption();
                    }

                    //primary domain and each subject alternative name must now be registered as an identifier with LE and validated
                    ReportProgress(progress,
                        new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RegisterDomainIdentity, managedSite)
                    );

                    //await Task.Delay(200); //allow UI update, we should we using async calls instead

                    List<string> allDomains = new List<string> { config.PrimaryDomain };

                    if (config.SubjectAlternativeNames != null) allDomains.AddRange(config.SubjectAlternativeNames);

                    // begin by assuming all identifiers are valid
                    bool allIdentifiersValidated = true;

                    if (config.ChallengeType == null) config.ChallengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP;

                    List<PendingAuthorization> identifierAuthorizations = new List<PendingAuthorization>();
                    var distinctDomains = allDomains.Distinct();

                    string failureSummaryMessage = null;

                    // perform validation process for each domain
                    foreach (var domain in distinctDomains)
                    {
                        //begin authorization process (register identifier, request authorization if not already given)
                        var domainIdentifierId = _vaultProvider.ComputeDomainIdentifierId(domain);

                        LogMessage(managedSite.Id, $"Attempting Domain Validation: {domain}", LogItemType.CertificateRequestStarted);

                        ReportProgress(progress,
                            new RequestProgressState(RequestState.Running, string.Format(Certify.Locales.CoreSR.CertifyManager_RegisteringAndValidatingX0, domain), managedSite)
                            );

                        //TODO: make operations async and yield IO of vault
                        /*var authorization = await Task.Run(() =>
                        {
                            return vaultManager.BeginRegistrationAndValidation(config, identifierAlias, challengeType: config.ChallengeType, domain: domain);
                        });*/

                        // begin authorization by registering the domain identifier. This may return
                        // an already validated authorization or we may still have to complete the
                        // authorization challenge. When rate limits are encountered, this step may fail.
                        var authorization = _vaultProvider.BeginRegistrationAndValidation(config, domainIdentifierId, challengeType: config.ChallengeType, domain: domain);

                        if (authorization != null && authorization.Identifier != null)
                        {
                            // check if authorization is pending, it may already be valid if an
                            // existing authorization was reused
                            if (authorization.Identifier.IsAuthorizationPending)
                            {
                                if (managedSite.ItemType == ManagedItemType.SSL_LetsEncrypt_LocalIIS)
                                {
                                    ReportProgress(progress,
                                        new RequestProgressState(
                                            RequestState.Running,
                                            string.Format(Certify.Locales.CoreSR.CertifyManager_PerformingChallengeResponseViaIISX0, domain),
                                            managedSite
                                        )
                                    );

                                    // ask LE to check our answer to their authorization challenge
                                    // (http-01 or tls-sni-01), LE will then attempt to fetch our
                                    // answer, if all accessible and correct (authorized) LE will
                                    // then allow us to request a certificate
                                    authorization = _vaultProvider.PerformIISAutomatedChallengeResponse(_iisManager, managedSite, authorization);

                                    // pass authorization log items onto main log
                                    /*authorization.LogItems?.ForEach((msg) =>
                                    {
                                        if (msg != null) LogMessage(managedSite.Id, msg, LogItemType.GeneralInfo);
                                    });*/

                                    if ((config.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP && config.PerformExtensionlessConfigChecks && !authorization.ExtensionlessConfigCheckedOK) ||
                                        (config.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI && config.PerformTlsSniBindingConfigChecks && !authorization.TlsSniConfigCheckedOK))
                                    {
                                        //if we failed the config checks, report any errors
                                        var msg = string.Format(CoreSR.CertifyManager_FailedPrerequisiteCheck, managedSite.ItemType);
                                        LogMessage(managedSite.Id, msg, LogItemType.CertficateRequestFailed);
                                        result.Message = msg;

                                        // TODO: should this be a status save?

                                        if (config.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP)
                                        {
                                            result.Message = string.Format(CoreSR.CertifyManager_AutomateConfigurationCheckFailed_HTTP, domain);
                                        }

                                        if (config.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI)
                                        {
                                            result.Message = Certify.Locales.CoreSR.CertifyManager_AutomateConfigurationCheckFailed_SNI;
                                        }

                                        ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedSite) { Result = result });

                                        await UpdateManagedSiteStatus(managedSite, RequestState.Error, result.Message);

                                        break;
                                    }
                                    else
                                    {
                                        ReportProgress(progress, new RequestProgressState(RequestState.Running, string.Format(CoreSR.CertifyManager_ReqestValidationFromLetsEncrypt, domain), managedSite));

                                        try
                                        {
                                            //ask LE to validate our challenge response
                                            _vaultProvider.SubmitChallenge(domainIdentifierId, config.ChallengeType);

                                            bool identifierValidated = _vaultProvider.CompleteIdentifierValidationProcess(authorization.Identifier.Alias);

                                            if (!identifierValidated)
                                            {
                                                var identifierInfo = _vaultProvider.GetDomainIdentifier(domain);
                                                var errorMsg = identifierInfo?.ValidationError;
                                                var errorType = identifierInfo?.ValidationErrorType;

                                                failureSummaryMessage = string.Format(CoreSR.CertifyManager_DomainValidationFailed, domain, errorMsg);
                                                ReportProgress(progress, new RequestProgressState(RequestState.Error, failureSummaryMessage, managedSite));
                                                await UpdateManagedSiteStatus(managedSite, RequestState.Error, failureSummaryMessage);
                                                allIdentifiersValidated = false;
                                            }
                                            else
                                            {
                                                ReportProgress(progress, new RequestProgressState(RequestState.Running, string.Format(CoreSR.CertifyManager_DomainValidationCompleted, domain), managedSite));

                                                identifierAuthorizations.Add(authorization);
                                            }
                                        }
                                        finally
                                        {
                                            // clean up challenge answers
                                            // (.well-known/acme-challenge/* files for http-01 or iis
                                            // bindings for tls-sni-01)
                                            authorization.Cleanup();
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // we already have a completed authorization, check it's valid
                                if (authorization.Identifier.Status == "valid")
                                {
                                    LogMessage(managedSite.Id, string.Format(CoreSR.CertifyManager_DomainValidationSkipVerifed, domain));

                                    identifierAuthorizations.Add(new PendingAuthorization { Identifier = authorization.Identifier });
                                }
                                else
                                {
                                    var errorMsg = "";
                                    if (authorization?.Identifier != null)
                                    {
                                        errorMsg = authorization.Identifier.ValidationError;
                                        var errorType = authorization.Identifier.ValidationErrorType;
                                    }

                                    failureSummaryMessage = $"Domain validation failed: {domain} \r\n{errorMsg}";

                                    LogMessage(managedSite.Id, string.Format(CoreSR.CertifyManager_DomainValidationFailed, domain));

                                    allIdentifiersValidated = false;
                                }
                            }
                        }
                        else
                        {
                            // could not begin authorization : TODO: pass error from authorization
                            // step to UI

                            var lastActionLogItem = _vaultProvider.GetLastActionLogItem();
                            var actionLogMsg = "";
                            if (lastActionLogItem != null)
                            {
                                actionLogMsg = lastActionLogItem.ToString();
                            }

                            LogMessage(managedSite.Id, $"Could not begin authorization for domain with Let's Encrypt: { domain } {(authorization?.AuthorizationError != null ? authorization?.AuthorizationError : "Could not register domain identifier")} - {actionLogMsg}");

                            /*if (authorization != null && authorization.LogItems != null)
                            {
                                LogMessage(managedSite.Id, authorization.LogItems);
                            }*/
                            allIdentifiersValidated = false;
                        }

                        // abandon authorization attempts if one of our domains has failed verification
                        if (!allIdentifiersValidated) break;
                    }

                    //check if all identifiers have a valid authorization
                    if (identifierAuthorizations.Count != distinctDomains.Count())
                    {
                        allIdentifiersValidated = false;
                    }

                    if (allIdentifiersValidated)
                    {
                        string primaryDnsIdentifier = identifierAuthorizations.First().Identifier.Alias;
                        string[] alternativeDnsIdentifiers = identifierAuthorizations.Select(i => i.Identifier.Alias).ToArray();

                        ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RequestCertificate, managedSite));

                        // Perform CSR request
                        // FIXME: make call async
                        var certRequestResult = _vaultProvider.PerformCertificateRequestProcess(primaryDnsIdentifier, alternativeDnsIdentifiers);

                        if (certRequestResult.IsSuccess)
                        {
                            ReportProgress(progress, new RequestProgressState(RequestState.Success, CoreSR.CertifyManager_CompleteRequest, managedSite));

                            string pfxPath = certRequestResult.Result.ToString();

                            // update managed site summary
                            try
                            {
                                var certInfo = CertificateManager.LoadCertificate(pfxPath);
                                managedSite.DateStart = certInfo.NotBefore;
                                managedSite.DateExpiry = certInfo.NotAfter;
                                managedSite.DateRenewed = DateTime.Now;

                                managedSite.CertificatePath = pfxPath;
                                managedSite.CertificateRevoked = false;

                                //ensure certificate contains all the requested domains
                                //var subjectNames = certInfo.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.UpnName, false);

                                //FIXME: LogMessage(managedSite.Id, "New certificate contains following domains: " + subjectNames, LogItemType.GeneralInfo);
                            }
                            catch (Exception)
                            {
                                LogMessage(managedSite.Id, "Failed to parse certificate dates", LogItemType.GeneralError);
                            }

                            if (managedSite.ItemType == ManagedItemType.SSL_LetsEncrypt_LocalIIS)
                            {
                                ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_AutoBinding, managedSite));

                                // Install certificate into certificate store and bind to IIS site
                                if (_iisManager.InstallCertForRequest(managedSite, pfxPath, cleanupCertStore: true))
                                {
                                    //all done
                                    LogMessage(managedSite.Id, CoreSR.CertifyManager_CompleteRequestAndUpdateBinding, LogItemType.CertificateRequestSuccessful);

                                    await UpdateManagedSiteStatus(managedSite, RequestState.Success);

                                    result.IsSuccess = true;
                                    result.Message = string.Format(CoreSR.CertifyManager_CertificateInstalledAndBindingUpdated, config.PrimaryDomain);
                                    ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedSite));
                                }
                                else
                                {
                                    // something broke
                                    result.Message = string.Format(CoreSR.CertifyManager_CertificateInstallFailed, pfxPath);
                                    await UpdateManagedSiteStatus(managedSite, RequestState.Error, result.Message);

                                    LogMessage(managedSite.Id, result.Message, LogItemType.GeneralError);
                                }
                            }
                            else
                            {
                                //user has opted for manual binding of certificate

                                result.IsSuccess = true;
                                result.Message = string.Format(CoreSR.CertifyManager_CertificateCreatedForBinding, pfxPath);
                                LogMessage(managedSite.Id, result.Message, LogItemType.CertificateRequestSuccessful);
                                await UpdateManagedSiteStatus(managedSite, RequestState.Success, result.Message);
                                ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedSite));
                            }
                        }
                        else
                        {
                            // certificate request failed

                            result.Message = string.Format(CoreSR.CertifyManager_LetsEncryptServiceTimeout, certRequestResult.ErrorMessage ?? "");
                            await UpdateManagedSiteStatus(managedSite, RequestState.Error, result.Message);
                            LogMessage(managedSite.Id, result.Message, LogItemType.CertficateRequestFailed);
                            ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedSite));
                        }
                    }
                    else
                    {
                        //failed to validate all identifiers
                        result.Message = string.Format(CoreSR.CertifyManager_ValidationForChallengeNotSuccess, (failureSummaryMessage != null ? failureSummaryMessage : ""));

                        await UpdateManagedSiteStatus(managedSite, RequestState.Error, result.Message);

                        LogMessage(managedSite.Id, result.Message, LogItemType.CertficateRequestFailed);
                        ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedSite));
                    }

                    // Goto label for aborted certificate request
                    CertRequestAborted: { }
                }
                catch (Exception exp)
                {
                    // overall exception thrown during process

                    result.IsSuccess = false;
                    result.Message = string.Format(Certify.Locales.CoreSR.CertifyManager_RequestFailed, managedSite.Name, exp.Message, exp);
                    LogMessage(managedSite.Id, result.Message, LogItemType.CertficateRequestFailed);
                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedSite));

                    await UpdateManagedSiteStatus(managedSite, RequestState.Error, result.Message);

                    //LogMessage(managedSite.Id, String.Join("\r\n", _vaultProvider.GetActionSummary())); FIXME: needs to be filtered in managed site
                    System.Diagnostics.Debug.WriteLine(exp.ToString());
                }
                finally
                {
                    // if the request was not aborted, perform post-request actions
                    if (!result.Abort)
                    {
                        // run post-request script, if set
                        if (!string.IsNullOrEmpty(config.PostRequestPowerShellScript))
                        {
                            try
                            {
                                string scriptOutput = await PowerShellManager.RunScript(result, config.PostRequestPowerShellScript);
                                LogMessage(managedSite.Id, $"Post-Request Script output:\n{scriptOutput}");
                            }
                            catch (Exception ex)
                            {
                                LogMessage(managedSite.Id, $"Post-Request Script error: {ex.Message}");
                            }
                        }
                        // run webhook triggers, if set
                        if ((config.WebhookTrigger == Webhook.ON_SUCCESS && result.IsSuccess) ||
                    (config.WebhookTrigger == Webhook.ON_ERROR && !result.IsSuccess) ||
                    (config.WebhookTrigger == Webhook.ON_SUCCESS_OR_ERROR))
                        {
                            try
                            {
                                var (success, code) = await Webhook.SendRequest(config, result.IsSuccess);
                                LogMessage(managedSite.Id, $"Webhook invoked: Url: {config.WebhookUrl}, Success: {success}, StatusCode: {code}");
                            }
                            catch (Exception ex)
                            {
                                LogMessage(managedSite.Id, $"Webhook error: {ex.Message}");
                            }
                        }
                    }
                }

                return result;
            });
        }

        private async Task UpdateManagedSiteStatus(ManagedSite managedSite, RequestState status, string msg = null)
        {
            managedSite.DateLastRenewalAttempt = DateTime.UtcNow;

            if (status == RequestState.Success)
            {
                managedSite.RenewalFailureCount = 0;
                managedSite.LastRenewalStatus = RequestState.Success;
            }
            else
            {
                managedSite.RenewalFailureMessage = msg;
                managedSite.RenewalFailureCount++;
                managedSite.LastRenewalStatus = RequestState.Error;
            }

            managedSite = await _siteManager.UpdatedManagedSite(managedSite);

            // report request state to staus hub clients
            OnManagedSiteUpdated?.Invoke(managedSite);
        }

        public List<DomainOption> GetDomainOptionsFromSite(string siteId)
        {
            var defaultNoDomainHost = "";
            var domainOptions = new List<DomainOption>();

            var matchingSites = _iisManager.GetSiteBindingList(CoreAppSettings.Current.IgnoreStoppedSites, siteId);
            var siteBindingList = matchingSites.Where(s => s.SiteId == siteId);

            bool includeEmptyHostnameBindings = false;

            foreach (var siteDetails in siteBindingList)
            {
                //if domain not currently in the list of options, add it
                if (!domainOptions.Any(item => item.Domain == siteDetails.Host))
                {
                    DomainOption opt = new DomainOption
                    {
                        Domain = siteDetails.Host,
                        IsPrimaryDomain = false,
                        IsSelected = true,
                        Title = ""
                    };

                    if (String.IsNullOrWhiteSpace(opt.Domain))
                    {
                        //binding has no hostname/domain set - user will need to specify
                        opt.Title = defaultNoDomainHost;
                        opt.Domain = defaultNoDomainHost;
                        opt.IsManualEntry = true;
                    }
                    else
                    {
                        opt.Title = siteDetails.Protocol + "://" + opt.Domain;
                    }

                    if (siteDetails.IP != null && siteDetails.IP != "0.0.0.0")
                    {
                        opt.Title += " : " + siteDetails.IP;
                    }

                    if (!opt.IsManualEntry || (opt.IsManualEntry && includeEmptyHostnameBindings))
                    {
                        domainOptions.Add(opt);
                    }
                }
            }

            //TODO: if one or more binding is to a specific IP, how to manage in UI?

            if (domainOptions.Any(d => !String.IsNullOrEmpty(d.Domain)))
            {
                // mark first domain as primary, if we have no other settings
                if (!domainOptions.Any(d => d.IsPrimaryDomain == true))
                {
                    var electableDomains = domainOptions.Where(d => !String.IsNullOrEmpty(d.Domain) && d.Domain != defaultNoDomainHost);
                    if (electableDomains.Any())
                    {
                        // promote first domain in list to primary by default
                        electableDomains.First().IsPrimaryDomain = true;
                    }
                }
            }

            return domainOptions;
        }

        public List<ManagedSite> ImportManagedSitesFromVault(bool mergeSitesAsSan = false)
        {
            var sites = new List<ManagedSite>();

            if (_iisManager == null || !_iisManager.IsIISAvailable)
            {
                // IIS not enabled, can't match sites to vault items
                return sites;
            }

            //get dns identifiers from vault
            var identifiers = _vaultProvider.GetDomainIdentifiers();

            // match existing IIS sites to vault items
            var iisSites = _iisManager.GetSiteBindingList(ignoreStoppedSites: CoreAppSettings.Current.IgnoreStoppedSites);

            foreach (var identifier in identifiers)
            {
                //identify IIS site related to this identifier (if any)
                var iisSite = iisSites.FirstOrDefault(d => d.Host == identifier.Dns);
                var site = new ManagedSite
                {
                    Id = Guid.NewGuid().ToString(),
                    GroupId = iisSite?.SiteId,
                    Name = identifier.Dns + (iisSite != null ? " : " + iisSite.SiteName : ""),
                    IncludeInAutoRenew = true,
                    Comments = "Imported from vault",
                    ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS,
                    TargetHost = "localhost",
                    RequestConfig = new CertRequestConfig
                    {
                        BindingIPAddress = iisSite?.IP,
                        BindingPort = iisSite?.Port.ToString(),
                        ChallengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP,
                        EnableFailureNotifications = true,
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = true,
                        PrimaryDomain = identifier.Dns,
                        SubjectAlternativeNames = new string[] { identifier.Dns }
                    }
                };
                site.DomainOptions.Add(new DomainOption { Domain = identifier.Dns, IsPrimaryDomain = true, IsSelected = true });
                sites.Add(site);
            }

            if (mergeSitesAsSan)
            {
                foreach (var s in sites)
                {
                    //merge sites with same group (iis site etc) and different primary domain
                    if (sites.Any(m => m.GroupId != null && m.GroupId == s.GroupId && m.RequestConfig.PrimaryDomain != s.RequestConfig.PrimaryDomain))
                    {
                        //existing site to merge into
                        //add san for dns
                        var mergedSite = sites.FirstOrDefault(m =>
                        m.GroupId != null && m.GroupId == s.GroupId
                        && m.RequestConfig.PrimaryDomain != s.RequestConfig.PrimaryDomain
                        && m.RequestConfig.PrimaryDomain != null
                        );
                        if (mergedSite != null)
                        {
                            mergedSite.DomainOptions.Add(new DomainOption { Domain = s.RequestConfig.PrimaryDomain, IsPrimaryDomain = false, IsSelected = true });

                            //use shortest version of domain name as site name
                            if (mergedSite.RequestConfig.PrimaryDomain.Contains(s.RequestConfig.PrimaryDomain))
                            {
                                mergedSite.Name = mergedSite.Name.Replace(mergedSite.RequestConfig.PrimaryDomain, s.RequestConfig.PrimaryDomain);
                            }

                            //flag spare site config to be discar
                            s.RequestConfig.PrimaryDomain = null;
                        }
                    }
                }

                //discard sites which have been merged into other sites
                sites.RemoveAll(s => s.RequestConfig.PrimaryDomain == null);
            }
            return sites;
        }

        public static bool IsRenewalRequired(ManagedSite s, int renewalIntervalDays)
        {
            // if we know the last renewal date, check whether we should renew again, otherwise
            // assume it's more than 30 days ago by default and attempt renewal

            var timeSinceLastRenewal = (s.DateRenewed.HasValue ? s.DateRenewed.Value : DateTime.Now.AddDays(-30)) - DateTime.Now;

            bool isRenewalRequired = Math.Abs(timeSinceLastRenewal.TotalDays) > renewalIntervalDays;

            return isRenewalRequired;
        }

        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null)
        {
            if (_isRenewAllInProgress)
            {
                Debug.WriteLine("Renew all is already is progress..");
                return await Task.FromResult(new List<CertificateRequestResult>());
            }

            this._isRenewAllInProgress = true;
            //currently the vault won't let us run parallel requests due to file locks
            bool performRequestsInParallel = false;

            bool testModeOnly = false;

            //await _siteManager.LoadAllManagedItems();

            IEnumerable<ManagedSite> sites = await _siteManager.GetManagedSites(new ManagedSiteFilter { IncludeOnlyNextAutoRenew = true }, reloadAll: true);

            if (autoRenewalOnly)
            {
                sites = sites.Where(s => s.IncludeInAutoRenew == true);
            }

            // check site list and examine current certificates. If certificate is less than n days
            // old, don't attempt to renew it
            var sitesToRenew = new List<ManagedSite>();
            var renewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays;

            int numRenewalTasks = 0;
            int maxRenewalTasks = CoreAppSettings.Current.MaxRenewalRequests;

            var renewalTasks = new List<Task<CertificateRequestResult>>();

            if (progressTrackers == null)
            {
                progressTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            }

            foreach (var s in sites.Where(s => s.IncludeInAutoRenew == true))
            {
                RequestProgressState progressState = new RequestProgressState(RequestState.Running, "Starting..", s);
                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);
                progressTrackers.Add(s.Id, progressIndicator);

                BeginTrackingProgress(progressState);

                // determine if this site requires renewal
                bool isRenewalRequired = IsRenewalRequired(s, renewalIntervalDays);

                //if we care about stopped sites being stopped, check for that
                bool isSiteRunning = true;
                if (!CoreAppSettings.Current.IgnoreStoppedSites)
                {
                    isSiteRunning = await IsManagedSiteRunning(s.Id);
                }

                if ((isRenewalRequired && isSiteRunning) || testModeOnly)
                {
                    //get matching progress tracker for this site
                    IProgress<RequestProgressState> tracker = null;
                    if (progressTrackers != null)
                    {
                        tracker = progressTrackers[s.Id];
                    }

                    // optionally limit the number of renewal tasks to attempt in this pass
                    if (maxRenewalTasks == 0 || maxRenewalTasks > 0 && numRenewalTasks < maxRenewalTasks)
                    {
                        if (testModeOnly)
                        {
                            //simulated request for UI testing
                            renewalTasks.Add(this.PerformDummyCertificateRequest(s, tracker));
                        }
                        else
                        {
                            renewalTasks.Add(this.PerformCertificateRequest(s, tracker));
                        }
                    }
                    numRenewalTasks++;
                }
                else
                {
                    var msg = CoreSR.CertifyManager_SkipRenewalOk;

                    if (isRenewalRequired && !isSiteRunning)
                    {
                        //TODO: show this as warning rather than success
                        msg = CoreSR.CertifyManager_SiteStopped;
                    }

                    if (progressTrackers != null)
                    {
                        //send progress back to report skip
                        var progress = (IProgress<RequestProgressState>)progressTrackers[s.Id];
                        ReportProgress(progress, new RequestProgressState(RequestState.Success, msg, s));
                    }

                    ManagedSiteLog.AppendLog(s.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.GeneralInfo, Message = msg + s.Name });
                }
            }

            if (!renewalTasks.Any())
            {
                //nothing to do
                _isRenewAllInProgress = false;
                return new List<CertificateRequestResult>();
            }

            if (performRequestsInParallel)
            {
                var results = await Task.WhenAll(renewalTasks);

                //siteManager.StoreSettings();
                _isRenewAllInProgress = false;
                return results.ToList();
            }
            else
            {
                var results = new List<CertificateRequestResult>();
                foreach (var t in renewalTasks)
                {
                    results.Add(await t);
                }

                _isRenewAllInProgress = false;
                return results;
            }
        }

        private async Task<bool> IsManagedSiteRunning(string id, IISManager iis = null)
        {
            var managedSite = await _siteManager.GetManagedSite(id);
            if (managedSite != null)
            {
                if (iis == null) iis = _iisManager;
                return iis.IsSiteRunning(managedSite.GroupId);
            }
            else
            {
                //site not identified, assume it is running
                return true;
            }
        }

        public async Task<bool> IsServerTypeAvailable(StandardServerTypes serverType)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return await this._iisManager.IsIISAvailableAsync();
            }
            return false;
        }

        public async Task<Version> GetServerTypeVersion(StandardServerTypes serverType)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return await this._iisManager.GetIisVersionAsync();
            }
            return null;
        }

        /// <summary>
        /// For current configured environment, show preview of recommended site management (for
        /// local IIS, scan sites and recommend actions)
        /// </summary>
        /// <returns></returns>
        public Task<List<ManagedSite>> PreviewManagedSites(StandardServerTypes serverType)
        {
            List<ManagedSite> sites = new List<ManagedSite>();

            // FIXME: IIS query is not async
            if (serverType == StandardServerTypes.IIS)
            {
                try
                {
                    var iisSites = new IISManager().GetSiteBindingList(ignoreStoppedSites: CoreAppSettings.Current.IgnoreStoppedSites).OrderBy(s => s.SiteId).ThenBy(s => s.Host);

                    var siteIds = iisSites.GroupBy(x => x.SiteId);

                    foreach (var s in siteIds)
                    {
                        ManagedSite managedSite = new ManagedSite { Id = s.Key };
                        managedSite.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;
                        managedSite.TargetHost = "localhost";
                        managedSite.Name = iisSites.First(i => i.SiteId == s.Key).SiteName;

                        //TODO: replace site binding with domain options
                        //managedSite.SiteBindings = new List<ManagedSiteBinding>();

                        /* foreach (var binding in s)
                         {
                             var managedBinding = new ManagedSiteBinding { Hostname = binding.Host, IP = binding.IP, Port = binding.Port, UseSNI = true, CertName = "Certify_" + binding.Host };
                             // managedSite.SiteBindings.Add(managedBinding);
                         }*/
                        sites.Add(managedSite);
                    }
                }
                catch (Exception)
                {
                    //can't read sites
                    Debug.WriteLine("Can't get IIS site list.");
                }
            }
            return Task.FromResult(sites);
        }

        public RequestProgressState GetRequestProgressState(string managedSiteId)
        {
            var progress = this._progressResults.FirstOrDefault(p => p.ManagedItem.Id == managedSiteId);
            if (progress == null)
            {
                return new RequestProgressState(RequestState.NotRunning, "No request in progress", null);
            }
            else
            {
                return progress;
            }
        }

        /// <summary>
        /// When called, look for periodic tasks we can perform such as renewal 
        /// </summary>
        /// <returns></returns>
        public async Task<bool> PerformPeriodicTasks()
        {
            Debug.WriteLine("Checking for periodic tasks..");
            await this.PerformRenewalAllManagedSites(true, null);
            return await Task.FromResult(true);
        }
    }
}