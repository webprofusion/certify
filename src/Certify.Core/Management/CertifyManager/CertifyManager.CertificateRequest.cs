using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Core.Management;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Models.Shared;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        /// <summary>
        /// Internet identifier name ASCII > Unicode mapping provider
        /// </summary>
        private IdnMapping _idnMapping = new IdnMapping();

        private bool _isRenewAllInProgress { get; set; }
        private ConcurrentDictionary<string, DateTimeOffset?> _renewalsInProgress = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset?>();

        /// <summary>
        /// When called, look for periodic maintenance tasks we can perform such as renewal
        /// </summary>
        /// <returns>  </returns>
        public async Task<bool> PerformRenewalTasks()
        {
            try
            {
                Debug.WriteLine("Checking for renewal tasks..");

                SettingsManager.LoadAppSettings();

                // perform pending renewals
                await PerformRenewAll(new RenewalSettings { });

                // flush status report queue
                await SendQueuedStatusReports();
            }
            catch (Exception exp)
            {
                _tc?.TrackException(exp);
                return await Task.FromResult(false);
            }

            return await Task.FromResult(true);
        }

        /// <summary>
        /// Perform Renew All: identify all items to renew then initiate renewal process
        /// </summary>
        /// <param name="autoRenewalOnly">  </param>
        /// <param name="progressTrackers">  </param>
        /// <returns>  </returns>
        public async Task<List<CertificateRequestResult>> PerformRenewAll(RenewalSettings settings, ConcurrentDictionary<string, Progress<RequestProgressState>> progressTrackers = null)
        {
            if (_isRenewAllInProgress)
            {
                _serviceLog?.Verbose("Renew all is already is progress..");
                return await Task.FromResult(new List<CertificateRequestResult>());
            }

            _isRenewAllInProgress = true;

            _serviceLog?.Verbose($"Performing Renew All for all applicable managed certificates.");

            _isRenewAllInProgress = true;

            var prefs = new RenewalPrefs
            {
                MaxRenewalRequests = CoreAppSettings.Current.MaxRenewalRequests,
                RenewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays,
                RenewalIntervalMode = CoreAppSettings.Current.RenewalIntervalMode,
                IncludeStoppedSites = !CoreAppSettings.Current.IgnoreStoppedSites,
                SuppressSkippedItems = true,
                PerformParallelRenewals = CoreAppSettings.Current.EnableParallelRenewals
            };

            var results = await RenewalManager.PerformRenewAll(
                _serviceLog,
                _itemManager,
                settings,
                prefs,

                ReportProgress, IsManagedCertificateRunning,
                (ManagedCertificate item, IProgress<RequestProgressState> progress, bool isPreview, string reason) =>
                {
                    return PerformCertificateRequest(null, item, progress, skipRequest: isPreview, skipTasks: isPreview, reason: reason);
                },
                progressTrackers);

            _isRenewAllInProgress = false;

            return results;
        }

        /// <summary>
        /// Test dummy method for async UI testing etc
        /// </summary>
        /// <param name="vaultManager">  </param>
        /// <param name="managedCertificate">  </param>
        /// <param name="progress">  </param>
        /// <returns>  </returns>
        public async Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null)
        {
#pragma warning disable IDE0022 // Use expression body for methods
            return await Task.Run(async () =>
            {
                for (var i = 0; i < 6; i++)
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Running, "Step " + i, managedCertificate));
                    var time = new Random().Next(2000);
                    await Task.Delay(time);
                }

                await Task.Delay(500);

                ReportProgress(progress, new RequestProgressState(RequestState.Success, CoreSR.Finish, managedCertificate));

                return new CertificateRequestResult(managedCertificate, true, "OK");
            });
