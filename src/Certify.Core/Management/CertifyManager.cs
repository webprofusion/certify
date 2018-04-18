using Certify.Core.Management.Challenges;
using Certify.Locales;
using Certify.Management.Servers;
using Certify.Models;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Microsoft.ApplicationInsights;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class CertifyManager : ICertifyManager, IDisposable
    {
        private ItemManager _siteManager = null;
        private IACMEClientProvider _acmeClientProvider = null;
        private IVaultProvider _vaultProvider = null;
        private ICertifiedServer _serverProvider = null;
        private ChallengeDiagnostics _challengeDiagnostics = null;
        private IdnMapping _idnMapping = new IdnMapping();
        private PluginManager _pluginManager { get; set; }
        private TelemetryClient _tc = null;

        private ObservableCollection<RequestProgressState> _progressResults { get; set; }

        public event Action<RequestProgressState> OnRequestProgressStateUpdated;

        public event Action<ManagedCertificate> OnManagedCertificateUpdated;

        private bool _isRenewAllInProgress { get; set; }
        private ILog _serviceLog { get; set; }

        public CertifyManager()
        {
            _serviceLog = new Loggy(
                new LoggerConfiguration()
               .MinimumLevel.Verbose()
               .WriteTo.Debug()
               .WriteTo.File(Util.GetAppDataFolder("logs") + "\\sessionlog.txt", shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
               .CreateLogger()
               );

            Util.SetSupportedTLSVersions();

            _siteManager = new ItemManager();
            _serverProvider = (ICertifiedServer)new ServerProviderIIS();

            _progressResults = new ObservableCollection<RequestProgressState>();

            _pluginManager = new PluginManager();
            _pluginManager.LoadPlugins();

            // TODO: convert providers to plugins
            var certes = new Certify.Providers.Certes.CertesACMEProvider(Management.Util.GetAppDataFolder() + "\\certes");

            _acmeClientProvider = certes;
            _vaultProvider = certes;

            // init remaining utilities and optionally enable telematics
            _challengeDiagnostics = new ChallengeDiagnostics(CoreAppSettings.Current.EnableValidationProxyAPI);

            if (CoreAppSettings.Current.EnableAppTelematics)
            {
                _tc = new Util().InitTelemetry();
            }

            PerformUpgrades();
        }

        public void PerformUpgrades()
        {
            // check if there are no registered contacts, if so see if we are upgrading from a vault
            if (GetContactRegistrations().Count == 0)
            {
                var acmeVaultMigration = new Models.Compat.ACMEVaultUpgrader();
                if (acmeVaultMigration.HasACMEVault())
                {
                    string email = acmeVaultMigration.GetContact();
                    if (!String.IsNullOrEmpty(email))
                    {
                        var addedOK = _acmeClientProvider.AddNewAccountAndAcceptTOS(_serviceLog, email).Result;
                    }
                }
            }
        }

        public void BeginTrackingProgress(RequestProgressState state)
        {
            var existing = _progressResults.FirstOrDefault(p => p.ManagedCertificate.Id == state.ManagedCertificate.Id);
            if (existing != null)
            {
                _progressResults.Remove(existing);
            }
            _progressResults.Add(state);
        }

        public async Task<bool> LoadSettingsAsync(bool skipIfLoaded)
        {
            await _siteManager.LoadAllManagedCertificates(skipIfLoaded);
            return true;
        }

        public async Task<ManagedCertificate> GetManagedCertificate(string id)
        {
            return await _siteManager.GetManagedCertificate(id);
        }

        public async Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site)
        {
            site = await _siteManager.UpdatedManagedCertificate(site);

            // report request state to status hub clients
            OnManagedCertificateUpdated?.Invoke(site);
            return site;
        }

        public async Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter = null)
        {
            return await this._siteManager.GetManagedCertificates(filter, true);
        }

        public List<RegistrationItem> GetContactRegistrations()
        {
            return _vaultProvider.GetContactRegistrations();
        }

        /// <summary>
        /// Perform set of test challenges and configuration checks to determine if site appears
        /// valid for certificate requests
        /// </summary>
        /// <param name="managedCertificate"> managed site to check </param>
        /// <param name="isPreviewMode">
        /// If true, perform full set of checks (DNS etc), if false performs minimal/basic checks
        /// </param>
        /// <returns></returns>
        public async Task<List<StatusMessage>> TestChallenge(ILog log, ManagedCertificate managedCertificate, bool isPreviewMode)
        {
            return await _challengeDiagnostics.TestChallengeResponse(log, _serverProvider, managedCertificate, isPreviewMode, CoreAppSettings.Current.EnableDNSValidationChecks);
        }

        public async Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate)
        {
            if (_tc != null) _tc.TrackEvent("RevokeCertificate");

            var result = await _acmeClientProvider.RevokeCertificate(log, managedCertificate);
            if (result.IsOK)
            {
                managedCertificate.CertificateRevoked = true;
            }
            return result;
        }

        /// <summary>
        /// Test dummy method for async UI testing etc 
        /// </summary>
        /// <param name="vaultManager"></param>
        /// <param name="managedCertificate"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null)
        {
            return await Task<CertificateRequestResult>.Run<CertificateRequestResult>(async () =>
            {
                for (var i = 0; i < 6; i++)
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Running, "Step " + i, managedCertificate));
                    var time = new Random().Next(2000);
                    await Task.Delay(time);
                }

                await Task.Delay(500);

                ReportProgress(progress, new RequestProgressState(RequestState.Success, CoreSR.Finish, managedCertificate));

                return new CertificateRequestResult { };
            });
        }

        public async Task DeleteManagedCertificate(string id)
        {
            var site = await _siteManager.GetManagedCertificate(id);
            if (site != null)
            {
                await this._siteManager.DeleteManagedCertificate(site);
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
                return await _acmeClientProvider.AddNewAccountAndAcceptTOS(_serviceLog, reg.EmailAddress);
            }
            else
            {
                // did not agree to terms
                return false;
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

        public async Task<List<SiteBindingItem>> GetPrimaryWebSites(bool ignoreStoppedSites)
        {
            return await _serverProvider.GetPrimarySites(ignoreStoppedSites);
        }

        private void ReportProgress(IProgress<RequestProgressState> progress, RequestProgressState state, bool logThisEvent = true)
        {
            if (progress != null) progress.Report(state);

            // report request state to staus hub clients
            OnRequestProgressStateUpdated?.Invoke(state);

            if (state.ManagedCertificate != null && logThisEvent)
            {
                LogMessage(state.ManagedCertificate.Id, state.Message, LogItemType.GeneralInfo);
            }
        }

        private void LogMessage(string managedItemId, string msg, LogItemType logType = LogItemType.GeneralInfo)
        {
            ManagedCertificateLog.AppendLog(managedItemId, new ManagedCertificateLogItem
            {
                EventDate = DateTime.UtcNow,
                LogItemType = LogItemType.GeneralInfo,
                Message = msg
            });
        }

        public async Task<CertificateRequestResult> ApplyCertificateBindings(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false)
        {
            if (_tc != null) _tc.TrackEvent("ReapplyCertBindings");
            string logPrefix = "";
            if (isPreviewOnly) logPrefix = "[Preview Mode] ";

            var result = new CertificateRequestResult { ManagedItem = managedCertificate, IsSuccess = false, Message = "" };
            var config = managedCertificate.RequestConfig;
            var pfxPath = managedCertificate.CertificatePath;

            if (managedCertificate.ItemType == ManagedCertificateType.SSL_LetsEncrypt_LocalIIS)
            {
                if (!isPreviewOnly) ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_AutoBinding, managedCertificate));

                // Install certificate into certificate store and bind to IIS site
                var actions = await _serverProvider.InstallCertForRequest(managedCertificate, pfxPath, cleanupCertStore: true, isPreviewOnly: isPreviewOnly);
                result.Actions = actions;

                if (!actions.Any(a => a.HasError))
                {
                    //all done
                    LogMessage(managedCertificate.Id, logPrefix + CoreSR.CertifyManager_CompleteRequestAndUpdateBinding, LogItemType.CertificateRequestSuccessful);

                    if (!isPreviewOnly) await UpdateManagedCertificateStatus(managedCertificate, RequestState.Success);

                    result.IsSuccess = true;
                    result.Message = logPrefix + string.Format(CoreSR.CertifyManager_CertificateInstalledAndBindingUpdated, config.PrimaryDomain);
                    ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate));
                }
                else
                {
                    // certificate install failed
                    result.Message = logPrefix + string.Format(CoreSR.CertifyManager_CertificateInstallFailed, pfxPath);
                    if (!isPreviewOnly) await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                    LogMessage(managedCertificate.Id, result.Message, LogItemType.GeneralError);
                }
            }
            return result;
        }

        public async Task<CertificateRequestResult> PerformCertificateRequest(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null)
        {
            // Perform pre-request checks and scripting hooks, invoke main request process, then
            // perform an post request scripting hooks
            if (log == null) log = ManagedCertificateLog.GetLogger(managedCertificate.Id);

            // start with a failure result, set to success when succeeding
            var result = new CertificateRequestResult { ManagedItem = managedCertificate, IsSuccess = false, Message = "" };

            var config = managedCertificate.RequestConfig;
            try
            {
                // run pre-request script, if set
                if (!string.IsNullOrEmpty(config.PreRequestPowerShellScript))
                {
                    try
                    {
                        string scriptOutput = await PowerShellManager.RunScript(result, config.PreRequestPowerShellScript);
                        LogMessage(managedCertificate.Id, $"Pre-Request Script output: \n{scriptOutput}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage(managedCertificate.Id, $"Pre-Request Script error:\n{ex.Message}");
                    }
                }

                // if the script has requested the certificate request to be aborted, skip the request
                if (result.Abort)
                {
                    LogMessage(managedCertificate.Id, $"Certificate Request Aborted: {managedCertificate.Name}");
                    result.Message = Certify.Locales.CoreSR.CertificateRequestWasAbortedByPSScript;
                }
                else
                {
                    await PerformCertificateRequestProcessing(log, managedCertificate, progress, result, config);
                }
            }
            catch (Exception exp)
            {
                // overall exception thrown during process

                result.IsSuccess = false;
                result.Message = string.Format(Certify.Locales.CoreSR.CertifyManager_RequestFailed, managedCertificate.Name, exp.Message, exp);

                LogMessage(managedCertificate.Id, result.Message, LogItemType.CertficateRequestFailed);

                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate));

                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                log.Error(exp, "Certificate request process failed: {exp}");
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
                            LogMessage(managedCertificate.Id, $"Post-Request Script output:\n{scriptOutput}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage(managedCertificate.Id, $"Post-Request Script error: {ex.Message}");
                        }
                    }

                    // run webhook triggers, if set
                    if (
                        (config.WebhookTrigger == Webhook.ON_SUCCESS && result.IsSuccess) ||
                        (config.WebhookTrigger == Webhook.ON_ERROR && !result.IsSuccess) ||
                        (config.WebhookTrigger == Webhook.ON_SUCCESS_OR_ERROR)
                    )
                    {
                        try
                        {
                            var webHookResult = await Webhook.SendRequest(config, result.IsSuccess);
                            LogMessage(managedCertificate.Id, $"Webhook invoked: Url: {config.WebhookUrl}, Success: {webHookResult.Success}, StatusCode: {webHookResult.StatusCode}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage(managedCertificate.Id, $"Webhook error: {ex.Message}");
                        }
                    }
                }
            }

            return result;
        }

        private async Task PerformCertificateRequestProcessing(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, CertificateRequestResult result, CertRequestConfig config)
        {
            // proceed with the request
            LogMessage(managedCertificate.Id, $"Beginning Certificate Request Process: {managedCertificate.Name} using ACME Provider:{_acmeClientProvider.GetProviderName()}");

            //enable or disable EFS flag on private key certs based on preference
            if (CoreAppSettings.Current.EnableEFS)
            {
                _vaultProvider.EnableSensitiveFileEncryption();
            }

            //primary domain and each subject alternative name must now be registered as an identifier with LE and validated
            ReportProgress(progress,
                new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RegisterDomainIdentity, managedCertificate)
            );

            List<string> allDomains = new List<string> { config.PrimaryDomain };

            if (config.SubjectAlternativeNames != null) allDomains.AddRange(config.SubjectAlternativeNames);

            // begin by assuming all identifiers are valid
            bool allIdentifiersValidated = true;

            if (config.ChallengeType == null && (config.Challenges == null || !config.Challenges.Any()))
            {
                config.Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                    new List<CertRequestChallengeConfig> {
                       new CertRequestChallengeConfig{
                           ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                            }
                        });
            }

            List<PendingAuthorization> identifierAuthorizations = new List<PendingAuthorization>();
            string failureSummaryMessage = null;

            var distinctDomains = allDomains.Distinct();

            // perform validation process for each domain

            // begin authorization by registering the domain identifier. This may return an already
            // validated authorization or we may still have to complete the authorization challenge.
            // When rate limits are encountered, this step may fail.
            var authorizations = await _acmeClientProvider.BeginCertificateOrder(log, config);

            if (authorizations.Any(a => a.IsFailure))
            {
                //failed to begin the order
                result.Message = $"Failed to create certificate order: {authorizations.FirstOrDefault(a => a.IsFailure).AuthorizationError}";

                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result });
                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                LogMessage(managedCertificate.Id, result.Message, LogItemType.CertficateRequestFailed);

                return;
            }

            foreach (var domain in distinctDomains)
            {
                var asciiDomain = _idnMapping.GetAscii(domain);
                var auth = authorizations.FirstOrDefault(a => a.Identifier?.Dns == asciiDomain);
                var challengeConfig = managedCertificate.GetChallengeConfig(domain);

                var authorization = auth;
                if (authorization != null && authorization.Identifier != null)
                {
                    LogMessage(managedCertificate.Id, $"Attempting Domain Validation: {domain}", LogItemType.CertificateRequestStarted);

                    ReportProgress(progress,
                                        new RequestProgressState(RequestState.Running, string.Format(Certify.Locales.CoreSR.CertifyManager_RegisteringAndValidatingX0, domain), managedCertificate)
                                        );

                    // check if authorization is pending, it may already be valid if an existing
                    // authorization was reused
                    if (auth.Identifier.IsAuthorizationPending)
                    {
                        ReportProgress(progress,
                            new RequestProgressState(
                                RequestState.Running,
                                string.Format(Certify.Locales.CoreSR.CertifyManager_PerformingChallengeResponseViaIISX0, domain),
                                managedCertificate
                            )
                        );

                        // ask LE to check our answer to their authorization challenge (http-01 or
                        // tls-sni-01), LE will then attempt to fetch our answer, if all accessible
                        // and correct (authorized) LE will then allow us to request a certificate
                        authorization = await _challengeDiagnostics.PerformAutomatedChallengeResponse(log, _serverProvider, managedCertificate, authorization);

                        if (
                            (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP && config.PerformExtensionlessConfigChecks && !authorization.AttemptedChallenge?.ConfigCheckedOK == true) ||
                            (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI && config.PerformTlsSniBindingConfigChecks && !authorization.AttemptedChallenge?.ConfigCheckedOK == true)
                            )
                        {
                            //if we failed the config checks, report any errors
                            var msg = string.Format(CoreSR.CertifyManager_FailedPrerequisiteCheck, managedCertificate.ItemType);

                            LogMessage(managedCertificate.Id, msg, LogItemType.CertficateRequestFailed);
                            result.Message = msg;

                            if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                            {
                                result.Message = string.Format(CoreSR.CertifyManager_AutomateConfigurationCheckFailed_HTTP, domain);
                            }

                            if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
                            {
                                result.Message = Certify.Locales.CoreSR.CertifyManager_AutomateConfigurationCheckFailed_SNI;
                            }

                            ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result });

                            await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                            break;
                        }
                        else
                        {
                            ReportProgress(progress, new RequestProgressState(RequestState.Running, string.Format(CoreSR.CertifyManager_ReqestValidationFromLetsEncrypt, domain), managedCertificate));

                            try
                            {
                                // if our challenge take a while to propagate, wait
                                if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                                {
                                    // TODO: need a single wait for all DNS challenges to be created,
                                    //       should await all in a collection then proceed to validation
                                    await Task.Delay(30000); //allows 30 seconds for validation to complete.//
                                }

                                //ask LE to validate our challenge response
                                var submissionStatus = await _acmeClientProvider.SubmitChallenge(log, challengeConfig.ChallengeType, authorization.AttemptedChallenge);

                                if (submissionStatus.IsOK)
                                {
                                    authorization = await _acmeClientProvider.CheckValidationCompleted(log, challengeConfig.ChallengeType, authorization);

                                    if (!authorization.IsValidated)
                                    {
                                        var identifierInfo = authorization.Identifier;
                                        var errorMsg = authorization.AuthorizationError;
                                        var errorType = identifierInfo?.ValidationErrorType;

                                        failureSummaryMessage = string.Format(CoreSR.CertifyManager_DomainValidationFailed, domain, errorMsg);
                                        ReportProgress(progress, new RequestProgressState(RequestState.Error, failureSummaryMessage, managedCertificate));

                                        await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, failureSummaryMessage);

                                        allIdentifiersValidated = false;
                                    }
                                    else
                                    {
                                        ReportProgress(progress, new RequestProgressState(RequestState.Running, string.Format(CoreSR.CertifyManager_DomainValidationCompleted, domain), managedCertificate));

                                        identifierAuthorizations.Add(authorization);
                                    }
                                }
                                else
                                {
                                    // challenge not submitted, already validated or failed submission
                                }
                            }
                            finally
                            {
                                // clean up challenge answers (.well-known/acme-challenge/* files for
                                // http-01 or iis bindings for tls-sni-01)

                                authorization.Cleanup();
                            }
                        }
                    }
                    else
                    {
                        // we already have a completed authorization, check it's valid
                        if (authorization.IsValidated)
                        {
                            LogMessage(managedCertificate.Id, string.Format(CoreSR.CertifyManager_DomainValidationSkipVerifed, domain));

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

                            LogMessage(managedCertificate.Id, failureSummaryMessage);

                            allIdentifiersValidated = false;
                        }
                    }
                }
                else
                {
                    // could not begin authorization

                    var actionLogMsg = "";

                    LogMessage(managedCertificate.Id, $"Could not begin authorization for domain with Let's Encrypt: [{ domain }] {(authorization?.AuthorizationError != null ? authorization?.AuthorizationError : "Could not register domain identifier")} - {actionLogMsg}");
                    failureSummaryMessage = $"[{domain}] : {authorization?.AuthorizationError}";

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
                ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RequestCertificate, managedCertificate));

                // Perform CSR request
                var certRequestResult = await _acmeClientProvider.CompleteCertificateRequest(log, config);

                if (certRequestResult.IsSuccess)
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Success, CoreSR.CertifyManager_CompleteRequest, managedCertificate));

                    string pfxPath = certRequestResult.Result.ToString();

                    // update managed site summary
                    try
                    {
                        var certInfo = CertificateManager.LoadCertificate(pfxPath);
                        managedCertificate.DateStart = certInfo.NotBefore;
                        managedCertificate.DateExpiry = certInfo.NotAfter;
                        managedCertificate.DateRenewed = DateTime.Now;

                        managedCertificate.CertificatePath = pfxPath;
                        managedCertificate.CertificatePreviousThumbprintHash = managedCertificate.CertificateThumbprintHash;
                        managedCertificate.CertificateThumbprintHash = certInfo.Thumbprint;
                        managedCertificate.CertificateRevoked = false;

                        //ensure certificate contains all the requested domains
                        //var subjectNames = certInfo.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.UpnName, false);

                        //FIXME: LogMessage(managedCertificate.Id, "New certificate contains following domains: " + subjectNames, LogItemType.GeneralInfo);
                    }
                    catch (Exception)
                    {
                        LogMessage(managedCertificate.Id, "Failed to parse certificate dates", LogItemType.GeneralError);
                    }

                    if (managedCertificate.ItemType == ManagedCertificateType.SSL_LetsEncrypt_LocalIIS)
                    {
                        ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_AutoBinding, managedCertificate));

                        // Install certificate into certificate store and bind to IIS site
                        var actions = await _serverProvider.InstallCertForRequest(managedCertificate, pfxPath, cleanupCertStore: true, isPreviewOnly: false);
                        if (!actions.Any(a => a.HasError))
                        {
                            //all done
                            LogMessage(managedCertificate.Id, CoreSR.CertifyManager_CompleteRequestAndUpdateBinding, LogItemType.CertificateRequestSuccessful);

                            await UpdateManagedCertificateStatus(managedCertificate, RequestState.Success);

                            result.IsSuccess = true;

                            // depending on the deployment type the final result will vary
                            // string.Format(CoreSR.CertifyManager_CertificateInstalledAndBindingUpdated, config.PrimaryDomain);
                            result.Message = "Request completed";
                            ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate));
                        }
                        else
                        {
                            // we failed to install this cert or create/update the https binding
                            result.Message = string.Format(CoreSR.CertifyManager_CertificateInstallFailed, pfxPath);
                            await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                            LogMessage(managedCertificate.Id, result.Message, LogItemType.GeneralError);
                        }
                    }
                    else
                    {
                        //user has opted for manual binding of certificate

                        result.IsSuccess = true;
                        result.Message = string.Format(CoreSR.CertifyManager_CertificateCreatedForBinding, pfxPath);
                        LogMessage(managedCertificate.Id, result.Message, LogItemType.CertificateRequestSuccessful);
                        await UpdateManagedCertificateStatus(managedCertificate, RequestState.Success, result.Message);
                        ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate));
                    }
                }
                else
                {
                    // certificate request failed

                    result.Message = string.Format(CoreSR.CertifyManager_LetsEncryptServiceTimeout, certRequestResult.ErrorMessage ?? "");
                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                    LogMessage(managedCertificate.Id, result.Message, LogItemType.CertficateRequestFailed);
                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate));
                }
            }
            else
            {
                //failed to validate all identifiers
                result.Message = string.Format(CoreSR.CertifyManager_ValidationForChallengeNotSuccess, (failureSummaryMessage != null ? failureSummaryMessage : ""));

                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                LogMessage(managedCertificate.Id, result.Message, LogItemType.CertficateRequestFailed);
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate));
            }
        }

        private async Task UpdateManagedCertificateStatus(ManagedCertificate managedCertificate, RequestState status, string msg = null)
        {
            managedCertificate.DateLastRenewalAttempt = DateTime.UtcNow;

            if (status == RequestState.Success)
            {
                managedCertificate.RenewalFailureCount = 0;
                managedCertificate.LastRenewalStatus = RequestState.Success;
            }
            else
            {
                managedCertificate.RenewalFailureMessage = msg;
                managedCertificate.RenewalFailureCount++;
                managedCertificate.LastRenewalStatus = RequestState.Error;
            }

            managedCertificate = await _siteManager.UpdatedManagedCertificate(managedCertificate);

            // report request state to status hub clients
            OnManagedCertificateUpdated?.Invoke(managedCertificate);

            //if reporting enabled, send report
            if (managedCertificate.RequestConfig?.EnableFailureNotifications == true)
            {
                await ReportManagedCertificateStatus(managedCertificate);
            }

            if (_tc != null) _tc.TrackEvent("UpdateManagedCertificatesStatus_" + status.ToString());
        }

        public async Task<List<DomainOption>> GetDomainOptionsFromSite(string siteId)
        {
            var defaultNoDomainHost = "";
            var domainOptions = new List<DomainOption>();

            var matchingSites = await _serverProvider.GetSiteBindingList(CoreAppSettings.Current.IgnoreStoppedSites, siteId);
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

            return domainOptions.OrderByDescending(d => d.IsPrimaryDomain).ThenBy(d => d.Domain).ToList();
        }

        public static bool IsRenewalRequired(ManagedCertificate s, int renewalIntervalDays, bool checkFailureStatus = false)
        {
            // if we know the last renewal date, check whether we should renew again, otherwise
            // assume it's more than 30 days ago by default and attempt renewal

            var timeSinceLastRenewal = (s.DateRenewed.HasValue ? s.DateRenewed.Value : DateTime.Now.AddDays(-30)) - DateTime.Now;

            bool isRenewalRequired = Math.Abs(timeSinceLastRenewal.TotalDays) > renewalIntervalDays;

            // if renewal is required but we have previously failed, scale the frequency of renewal
            // attempts to a minimum of once per 24hrs.
            if (isRenewalRequired && checkFailureStatus)
            {
                if (s.LastRenewalStatus == RequestState.Error)
                {
                    // our last attempt failed, check how many failures we've had to decide whether
                    // we should attempt now, Scale wait time based on how many attempts we've made.
                    // Max 48hrs between attempts
                    if (s.DateLastRenewalAttempt != null && s.RenewalFailureCount > 0)
                    {
                        var hoursWait = 48;
                        if (s.RenewalFailureCount > 0 && s.RenewalFailureCount < 48)
                        {
                            hoursWait = s.RenewalFailureCount;
                        }
                        var nextAttemptByDate = s.DateLastRenewalAttempt.Value.AddHours(hoursWait);
                        if (DateTime.Now < nextAttemptByDate)
                        {
                            isRenewalRequired = false;
                        }
                    }
                }
            }
            return isRenewalRequired;
        }

        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedCertificates(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null)
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

            IEnumerable<ManagedCertificate> sites = await _siteManager.GetManagedCertificates(
                new ManagedCertificateFilter
                {
                    IncludeOnlyNextAutoRenew = true
                }, reloadAll: true);

            if (autoRenewalOnly)
            {
                // auto renew enabled sites in order of oldest date renewed first
                sites = sites.Where(s => s.IncludeInAutoRenew == true)
                             .OrderBy(s => s.DateRenewed ?? DateTime.MinValue);
            }

            // check site list and examine current certificates. If certificate is less than n days
            // old, don't attempt to renew it
            var sitesToRenew = new List<ManagedCertificate>();
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
                bool isRenewalOnHold = false;
                if (isRenewalRequired)
                {
                    //check if we have renewal failures, if so wait a bit longer
                    isRenewalOnHold = !IsRenewalRequired(s, renewalIntervalDays, checkFailureStatus: true);

                    if (isRenewalOnHold) isRenewalRequired = false;
                }

                //if we care about stopped sites being stopped, check for that
                bool isSiteRunning = true;
                if (!CoreAppSettings.Current.IgnoreStoppedSites)
                {
                    isSiteRunning = await IsManagedCertificateRunning(s.Id);
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
                            renewalTasks.Add(this.PerformCertificateRequest(null, s, tracker));
                        }
                    }
                    numRenewalTasks++;
                }
                else
                {
                    var msg = CoreSR.CertifyManager_SkipRenewalOk;
                    bool logThisEvent = false;

                    if (isRenewalRequired && !isSiteRunning)
                    {
                        //TODO: show this as warning rather than success
                        msg = CoreSR.CertifyManager_SiteStopped;
                    }

                    if (isRenewalOnHold)
                    {
                        //TODO: show this as warning rather than success

                        msg = String.Format(CoreSR.CertifyManager_RenewalOnHold, s.RenewalFailureCount);
                        logThisEvent = true;
                    }

                    if (progressTrackers != null)
                    {
                        //send progress back to report skip
                        var progress = (IProgress<RequestProgressState>)progressTrackers[s.Id];
                        ReportProgress(progress, new RequestProgressState(RequestState.Success, msg, s), logThisEvent);
                    }
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

        private async Task<bool> IsManagedCertificateRunning(string id, ICertifiedServer iis = null)
        {
            var managedCertificate = await _siteManager.GetManagedCertificate(id);
            if (managedCertificate != null)
            {
                if (iis == null) iis = _serverProvider;
                return await iis.IsSiteRunning(managedCertificate.GroupId);
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
                return await this._serverProvider.IsAvailable();
            }
            return false;
        }

        public async Task<Version> GetServerTypeVersion(StandardServerTypes serverType)
        {
            return await this._serverProvider.GetServerVersion();
        }

        public RequestProgressState GetRequestProgressState(string managedItemId)
        {
            var progress = this._progressResults.FirstOrDefault(p => p.ManagedCertificate.Id == managedItemId);
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

            SettingsManager.LoadAppSettings();

            if (CoreAppSettings.Current.UseBackgroundServiceAutoRenewal)
            {
                await this.PerformRenewalAllManagedCertificates(true, null);
            }

            return await Task.FromResult(true);
        }

        public async Task<bool> PerformDailyTasks()
        {
            Debug.WriteLine("Checking for daily tasks..");

            SettingsManager.LoadAppSettings();

            if (_tc != null) _tc.TrackEvent("ServiceDailyTaskCheck");

            return await Task.FromResult(true);
        }

        /// <summary>
        /// If enabled in the request config, report status of the renewal 
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        private async Task ReportManagedCertificateStatus(ManagedCertificate managedCertificate)
        {
            if (this._pluginManager != null && this._pluginManager.DashboardClient != null)
            {
                var report = new Models.Shared.RenewalStatusReport
                {
                    InstanceId = CoreAppSettings.Current.InstanceId,
                    MachineName = Environment.MachineName,
                    PrimaryContactEmail = GetPrimaryContactEmail(),
                    ManagedSite = managedCertificate,
                    AppVersion = new Management.Util().GetAppVersion().ToString()
                };
                try
                {
                    await this._pluginManager.DashboardClient.ReportRenewalStatusAsync(report);
                }
                catch (Exception)
                {
                    // failed to report status
                    LogMessage(managedCertificate.Id, "Failed to send renewal status report.", LogItemType.GeneralWarning);
                }
            }
        }

        public string GetPrimaryContactEmail()
        {
            var contacts = GetContactRegistrations();
            if (contacts.Any())
            {
                return contacts.FirstOrDefault()?.Name.Replace("mailto:", "");
            }
            else
            {
                return null;
            }
        }

        public async Task<List<ActionStep>> GeneratePreview(ManagedCertificate item)
        {
            return await new PreviewManager().GeneratePreview(item, _serverProvider, this);
        }

        public async Task<List<ManagedCertificate>> PreviewManagedCertificates(StandardServerTypes serverType)
        {
            return await new PreviewManager().PreviewManagedCertificates(StandardServerTypes.IIS, _serverProvider, this);
        }

        public void Dispose()
        {
            ManagedCertificateLog.DisposeLoggers();
        }
    }
}