#pragma warning restore IDE0022 // Use expression body for methods
        }

        /// <summary>
        /// Initiate or resume the certificate request workflow for a given managed certificate
        /// </summary>
        /// <param name="log">  </param>
        /// <param name="managedCertificate">  </param>
        /// <param name="progress">  </param>
        /// <returns>  </returns>
        public async Task<CertificateRequestResult> PerformCertificateRequest(
                ILog log, ManagedCertificate managedCertificate,
                IProgress<RequestProgressState> progress = null,
                bool resumePaused = false,
                bool skipRequest = false,
                bool failOnSkip = false,
                bool skipTasks = false,
                bool isInteractive = false,
                string reason = null
            )
        {

            // check if we have an existing request in progress, if so skip for now (max request in progress age 10 mins)
            _renewalsInProgress.TryGetValue(managedCertificate.Id, out var existingRequest);

            if (existingRequest.HasValue)
            {
                var age = existingRequest.Value - DateTimeOffset.Now;

                if (Math.Abs(age.TotalMinutes) > 10)
                {
                    // if we have a stuck request, let the user start it again
                    _renewalsInProgress.TryRemove(managedCertificate.Id, out existingRequest);
                }
                else
                {
                    return new CertificateRequestResult { Abort = true, IsSuccess = false, ManagedItem = managedCertificate, Message = "Certificate request already in progress." };
                }
            }

            _renewalsInProgress.TryAdd(managedCertificate.Id, DateTimeOffset.Now);

            _serviceLog?.Information("Performing Certificate Request: {Name} [{Id}]", managedCertificate.Name, managedCertificate.Id);

            // Perform pre-request checks and scripting hooks, invoke main request process, then
            // perform an post request scripting hooks
            if (log == null)
            {
                log = ManagedCertificateLog.GetLogger(managedCertificate.Id, _loggingLevelSwitch);
            }

            log.Information("---- Beginning Request [{Name}] ----", managedCertificate.Name);

            if (reason != null)
            {
                log.Information("Renewal Reason: {reason}", reason);
            }

            // start with a failure result, set to success when succeeding
            var requestResult = new CertificateRequestResult(managedCertificate);

            managedCertificate.RenewalFailureMessage = ""; // clear any previous renewal error or instructions
            var currentFailureCount = managedCertificate.RenewalFailureCount; // preserve current failure count if we encounter a new failure later in the process

            try
            {

                if (managedCertificate.PreRequestTasks?.Any() == true && managedCertificate.Health != ManagedCertificateHealth.AwaitingUser)
                {
                    // run pre-request tasks, currently if any of these fail the request will abort

                    log.Information($"Performing Pre-Request Tasks..");

                    var results = await PerformTaskList(log, isPreviewOnly: false, skipDeferredTasks: true, requestResult, managedCertificate.PreRequestTasks);

                    // log results
                    var preRequestTasks = new ActionStep
                    {
                        Category = "Pre-Request Tasks",
                        Key = "PreRequestTasks",
                        Substeps = new List<ActionStep>(),
                        HasError = results.Any(r => r.HasError),
                        HasWarning = results.Any(r => r.HasWarning),
                    };

                    foreach (var r in results)
                    {
                        r.Category = "Pre-Request Tasks";
                        preRequestTasks.Substeps.Add(r);
                    }

                    requestResult.Actions.Add(preRequestTasks);

                    if (results.Any(r => r.HasError))
                    {
                        requestResult.Abort = true;

                        var msg = $"Request was aborted due to failed Pre-Request Task.";

                        requestResult.Message = msg;
                    }
                }

                // if the script has requested the certificate request to be aborted, skip the request
                if (!requestResult.Abort)
                {

                    if (!skipRequest && managedCertificate.SkipCertificateRequest != true)
                    {
                        if (resumePaused && managedCertificate.Health == ManagedCertificateHealth.AwaitingUser)
                        {
                            // resume a previously paused request
                            CertificateRequestResult result;

                            // If mixing manual dns with acme-dns, manual challenges need to be checked without re-challenging
                            if (managedCertificate.RequestConfig.Challenges?.Any(c => c.ChallengeProvider == "DNS01.Manual") == true)
                            {
                                // resume manual dns requests etc
                                result = await CompleteCertificateRequest(log, managedCertificate, progress, null);
                            }
                            else
                            {
                                // perform normal certificate challenge/response/renewal (acme-dns etc)
                                result = await PerformCoreCertificateRequest(log, managedCertificate, progress, requestResult, isInteractive, resumeExistingOrder: true);
                            }

                            // copy result from sub-request, preserve existing logged actions
                            requestResult.ApplyChanges(result);

                        }
                        else
                        {
                            if (managedCertificate.Health != ManagedCertificateHealth.AwaitingUser)
                            {
                                // perform normal certificate challenge/response/renewal
                                var result = await PerformCoreCertificateRequest(log, managedCertificate, progress, requestResult, isInteractive, resumeExistingOrder: false);
                                requestResult.ApplyChanges(result);
                            }
                            else
                            {
                                // request is waiting on user input but has been automatically initiated,
                                // therefore skip for now
                                requestResult.Abort = true;
                                log.Information("Certificate Request Skipped, Awaiting User Input: {Name}", managedCertificate.Name);
                            }
                        }
                    }
                    else
                    {
                        // caller asked to skip the actual certificate request (e.g. unit testing)

                        if (failOnSkip)
                        {
                            requestResult.Message = $"Certificate Request Skipped (on demand, marked as failed): {managedCertificate.Name}";
                            requestResult.IsSuccess = false;
                        }
                        else
                        {
                            requestResult.Message = $"Certificate Request Skipped (on demand): {managedCertificate.Name}";
                            requestResult.IsSuccess = managedCertificate.LastRenewalStatus == RequestState.Success;
                        }

                        ReportProgress(progress, new RequestProgressState(RequestState.Success, requestResult.Message, managedCertificate));
                    }
                }
            }
            catch (Exception exp)
            {
                // overall exception thrown during process

                requestResult.IsSuccess = false;
                requestResult.Abort = true;

                try
                {
                    // attempt to log error

                    log?.Error(exp, $"Certificate request process failed: {exp}");

                    requestResult.Message = string.Format(Certify.Locales.CoreSR.CertifyManager_RequestFailed, managedCertificate.Name, exp.Message, exp);

                    log?.Error(requestResult.Message);

                    ReportProgress(progress, new RequestProgressState(RequestState.Error, requestResult.Message, managedCertificate), logThisEvent: false);

                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, requestResult.Message, currentFailureCount);
                }
                catch { }
            }
            finally
            {
                requestResult.ManagedItem = managedCertificate;

                // if request is not awaiting user and there are any post requests tasks, run them now
                if (managedCertificate.PostRequestTasks?.Any() == true && managedCertificate.Health != ManagedCertificateHealth.AwaitingUser)
                {

                    // run applicable deployment tasks (whether success or failed), powershell
                    log?.Information($"Performing Post-Request (Deployment) Tasks..");

                    var results = await PerformTaskList(log, isPreviewOnly: false, skipDeferredTasks: true, requestResult, managedCertificate.PostRequestTasks);

                    // log results
                    var postRequestTasks = new ActionStep
                    {
                        Category = "Post-Request Tasks",
                        Key = "PostRequestTasks",
                        Substeps = new List<ActionStep>(),
                        HasError = results.Any(r => r.HasError),
                        HasWarning = results.Any(r => r.HasWarning),
                    };

                    foreach (var r in results)
                    {
                        if (r.HasError || r.HasWarning)
                        {
                            log.Error($"{r.Title} :: {r.Description}", (r.HasError || r.HasWarning));

                        }
                        else
                        {
                            log.Information($"{r.Title} :: {r.Description}", (r.HasError || r.HasWarning) ? LogItemType.CertificateRequestAttentionRequired : LogItemType.GeneralInfo);

                        }

                        r.Category = "Post-Request Tasks";
                        postRequestTasks.Substeps.Add(r);

                    }

                    requestResult.Actions.Add(postRequestTasks);

                    // certificate may already be deployed to some extent so this counts a completed with warnings
                    if (results.Any(r => r.HasError))
                    {
                        requestResult.IsSuccess = false;

                        var msg = $"Deployment Tasks did not complete successfully.";
                        requestResult.Message = msg;
                    }
                }

                // final state is either paused, success or error
                var finalState = managedCertificate.Health == ManagedCertificateHealth.AwaitingUser ?
                     RequestState.Paused :
                     (requestResult.IsSuccess ? RequestState.Success : RequestState.Error);

                ReportProgress(progress, new RequestProgressState(finalState, requestResult.Message, managedCertificate), logThisEvent: false);

                if (string.IsNullOrEmpty(requestResult.Message) && !string.IsNullOrEmpty(managedCertificate.RenewalFailureMessage))
                {
                    requestResult.Message = managedCertificate.RenewalFailureMessage;
                }

                await UpdateManagedCertificateStatus(managedCertificate, finalState, requestResult.Message, currentFailureCount);
            }

            _renewalsInProgress.TryRemove(managedCertificate.Id, out _);

            if (isInteractive)
            {
                await SendQueuedStatusReports();
            }

            return requestResult;
        }

        /// <summary>
        /// Get (cached) list of expected challenges and responses, used for dynamic challenge response services
        /// </summary>
        /// <param name="challengeType"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public Task<List<SimpleAuthorizationChallengeItem>> GetCurrentChallengeResponses(string challengeType, string key = null)
        {
            var challengeResponses = _currentChallenges
                .Where(c => c.Value.ChallengeType == challengeType && (key == null || (key != null && c.Value.Key == key)))
                .Select(a => a.Value).ToList();

            return Task.FromResult(challengeResponses);
        }

        /// <summary>
        /// Begin or resume an certificate order request
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCertificate"></param>
        /// <param name="progress"></param>
        /// <param name="result"></param>
        /// <param name="config"></param>
        /// <param name="isInteractive"></param>
        /// <returns></returns>
        private async Task<CertificateRequestResult> PerformCoreCertificateRequest(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, CertificateRequestResult result, bool isInteractive, bool resumeExistingOrder)
        {
            //primary identifier and each subject alternative name must now be registered as an identifier with ACME CA and validated
            log?.Information($"{Util.GetUserAgent()}");

            var caAccount = await GetAccountDetails(managedCertificate, allowFailover: CoreAppSettings.Current.EnableAutomaticCAFailover);
            var acmeClientProvider = await GetACMEProvider(managedCertificate, caAccount);

            if (caAccount == null || acmeClientProvider == null)
            {
                result.IsSuccess = false;
                result.Abort = true;
                result.Message = $"There is no matching ACME account for the currently selected Certificate Authority. Check you have added a {(managedCertificate.UseStagingMode ? "Staging" : "Production")} account for the CA under the app Settings.";

                return result;
            }

            if (!resumeExistingOrder)
            {
                managedCertificate.CurrentOrderUri = null;
            }

            log?.Information("Beginning certificate request process: {Name} using ACME provider {Provider}", managedCertificate.Name, acmeClientProvider.GetProviderName());

            _certificateAuthorities.TryGetValue(caAccount?.CertificateAuthorityId, out var certAuthority);

            if (caAccount.IsFailoverSelection)
            {
                log?.Warning("Due to previous renewal failures an alternative CA account has been selected for failover: {Title}", certAuthority?.Title);
            }
            else
            {
                log?.Information("The selected Certificate Authority is: {Title}", certAuthority?.Title);
            }

            if (managedCertificate.RequestConfig.PreferredExpiryDays > 0)
            {
                log?.Information("Requested certificate lifetime is {Days} days.", managedCertificate.RequestConfig.PreferredExpiryDays);

            }

            log?.Information("Requested identifiers to include on certificate: {Identifiers}", string.Join(";", managedCertificate.GetCertificateIdentifiers()));

            ReportProgress(progress,
                new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RegisterDomainIdentity, managedCertificate, false), logThisEvent: false
            );

#pragma warning disable CS0618 // Type or member is obsolete
            var config = managedCertificate.RequestConfig;

            if (config.ChallengeType == null && (config.Challenges == null || !config.Challenges.Any()))
            {
                config.Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                    new List<CertRequestChallengeConfig> {
                       new CertRequestChallengeConfig{
                           ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                            }
                        });
            }
#pragma warning restore CS0618 // Type or member is obsolete

            var identifierAuthorizations = new List<PendingAuthorization>();

            managedCertificate.LastAttemptedCA = caAccount.CertificateAuthorityId;

            // begin authorization by registering the cert order. The response will include a list of
            // authorizations per identifier. Authorizations may already be validated or we may still
            // have to complete the authorization challenge. When rate limits are encountered, this
            // step may fail.
            var pendingOrder = await acmeClientProvider.BeginCertificateOrder(log, managedCertificate, resumeExistingOrder);

            if (pendingOrder.IsFailure)
            {
                result.IsSuccess = false;
                result.Abort = true;
                result.Message = pendingOrder.FailureMessage;

                return result;
            }

            if (pendingOrder.IsPendingAuthorizations)
            {
                var authorizations = pendingOrder.Authorizations;

                if (authorizations.Any(a => a.IsFailure))
                {
                    //failed to begin the order
                    result.IsSuccess = false;
                    result.Abort = true;
                    result.Message = $"{authorizations.FirstOrDefault(a => a.IsFailure)?.AuthorizationError}";

                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result }, logThisEvent: false);

                    return result;
                }
                else
                {
                    // store the Order Uri so we can resume the order later if required
                    managedCertificate.CurrentOrderUri = pendingOrder.OrderUri;
                }

                // perform all automated challenges (creating either http resources within the identifier
                // sites or creating DNS TXT records, depending on the challenge types)
                // the challenge is not yet submitted for checking by the CA

                await PrepareAutomatedChallengeResponses(log, managedCertificate, authorizations, result, config, progress);

                // if any challenge responses require a manual step, pause our request here and wait
                // for user intervention
                if (authorizations.Any(a => a.AttemptedChallenge?.IsAwaitingUser == true))
                {
                    var instructions = "";
                    foreach (var a in authorizations.Where(auth => auth.AttemptedChallenge?.IsAwaitingUser == true))
                    {
                        instructions += a.AttemptedChallenge.ChallengeResultMsg + "\r\n";
                    }

                    ReportProgress(
                        progress,
                        new RequestProgressState(RequestState.Paused, instructions, managedCertificate),
                        logThisEvent: true
                    );

                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Paused, instructions);

                    // if manual DNS and notification enabled, inform user (one or more)
                    if (CoreAppSettings.Current.EnableStatusReporting)
                    {
                        if (_pluginManager.DashboardClient != null)
                        {
                            // fire notification via API if user not interactive
                            if (!isInteractive)
                            {
                                await _pluginManager.DashboardClient.ReportUserActionRequiredAsync(new Models.Shared.ItemActionRequired
                                {
                                    InstanceId = managedCertificate.InstanceId,
                                    ManagedItemId = managedCertificate.Id,
                                    ItemTitle = managedCertificate.Name,
                                    ActionType = "manualdns",
                                    InstanceTitle = Environment.MachineName,
                                    Message = instructions,
                                    NotificationEmail = (await GetAccountDetails(managedCertificate))?.Email,
                                    AppVersion = Util.GetAppVersion().ToString() + ";" + Environment.OSVersion.ToString()
                                });
                            }
                        }
                    }

                    // return now and let user action the paused request
                    return result;
                }

                if (authorizations.Any(a => a.IsFailure == true))
                {
                    result.IsSuccess = false;
                    result.Abort = true;
                    result.Message = $"{authorizations.FirstOrDefault(a => a.IsFailure)?.AuthorizationError}";

                    log?.Error(result.Message);

                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result }, logThisEvent: false);

                    return result;
                }

                // if any of our authorizations require a delay for challenge response propagation, wait now.
                var propagationSecondsRequired = authorizations.Max(a => a.AttemptedChallenge?.PropagationSeconds);

                if (authorizations.Any() && propagationSecondsRequired > 0)
                {
                    var wait = propagationSecondsRequired;
                    while (wait > 0)
                    {
                        ReportProgress(
                            progress,
                            new RequestProgressState(RequestState.Paused, $"Pausing for {wait} seconds to allow for challenge response propagation.", managedCertificate),
                            logThisEvent: false
                            );
                        await Task.Delay(1000);
                        wait--;
                    }
                }
            }
            else
            {
                ReportProgress(
                           progress,
                           new RequestProgressState(RequestState.Running, $"Order authorizations already completed.", managedCertificate),
                           logThisEvent: true
                           );
            }

            // store the Order Uri, so we can refer to the order later if required
            if (!string.IsNullOrEmpty(pendingOrder.OrderUri))
            {
                managedCertificate.CurrentOrderUri = pendingOrder.OrderUri;
            }

            // perform any pending authorizations (submit challenges for validation) and complete processing
            return await CompleteCertificateRequest(log, managedCertificate, progress, pendingOrder);
        }

        /// <summary>
        /// For the given managed certificate get the applicable target server provider
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        private ITargetWebServer GetTargetServerProvider(ManagedCertificate managedCertificate)
        {
            var serverType = managedCertificate.RequestConfig.DeploymentTargetType ?? "iis";

            var sp = _serverProviders.FirstOrDefault(s => s.GetServerTypeInfo().ServerType.ToString().ToLower() == serverType);

            if (sp == null)
            {
                return _serverProviders.First();
            }
            else
            {
                return sp;
            }
        }

        /// <summary>
        /// Resume processing the current order for a managed certificate, submitting challenges and verifying status. Authorization challenge responses must have been prepared first.
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCertificate"></param>
        /// <param name="progress"></param>
        /// <param name="pendingOrder"></param>
        /// <returns></returns>
        private async Task<CertificateRequestResult> CompleteCertificateRequest(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, PendingOrder pendingOrder)
        {
            var result = new CertificateRequestResult(managedCertificate);

            var caAccount = await GetAccountDetails(managedCertificate, allowFailover: false, isResumedOrder: true);
            var _acmeClientProvider = await GetACMEProvider(managedCertificate, caAccount);

            _certificateAuthorities.TryGetValue(caAccount?.CertificateAuthorityId, out var certAuthority);

            log?.Information($"Resuming certificate request using CA: {certAuthority?.Title}");

            // if we don't have a pending order, load the details of the most recent order (can be used to re-fetch the existing cert)
            if (pendingOrder == null && managedCertificate.CurrentOrderUri != null)
            {
                pendingOrder = await _acmeClientProvider.BeginCertificateOrder(log, managedCertificate, resumeExistingOrder: true);

                if (pendingOrder.IsFailure)
                {
                    result.IsSuccess = false;
                    result.Message = pendingOrder.FailureMessage;

                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                    return result;
                }
            }
            else
            {
                if (pendingOrder == null)
                {
                    throw new Exception("No pending certificate order.");
                }
            }

            var validationFailed = false;
            var failureSummaryMessage = "";
            var cleanupChallengesLast = CoreAppSettings.Current.PerformChallengeCleanupsLast;

            if (pendingOrder.IsPendingAuthorizations)
            {
                var authorizations = pendingOrder.Authorizations;

                var distinctIdentifiers = managedCertificate.GetCertificateIdentifiers();

                if (!authorizations.All(a => a.IsValidated))
                {
                    // resume process, ask CA to check our challenge responses
                    foreach (var identifier in distinctIdentifiers)
                    {
                        var normalisedKey = identifier.IdentifierType == CertIdentifierType.Dns ? _idnMapping.GetAscii(identifier.Value).ToLower() : identifier.Value;

                        var authorization = authorizations.FirstOrDefault(a => a.Identifier?.Value == normalisedKey);

                        if (authorization?.IsValidated == true)
                        {
                            //skip already verified
                            log?.Information(string.Format(CoreSR.CertifyManager_DomainValidationSkipVerifed, identifier.Value));
                        }
                        else
                        {
                            var challengeConfig = managedCertificate.GetChallengeConfig(identifier);

                            if (authorization?.Identifier != null)
                            {
                                var msg = $"Attempting challenge response validation for: {identifier}";

                                log?.Information(msg);

                                ReportProgress(progress, new RequestProgressState(RequestState.Running, msg, managedCertificate), logThisEvent: false);

                                // check if authorization is pending, it may already be valid if an
                                // existing authorization was reused
                                if (authorization.Identifier.IsAuthorizationPending)
                                {
                                    ReportProgress(progress,
                                        new RequestProgressState(
                                            RequestState.Running,
                                            $"Checking automated challenge response for: {identifier}",
                                            managedCertificate
                                        )
                                    );

                                    // ask CA to check our answer to their authorization challenge
                                    // (http-01 or dns-01), ACME CA will then attempt to fetch our answer,
                                    // if all accessible and correct (authorized) ACME CA will then allow us
                                    // to request a certificate
                                    // note: rate limits apply to pending authorizations which have not yet been submitted so a
                                    // pending authorization for each identifier must be submitted even if others have already failed

                                    try
                                    {
                                        // ask ACME CA to validate our challenge response

                                        // this strategy could be optimised by submitting all challenges first, then checking the overall order authz, rather than polling each one

                                        if (authorization.AttemptedChallenge == null)
                                        {
                                            authorization.AttemptedChallenge = authorization.Challenges.FirstOrDefault(c => c.ChallengeType == challengeConfig.ChallengeType);
                                        }

                                        log?.Information($"Submitting challenge for validation: {identifier} {authorization?.AttemptedChallenge?.ResourceUri}");

                                        var submissionStatus = await _acmeClientProvider.SubmitChallenge(log, challengeConfig.ChallengeType, authorization);

                                        if (submissionStatus.IsOK)
                                        {
                                            authorization = await _acmeClientProvider.CheckValidationCompleted(log, challengeConfig.ChallengeType, authorization);

                                            if (!authorization.IsValidated)
                                            {
                                                var identifierInfo = authorization.Identifier;
                                                var errorMsg = authorization.AuthorizationError;

                                                failureSummaryMessage = string.Format(CoreSR.CertifyManager_DomainValidationFailed, identifier, errorMsg);

                                                ReportProgress(progress, new RequestProgressState(RequestState.Error, failureSummaryMessage, managedCertificate));

                                                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, failureSummaryMessage);

                                                validationFailed = true;
                                            }
                                            else
                                            {
                                                ReportProgress(progress, new RequestProgressState(RequestState.Running, string.Format(CoreSR.CertifyManager_DomainValidationCompleted, identifier), managedCertificate));
                                            }
                                        }
                                        else
                                        {
                                            failureSummaryMessage = submissionStatus.Message;
                                            ReportProgress(progress, new RequestProgressState(RequestState.Error, submissionStatus.Message, managedCertificate));

                                            await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, submissionStatus.Message);
                                            validationFailed = true;
                                        }
                                    }
                                    catch (Exception exp)
                                    {
                                        failureSummaryMessage = $"A problem occurred while checking challenge responses: {exp.ToString()}";

                                        log?.Error(failureSummaryMessage);
                                        validationFailed = true;
                                    }
                                    finally
                                    {
                                        // clean up challenge answers (.well-known/acme-challenge/* files or DNS entries)
                                        // for http-01 or dns-01)

                                        if (authorization.Cleanup != null && !cleanupChallengesLast)
                                        {
                                            await authorization.Cleanup();
                                        }
                                    }
                                }
                                else
                                {
                                    // we already have a completed authorization, check it's valid
                                    if (authorization.IsValidated)
                                    {
                                        log?.Information(string.Format(CoreSR.CertifyManager_DomainValidationSkipVerifed, identifier));
                                    }
                                    else
                                    {
                                        var errorMsg = "Failed";

                                        failureSummaryMessage = $"Validation failed: {identifier} \r\n{errorMsg}";

                                        log?.Error(failureSummaryMessage);

                                        validationFailed = true;
                                    }
                                }
                            }
                            else
                            {
                                // could not begin authorization

                                log?.Error($"Could not complete authorization for domain with the Certificate Authority: [{identifier}] {(authorization?.AuthorizationError ?? "Could not register domain identifier")}");

                                failureSummaryMessage = $"[{identifier}] : {authorization?.AuthorizationError}";

                                validationFailed = true;
                            }
                        }
                    }

                    if (cleanupChallengesLast)
                    {
                        // perform cleanups as batch
                        log?.Information($"Performing challenge cleanups.");

                        foreach (var authCleanup in authorizations)
                        {
                            if (authCleanup.Cleanup != null)
                            {
                                await authCleanup.Cleanup();
                            }
                        }
                    }
                }
            }

            if (!validationFailed)
            {
                // all identifiers validated, request the certificate
                ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RequestCertificate, managedCertificate));

                // check item or settings for preferred chain option, prefer most specific first in order of: Managed Cert config, Account Settings config, CA Default config
                var preferredChain = managedCertificate.RequestConfig.PreferredChain;

                if (string.IsNullOrEmpty(preferredChain) && !string.IsNullOrEmpty(caAccount.PreferredChain))
                {
                    preferredChain = caAccount.PreferredChain;
                }

                if (string.IsNullOrEmpty(preferredChain) && !string.IsNullOrEmpty(certAuthority?.DefaultPreferredChain))
                {
                    preferredChain = certAuthority.DefaultPreferredChain;
                }

                var pfxPwd = await GetPfxPassword(managedCertificate);

                var certRequestResult = await _acmeClientProvider.CompleteCertificateRequest(log, managedCertificate, pendingOrder.OrderUri, pfxPwd, preferredChain, CoreAppSettings.Current.DefaultKeyType, useModernPFXBuildAlgs: CoreAppSettings.Current.UseModernPFXAlgs);

                if (certRequestResult.IsSuccess)
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Success, CoreSR.CertifyManager_CompleteRequest, managedCertificate));

                    var primaryCertFilePath = certRequestResult.Result.ToString();

                    var certCleanupName = "";

                    // update managed site summary
                    try
                    {

                        X509Certificate2 certInfo = null;
                        if (!string.IsNullOrWhiteSpace(primaryCertFilePath) && primaryCertFilePath.EndsWith(".pfx", StringComparison.InvariantCultureIgnoreCase))
                        {
                            certInfo = CertificateManager.LoadCertificate(primaryCertFilePath, pfxPwd, throwOnError: true);
                        }
                        else if (certRequestResult.SupportingData is X509Certificate2)
                        {
                            certInfo = certRequestResult.SupportingData as X509Certificate2;
                        }

                        if (!string.IsNullOrWhiteSpace(certInfo.FriendlyName))
                        {
                            // PFX only has friendly name in the exported file version, if available this is then used for the cleanup later
                            certCleanupName = certInfo.FriendlyName.Substring(0, certInfo.FriendlyName.IndexOf("]") + 1);
                        }

                        managedCertificate.DateStart = new DateTimeOffset(certInfo.NotBefore);
                        managedCertificate.DateExpiry = new DateTimeOffset(certInfo.NotAfter);
                        managedCertificate.DateRenewed = DateTimeOffset.UtcNow;
                        managedCertificate.DateLastOcspCheck = DateTimeOffset.UtcNow;
                        managedCertificate.DateLastRenewalInfoCheck = DateTimeOffset.UtcNow;
                        managedCertificate.DateNextScheduledRenewalAttempt = null;

                        managedCertificate.CertificatePath = primaryCertFilePath;
                        managedCertificate.CertificatePreviousThumbprintHash = managedCertificate.CertificateThumbprintHash;
                        managedCertificate.CertificateThumbprintHash = certInfo.Thumbprint;
                        managedCertificate.CertificateRevoked = false;

                        managedCertificate.ARICertificateId = Certify.Shared.Core.Utils.PKI.CertUtils.GetARICertIdBase64(certInfo);
                        managedCertificate.CertificateCurrentCA = managedCertificate.LastAttemptedCA;
                    }
                    catch (Exception exp)
                    {
                        log?.Error("Failed to parse certificate: {exp}", exp);
                    }

                    // Install certificate into certificate store and bind to matching sites on server
                    var deploymentManager = new BindingDeploymentManager();

                    // select required target service provider (e.g. IIS)
                    var serverProvider = GetTargetServerProvider(managedCertificate);

                    // deploy certificate as required
                    if (managedCertificate.RequestConfig.DeploymentSiteOption != DeploymentOption.NoDeployment && managedCertificate.RequestConfig.DeploymentSiteOption != DeploymentOption.DeploymentStoreOnly)
                    {
                        ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_AutoBinding, managedCertificate));
                    }

                    var actions = await deploymentManager.StoreAndDeploy(
                            serverProvider.GetDeploymentTarget(),
                            managedCertificate,
                            primaryCertFilePath,
                            pfxPwd,
                            isPreviewOnly: false,
                            CoreAppSettings.Current.DefaultCertificateStore
                        );

                    log?.Debug("Performing post request cleanup as required");

                    if (!actions.Any(a => a.HasError))
                    {

                        await UpdateManagedCertificateStatus(managedCertificate, RequestState.Success);

                        result.IsSuccess = true;

                        // depending on the deployment type the final result will vary
                        result.Message = "New certificate received and standard deployment performed OK.";

                        if (managedCertificate.RequestConfig.DeploymentSiteOption == DeploymentOption.DeploymentStoreOnly)
                        {
                            result.Message = "New certificate received and stored OK.";
                        }
                        else if (managedCertificate.RequestConfig.DeploymentSiteOption == DeploymentOption.NoDeployment)
                        {
                            result.Message = "New certificate received OK.";
                        }
                        else
                        {
                            // certificate deployment was completed, log success
                            log?.Information(CoreSR.CertifyManager_CompleteRequestAndUpdateBinding);
                        }

                        ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate, false));

                        // perform cert cleanup (if enabled)
                        var cleanupStore = CoreAppSettings.Current.DefaultCertificateStore ?? CertificateManager.DEFAULT_STORE_NAME;
                        log?.Debug($"Cleanup processing: Windows {_useWindowsNativeFeatures} Enabled {CoreAppSettings.Current.EnableCertificateCleanup} Current Thumbprint {managedCertificate.CertificateThumbprintHash} Cleanup Name: {certCleanupName} CleanupStore: {cleanupStore}");
                        if (_useWindowsNativeFeatures && CoreAppSettings.Current.EnableCertificateCleanup && !string.IsNullOrEmpty(managedCertificate.CertificateThumbprintHash))
                        {
                            try
                            {

                                var mode = CoreAppSettings.Current.CertificateCleanupMode;

                                // if pref is for full cleanup, use After Renewal just for this renewal cleanup
                                if (mode == CertificateCleanupMode.FullCleanup)
                                {
                                    mode = CertificateCleanupMode.AfterRenewal;
                                }

                                if (mode == CertificateCleanupMode.AfterRenewal)
                                {
                                    _serviceLog.Information($"Checking for previous certs matching cleanup mode.");

                                    // cleanup certs based on the given cleanup mode
                                    var certsRemoved = CertificateManager.PerformCertificateStoreCleanup(
                                        mode ?? CertificateCleanupMode.AfterExpiry,
                                        DateTimeOffset.UtcNow,
                                        matchingName: certCleanupName,
                                        excludedThumbprints: new List<string> { managedCertificate.CertificateThumbprintHash },
                                        log: _serviceLog,
                                        storeName: cleanupStore
                                    );

                                    if (certsRemoved.Any())
                                    {
                                        foreach (var c in certsRemoved)
                                        {
                                            _serviceLog?.Information($"Cleanup removed cert: {c}");
                                        }
                                    }
                                    else
                                    {
                                        _serviceLog?.Debug($"No previous certs removed during cleanup.");
                                        log?.Debug($"No previous certs removed during cleanup.");
                                    }
                                }
                            }
                            catch (Exception exp)
                            {
                                // log exception
                                _serviceLog?.Error("Failed to perform certificate cleanup: " + exp.ToString());
                            }
                        }
                        else
                        {
                            _serviceLog?.Warning("Certificate cleanup on renewal is not enabled.");
                        }
                    }
                    else
                    {
                        log?.Debug($"One or more failures. Cleanup skipped.");

                        // we failed to install this cert or create/update the https binding
                        var msg = string.Join("\r\n",
                            actions.Where(s => s.HasError)
                           .Select(s => s.Description).ToArray()
                           );

                        result.Message = msg;

                        log?.Error(result.Message);

                        await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                    }
                }
                else
                {
                    log?.Debug($"Request Failed. Cleanup skipped.");

                    // certificate request failed
                    result.IsSuccess = false;
                    result.Message = $"The certificate order failed to complete. {certRequestResult.ErrorMessage ?? ""}";

                    log?.Error(result.Message);

                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                }
            }
            else
            {
                log?.Debug($"Failed to validate. Cleanup skipped.");

                //failed to validate all identifiers
                result.IsSuccess = false;
                result.Message = string.Format(CoreSR.CertifyManager_ValidationForChallengeNotSuccess, (failureSummaryMessage ?? ""));

                log?.Error(result.Message);

                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
            }

            log?.Debug($"End of CompleteCertificateRequest.");

            return result;
        }

        /// <summary>
        /// Get the decrypted PFX password for a given managed certificate (may be blank or a specific string from a stored credential)
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        private async Task<string> GetPfxPassword(ManagedCertificate managedCertificate)
        {
            var pfxPwd = "";
            var pwdCredentialId = managedCertificate.CertificatePasswordCredentialId ?? CoreAppSettings.Current.DefaultKeyCredentials ?? null;

            // if pwd specified for pfx (a default or specific to this managed cert), fetch from credentials store
            if (!string.IsNullOrEmpty(pwdCredentialId))
            {
                var cred = await _credentialsManager.GetUnlockedCredentialsDictionary(pwdCredentialId);
                if (cred != null)
                {
                    pfxPwd = cred["password"];
                }
            }

            return pfxPwd;
        }

        /// <summary>
        /// For each identifier in the current certificate order, prepare/implement the required automated challenge responses (DNS updates, http challenge responses)
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCertificate"></param>
        /// <param name="authorizations"></param>
        /// <param name="result"></param>
        /// <param name="config"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        private async Task PrepareAutomatedChallengeResponses(ILog log, ManagedCertificate managedCertificate, List<PendingAuthorization> authorizations, CertificateRequestResult result, CertRequestConfig config, IProgress<RequestProgressState> progress)
        {
            var failureSummaryMessage = "";

            var identifiers = managedCertificate.GetCertificateIdentifiers();

            foreach (var identifier in identifiers)
            {

                var authKey = identifier.IdentifierType == CertIdentifierType.Dns ? _idnMapping.GetAscii(identifier.Value).ToLower() : identifier.Value;

                var authorization = authorizations.FirstOrDefault(a => a.Identifier?.Value == authKey && a.Identifier?.IdentifierType.ToLower() == identifier.IdentifierType.ToLower());

                var challengeConfig = managedCertificate.GetChallengeConfig(identifier);

                // if our challenge takes a while to propagate, wait
                if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                {
                    result.ChallengeResponsePropagationSeconds = 60;
                }

                if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                {
                    // startup http challenge server if required
                    if (CoreAppSettings.Current.EnableHttpChallengeServer)
                    {
                        if (await IsHttpChallengeProcessStarted())
                        {
                            _httpChallengeServerAvailable = true;
                        }
                        else
                        {
                            _httpChallengeServerAvailable = await StartHttpChallengeServer();
                        }

                        if (_httpChallengeServerAvailable)
                        {
                            log?.Information($"Http Challenge Server process available.");
                        }
                        else
                        {
                            log?.Warning($"Http Challenge Server process enabled but unavailable (port 80 may be in use).");

                            _tc?.TrackEvent("ChallengeResponse_HttpChallengeServer_Unavailable");
                        }
                    }
                    else
                    {
                        log?.Information($"Http Challenge Server process is disabled. Only filesystem based validation via system web server will be possible.");
                    }
                }

                if (authorization?.Identifier != null)
                {

                    // check if authorization is pending, it may already be valid if an existing
                    // authorization was reused
                    if (!authorization.IsValidated)
                    {
                        var logmsg = $"Preparing automated challenge responses for: {identifier}";

                        log?.Information(logmsg);

                        ReportProgress(progress, new RequestProgressState(RequestState.Running, logmsg, managedCertificate), logThisEvent: false);

                        // store cache of normalisedKey/value responses for http challenge server use
                        var rc = authorization.Challenges?.FirstOrDefault(c => c.ChallengeType == challengeConfig.ChallengeType);
                        if (rc != null)
                        {
                            _currentChallenges.TryAdd(rc.Key,
                                new SimpleAuthorizationChallengeItem
                                {
                                    ChallengeType = rc.ChallengeType,
                                    Key = rc.Key,
                                    Value = rc.Value
                                });
                        }

                        var providerDesc = challengeConfig.ChallengeProvider ?? challengeConfig.ChallengeType;

                        _tc?.TrackEvent($"PerformChallengeResponse_{providerDesc}");

                        // perform our challenge response (http or dns)

                        var serverProvider = GetTargetServerProvider(managedCertificate);

                        authorization = await _challengeResponseService.PrepareAutomatedChallengeResponse(log, serverProvider, managedCertificate, authorization, _credentialsManager);

                        // if we had automated checks configured and they failed more than twice in a
                        // row, fail and report error here
                        if (
                            managedCertificate.RenewalFailureCount > 2
                            &&
                            (
                                (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP &&
                                 config.PerformExtensionlessConfigChecks &&
                                 !authorization.AttemptedChallenge?.ConfigCheckedOK == true) ||
                                (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI &&
                                 config.PerformTlsSniBindingConfigChecks &&
                                 !authorization.AttemptedChallenge?.ConfigCheckedOK == true)
                             )
                        )
                        {
                            //if we failed the config checks, report any errors
                            var msg = string.Format(CoreSR.CertifyManager_FailedPrerequisiteCheck, managedCertificate.ItemType);

                            if (authorization?.AttemptedChallenge?.ChallengeResultMsg != null)
                            {
                                msg += ":: " + authorization.AttemptedChallenge.ChallengeResultMsg;
                            }

                            log?.Error(msg);
                            result.Message = msg;

                            switch (challengeConfig.ChallengeType)
                            {
                                case SupportedChallengeTypes.CHALLENGE_TYPE_HTTP:
                                    result.Message =
                                        string.Format(CoreSR.CertifyManager_AutomateConfigurationCheckFailed_HTTP, identifier);
                                    break;

                                case SupportedChallengeTypes.CHALLENGE_TYPE_SNI:
                                    result.Message = Certify.Locales.CoreSR.CertifyManager_AutomateConfigurationCheckFailed_SNI;
                                    break;
                            }

                            ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result }, logThisEvent: false);

                            await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                            return;
                        }
                    }
                    else
                    {
                        log?.Information($"Authorization already valid for {identifier}");
                    }
                }
                else
                {
                    // could not begin authorization

                    log?.Error($"Could not begin authorization for identifier with the Certificate Authority: [{identifier}] {(authorization?.AuthorizationError ?? "Could not register identifier")} ");

                    failureSummaryMessage = $"[{identifier}] : {authorization?.AuthorizationError}";
                }
            }
        }

        /// <summary>
        /// Perform deployment for one or all managed certificates
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="progress"></param>
        /// <param name="isPreviewOnly"></param>
        /// <param name="includeDeploymentTasks"></param>
        /// <returns></returns>
        public async Task<List<CertificateRequestResult>> RedeployManagedCertificates(ManagedCertificateFilter filter, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false, bool includeDeploymentTasks = false)
        {
            _tc?.TrackEvent("RedeployCertificates");

            var managedCerts = await GetManagedCertificates(filter);

            var results = new List<CertificateRequestResult>();

            foreach (var managedCertificate in managedCerts)
            {
                if (!string.IsNullOrEmpty(managedCertificate.CertificatePath) && File.Exists(managedCertificate.CertificatePath))
                {
                    var result = await DeployCertificate(managedCertificate, progress, isPreviewOnly, includeDeploymentTasks);

                    results.Add(result);
                }
            }

            return results;
        }

        /// <summary>
        /// Perform automated deployment (if any)
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="progress"></param>
        /// <param name="isPreviewOnly"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> DeployCertificate(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false, bool includeDeploymentTasks = false)
        {
            var logPrefix = "";

            if (!isPreviewOnly)
            {
                _tc?.TrackEvent("DeployCertificate");
            }
            else
            {
                logPrefix = "[Preview Mode] ";
            }

            _serviceLog?.Information($"{(isPreviewOnly ? "Previewing" : "Performing")} Certificate Deployment: {managedCertificate.Name}");

            var result = new CertificateRequestResult(managedCertificate);
            var config = managedCertificate.RequestConfig;
            var pfxPath = managedCertificate.CertificatePath;

            // perform required deployment
            if (!isPreviewOnly)
            {

                if (!System.IO.File.Exists(pfxPath))
                {
                    return new CertificateRequestResult(managedCertificate, isSuccess: false, msg: $"[{managedCertificate.Name}] Certificate path is invalid or file does not exist. Cannot deploy certificate.");
                }

                ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_AutoBinding, managedCertificate));
            }

            var pfxPwd = await GetPfxPassword(managedCertificate);

            // Install certificate into certificate store and bind to IIS site
            var deploymentManager = new BindingDeploymentManager();

            var serverProvider = GetTargetServerProvider(managedCertificate);

            var actions = await deploymentManager.StoreAndDeploy(
                    serverProvider.GetDeploymentTarget(),
                    managedCertificate,
                    pfxPath,
                    pfxPwd,
                    isPreviewOnly: isPreviewOnly,
                    CoreAppSettings.Current.DefaultCertificateStore
                );

            result.Actions = actions;

            if (!actions.Any(a => a.HasError))
            {
                //all done
                LogMessage(managedCertificate.Id, logPrefix + CoreSR.CertifyManager_CompleteRequestAndUpdateBinding, LogItemType.CertificateRequestSuccessful);

                if (!isPreviewOnly)
                {
                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Success);
                }

                result.IsSuccess = true;
                result.Message = logPrefix + string.Format(CoreSR.CertifyManager_CertificateInstalledAndBindingUpdated, config.PrimaryDomain);

                if (!isPreviewOnly)
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate));
                }

                // optionally perform deployment tasks
                if (includeDeploymentTasks && managedCertificate.PostRequestTasks?.Any() == true)
                {

                    // run applicable deployment tasks (whether success or failed), powershell
                    LogMessage(managedCertificate.Id, $"Performing Post-Request (Deployment) Tasks..");

                    var log = ManagedCertificateLog.GetLogger(managedCertificate.Id, _loggingLevelSwitch);

                    var results = await PerformTaskList(log, isPreviewOnly: isPreviewOnly, skipDeferredTasks: true, result, managedCertificate.PostRequestTasks);

                    // log results
                    var postRequestTasks = new ActionStep
                    {
                        Category = "Post-Request Tasks",
                        Key = "PostRequestTasks",
                        Substeps = new List<ActionStep>(),
                        HasError = results.Any(r => r.HasError),
                        HasWarning = results.Any(r => r.HasWarning),
                    };

                    foreach (var r in results)
                    {
                        LogMessage(managedCertificate.Id, $"{r.Title} :: {r.Description}", (r.HasError || r.HasWarning) ? LogItemType.CertificateRequestAttentionRequired : LogItemType.GeneralInfo);

                        r.Category = "Post-Request Tasks";
                        postRequestTasks.Substeps.Add(r);

                    }

                    result.Actions.Add(postRequestTasks);

                    // certificate may already be deployed to some extent so this counts a completed with warnings
                    if (results.Any(r => r.HasError))
                    {
                        result.IsSuccess = false;

                        var msg = $"Deployment Tasks did not complete successfully.";
                        result.Message = msg;
                    }
                }
            }
            else
            {
                // certificate install failed
                result.Message = logPrefix + string.Format(CoreSR.CertifyManager_CertificateInstallFailed, pfxPath);
                if (!isPreviewOnly)
                {
                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                }

                LogMessage(managedCertificate.Id, result.Message, LogItemType.GeneralError);
            }

            return result;
        }

        /// <summary>
        /// Perform certificate revocation via ACME
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        public async Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate)
        {
            _serviceLog?.Information($"Performing Certificate Revoke: {managedCertificate.Name}");

            if (log == null)
            {
                log = ManagedCertificateLog.GetLogger(managedCertificate.Id, _loggingLevelSwitch);
            }

            _tc?.TrackEvent("RevokeCertificate");

            log?.Warning($"Revoking certificate: {managedCertificate.Name}");

            var caAccount = await GetAccountDetails(managedCertificate, allowFailover: false, isResumedOrder: true);
            var acmeClientProvider = await GetACMEProvider(managedCertificate, caAccount);

            if (acmeClientProvider == null)
            {
                log?.Error($"Could not revoke certificate as no matching valid ACME account could be found.");
                return new StatusMessage { IsOK = false, Message = "Could not revoke certificate. No matching valid ACME account could be found" };
            }

            var result = await acmeClientProvider.RevokeCertificate(log, managedCertificate);

            if (result.IsOK)
            {
                log?.Information($"Certificate revocation completed: {managedCertificate.Name}");
                managedCertificate.CertificateRevoked = true;

                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error);
            }
            else
            {
                log?.Warning(result.Message);
            }

            return result;
        }
    }
}
