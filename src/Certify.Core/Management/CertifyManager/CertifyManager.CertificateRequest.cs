using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Plugins;
using Certify.Models.Providers;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        /// <summary>
        /// The maximum number of certificate requests which will be attempted in a batch (Renew All)
        /// </summary>
        private const int MAX_CERTIFICATE_REQUEST_TASKS = 50;

        /// <summary>
        /// Internet domain name ASCII > Unicode mapping provider
        /// </summary>
        private IdnMapping _idnMapping = new IdnMapping();

        private bool _isRenewAllInProgress { get; set; }

        /// <summary>
        /// Perform Renew All: identify all items to renew then initiate renewal process
        /// </summary>
        /// <param name="autoRenewalOnly">  </param>
        /// <param name="progressTrackers">  </param>
        /// <returns>  </returns>
        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedCertificates(RenewalSettings settings, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null)
        {
            if (_isRenewAllInProgress)
            {
                Debug.WriteLine("Renew all is already is progress..");
                return await Task.FromResult(new List<CertificateRequestResult>());
            }

            _serviceLog?.Information($"Performing Renew All for all applicable managed certificates.");

            _isRenewAllInProgress = true;

            // we can perform request in parallel but if processing many requests this can cause issues committing IIS bindings etc
            var performRequestsInParallel = false;

            var testModeOnly = false;

            IEnumerable<ManagedCertificate> managedCertificates;

            if (settings.TargetManagedCertificates?.Any() == true)
            {
                var targetCerts = new List<ManagedCertificate>();
                foreach (var id in settings.TargetManagedCertificates)
                {
                    targetCerts.Add(await _itemManager.GetById(id));
                }

                managedCertificates = targetCerts;

            }
            else
            {
                managedCertificates = await _itemManager.Find(
                    new ManagedCertificateFilter
                    {
                        IncludeOnlyNextAutoRenew = (settings.Mode == RenewalMode.Auto)
                    }
                );
            }

            if (settings.Mode == RenewalMode.Auto || settings.Mode == RenewalMode.RenewalsDue)
            {
                // auto renew enabled sites in order of oldest date renewed (or earliest attempted), items not yet attempted are first.
                // if mode is just RenewalDue then we also include items that are not marked auto renew (the user may be controlling when to perform renewal).

                managedCertificates = managedCertificates.Where(s => s.IncludeInAutoRenew == true || settings.Mode == RenewalMode.RenewalsDue)
                             .OrderBy(s => s.DateRenewed ?? s.DateLastRenewalAttempt ?? DateTime.MinValue);
            }
            else if (settings.Mode == RenewalMode.NewItems)
            {
                // new items not yet completed in order of oldest renewal attempt first
                managedCertificates = managedCertificates.Where(s => s.DateRenewed == null)
                              .OrderBy(s => s.DateLastRenewalAttempt ?? DateTime.Now.AddHours(-48));
            }
            else if (settings.Mode == RenewalMode.RenewalsWithErrors)
            {
                // items with current errors in order of oldest renewal attempt first
                managedCertificates = managedCertificates.Where(s => s.LastRenewalStatus == RequestState.Error)
                              .OrderBy(s => s.DateLastRenewalAttempt ?? DateTime.Now.AddHours(-1));
            }

            // check site list and examine current certificates. If certificate is less than n days
            // old, don't attempt to renew it
            var sitesToRenew = new List<ManagedCertificate>();
            var renewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays;
            var renewalIntervalMode = CoreAppSettings.Current.RenewalIntervalMode ?? RenewalIntervalModes.DaysAfterLastRenewal;

            var numRenewalTasks = 0;
            var maxRenewalTasks = CoreAppSettings.Current.MaxRenewalRequests;

            var renewalTasks = new List<Task<CertificateRequestResult>>();

            if (progressTrackers == null)
            {
                progressTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            }

            if (managedCertificates.Count(c => c.LastRenewalStatus == RequestState.Error) > MAX_CERTIFICATE_REQUEST_TASKS)
            {
                System.Diagnostics.Debug.WriteLine("Too many failed certificates. Fix failures or delete. Failures: " + managedCertificates.Count(c => c.LastRenewalStatus == RequestState.Error));
            }

            foreach (var managedCertificate in managedCertificates)
            {
                var progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCertificate);
                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);
                progressTrackers.Add(managedCertificate.Id, progressIndicator);

                BeginTrackingProgress(progressState);

                // determine if this site currently requires renewal for auto mode (or renewals due mode)
                // In auto mode we skip if recent failures, in Renewals Due mode we ignore recent failures
                var isRenewalRequired = (settings.Mode != RenewalMode.Auto && settings.Mode != RenewalMode.RenewalsDue) || IsRenewalRequired(managedCertificate, renewalIntervalDays, renewalIntervalMode, checkFailureStatus: false);

                var isRenewalOnHold = false;

                if (isRenewalRequired && settings.Mode == RenewalMode.Auto)
                {
                    //check if we have renewal failures, if so wait a bit longer.
                    isRenewalOnHold = !IsRenewalRequired(managedCertificate, renewalIntervalDays, renewalIntervalMode, checkFailureStatus: true);

                    if (isRenewalOnHold)
                    {
                        isRenewalRequired = false;
                    }
                }

                if (settings.Mode == RenewalMode.All)
                {
                    // on all mode, everything gets an attempted renewal
                    isRenewalRequired = true;
                }

                //if we care about stopped sites being stopped, check for that if a specific site is selected
                var isSiteRunning = true;
                if (!CoreAppSettings.Current.IgnoreStoppedSites && !string.IsNullOrEmpty(managedCertificate.ServerSiteId))
                {
                    isSiteRunning = await IsManagedCertificateRunning(managedCertificate.Id);
                }

                if ((isRenewalRequired && isSiteRunning) || testModeOnly)
                {
                    //get matching progress tracker for this site
                    IProgress<RequestProgressState> tracker = null;
                    if (progressTrackers != null)
                    {
                        tracker = progressTrackers[managedCertificate.Id];
                    }

                    // limit the number of renewal tasks to attempt in this pass either to custom setting or max allowed
                    if (
                        (maxRenewalTasks == 0 && numRenewalTasks < MAX_CERTIFICATE_REQUEST_TASKS)
                        || (maxRenewalTasks > 0 && numRenewalTasks < maxRenewalTasks && numRenewalTasks < MAX_CERTIFICATE_REQUEST_TASKS))
                    {
                        if (testModeOnly)
                        {
                            //simulated request for UI testing

                            renewalTasks.Add(
                                new Task<CertificateRequestResult>(
                                () => PerformDummyCertificateRequest(managedCertificate, tracker).Result,
                                TaskCreationOptions.LongRunning
                            ));
                        }
                        else
                        {
                            renewalTasks.Add(
                               new Task<CertificateRequestResult>(
                               () => PerformCertificateRequest(null, managedCertificate, tracker, skipRequest: settings.IsPreviewMode, skipTasks: settings.IsPreviewMode).Result,
                               TaskCreationOptions.LongRunning
                           ));
                        }
                    }
                    else
                    {
                        //send progress back to report skip
                        var progress = (IProgress<RequestProgressState>)progressTrackers[managedCertificate.Id];
                        ReportProgress(progress, new RequestProgressState(RequestState.NotRunning, "Skipped renewal because the max requests per batch has been reached. This request will be attempted again later.", managedCertificate), true);
                    }

                    // track number of tasks being attempted, not counting failures (otherwise cumulative failures can eventually exhaust allowed number of task)
                    if (managedCertificate.LastRenewalStatus != RequestState.Error)
                    {
                        numRenewalTasks++;
                    }
                }
                else
                {
                    var msg = CoreSR.CertifyManager_SkipRenewalOk;
                    var logThisEvent = false;

                    if (isRenewalRequired && !isSiteRunning)
                    {
                        //TODO: show this as warning rather than success
                        msg = CoreSR.CertifyManager_SiteStopped;
                    }

                    if (isRenewalOnHold)
                    {
                        //TODO: show this as warning rather than success

                        msg = string.Format(CoreSR.CertifyManager_RenewalOnHold, managedCertificate.RenewalFailureCount);
                        logThisEvent = true;
                    }

                    if (progressTrackers != null)
                    {
                        //send progress back to report skip
                        var progress = (IProgress<RequestProgressState>)progressTrackers[managedCertificate.Id];
                        ReportProgress(progress, new RequestProgressState(RequestState.Success, msg, managedCertificate), logThisEvent);
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
                //var results = await Task.WaitAll(renewalTasks);
                var results = new List<CertificateRequestResult>();
                foreach (var t in renewalTasks)
                {
                    t.Start();
                    results.Add(await t);
                }

                _isRenewAllInProgress = false;
                return results.ToList();
            }
            else
            {
                var results = new List<CertificateRequestResult>();

                // perform all renewal tasks one after the other
                foreach (var t in renewalTasks)
                {
                    t.RunSynchronously();
                    results.Add(await t);
                }

                _isRenewAllInProgress = false;
                return results;
            }
        }

        /// <summary>
        /// if we know the last renewal date, check whether we should renew again, otherwise assume
        /// it's more than 30 days ago by default and attempt renewal
        /// </summary>
        /// <param name="s">  </param>
        /// <param name="renewalIntervalDays">  </param>
        /// <param name="checkFailureStatus">  </param>
        /// <returns>  </returns>
        public static bool IsRenewalRequired(ManagedCertificate s, int renewalIntervalDays, string renewalIntervalMode, bool checkFailureStatus = false)
        {
            var timeNow = DateTime.Now;

            var timeSinceLastRenewal = (s.DateRenewed ?? timeNow.AddDays(-30)) - timeNow;

            var timeToExpiry = (s.DateExpiry ?? timeNow) - timeNow;

            var isRenewalRequired = false;

            if (renewalIntervalMode == RenewalIntervalModes.DaysBeforeExpiry)
            {
                // is item expiring within N days
                isRenewalRequired = Math.Abs(timeToExpiry.TotalDays) <= renewalIntervalDays;
            }
            else
            {
                // was item renewed more than N days ago
                isRenewalRequired = Math.Abs(timeSinceLastRenewal.TotalDays) > renewalIntervalDays;
            }

            // if we have never attempted renewal, renew now
            if (!isRenewalRequired && (s.DateLastRenewalAttempt == null && s.DateRenewed == null))
            {
                isRenewalRequired = true;
            }

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
            bool isInteractive = false
            )
        {
            _serviceLog?.Information($"Performing Certificate Request: {managedCertificate.Name} [{managedCertificate.Id}]");

            // Perform pre-request checks and scripting hooks, invoke main request process, then
            // perform an post request scripting hooks
            if (log == null)
            {
                log = ManagedCertificateLog.GetLogger(managedCertificate.Id, _loggingLevelSwitch);
            }

            LogMessage(managedCertificate.Id, $"---- Beginning Request [{managedCertificate.Name}] ----");

            // start with a failure result, set to success when succeeding
            var certRequestResult = new CertificateRequestResult(managedCertificate);

            var config = managedCertificate.RequestConfig;

            managedCertificate.RenewalFailureMessage = ""; // clear any previous renewal error or instructions

            try
            {

                if (managedCertificate.PreRequestTasks?.Any() == true && managedCertificate.Health != ManagedCertificateHealth.AwaitingUser)
                {
                    // run pre-request tasks, currently if any of these fail the request will abort

                    LogMessage(managedCertificate.Id, $"Performing Pre-Request Tasks..");

                    var results = await PerformTaskList(log, isPreviewOnly: false, skipDeferredTasks: true, certRequestResult, managedCertificate.PreRequestTasks);

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
                        // LogMessage(managedCertificate.Id, $"{r.Title} :: {r.Description}", (r.HasError || r.HasWarning) ? LogItemType.CertificateRequestAttentionRequired : LogItemType.GeneralInfo);

                        r.Category = "Pre-Request Tasks";
                        preRequestTasks.Substeps.Add(r);

                    }

                    certRequestResult.Actions.Add(preRequestTasks);

                    if (results.Any(r => r.HasError))
                    {
                        certRequestResult.Abort = true;

                        var msg = $"Request was aborted due to failed Pre-Request Task.";

                        certRequestResult.Message = msg;
                    }
                }

                // if the script has requested the certificate request to be aborted, skip the request
                if (!certRequestResult.Abort)
                {

                    if (!skipRequest && managedCertificate.SkipCertificateRequest != true)
                    {
                        if (resumePaused && managedCertificate.Health == ManagedCertificateHealth.AwaitingUser)
                        {
                            // resume a previously paused request
                            CertificateRequestResult r;

                            // If mixing manual dns with acme-dns, manual challenges need to be checked without re-challenging
                            if (managedCertificate.RequestConfig.Challenges?.Any(c => c.ChallengeProvider == "DNS01.Manual") == true)
                            {
                                // resume manual dns requests etc
                                r = await CompleteCertificateRequestProcessing(log, managedCertificate, progress, null);

                            }
                            else
                            {
                                // perform normal certificate challenge/response/renewal (acme-dns etc)
                                r = await PerformCertificateRequestProcessing(log, managedCertificate, progress, certRequestResult, config, isInteractive);
                            }

                            // copy result from sub-request, preserve existing logged actions
                            certRequestResult.Message = r.Message;
                            certRequestResult.IsSuccess = r.IsSuccess;
                            certRequestResult.ManagedItem = r.ManagedItem;
                            certRequestResult.Result = r.Result;
                            certRequestResult.Abort = r.Abort;

                        }
                        else
                        {
                            if (managedCertificate.Health != ManagedCertificateHealth.AwaitingUser)
                            {
                                // perform normal certificate challenge/response/renewal
                                var r = await PerformCertificateRequestProcessing(log, managedCertificate, progress, certRequestResult, config, isInteractive);

                                certRequestResult.Message = r.Message;
                                certRequestResult.IsSuccess = r.IsSuccess;
                                certRequestResult.ManagedItem = r.ManagedItem;
                                certRequestResult.Result = r.Result;
                                certRequestResult.Abort = r.Abort;
                            }
                            else
                            {
                                // request is waiting on user input but has been automatically initiated,
                                // therefore skip for now
                                certRequestResult.Abort = true;
                                LogMessage(managedCertificate.Id, $"Certificate Request Skipped, Awaiting User Input: {managedCertificate.Name}");
                            }
                        }
                    }
                    else
                    {
                        // caller asked to skip the actual certicate request (e.g. unit testing)

                        if (failOnSkip)
                        {
                            certRequestResult.Message = $"Certificate Request Skipped (on demand, marked as failed): {managedCertificate.Name}";
                            // LogMessage(managedCertificate.Id, msg);
                            certRequestResult.IsSuccess = false;
                        }
                        else
                        {
                            certRequestResult.Message = $"Certificate Request Skipped (on demand): {managedCertificate.Name}";
                            // LogMessage(managedCertificate.Id, msg);
                            certRequestResult.IsSuccess = managedCertificate.LastRenewalStatus == RequestState.Success;
                        }

                        ReportProgress(progress, new RequestProgressState(RequestState.Success, certRequestResult.Message, managedCertificate));
                    }
                }
            }
            catch (Exception exp)
            {
                // overall exception thrown during process

                certRequestResult.IsSuccess = false;
                certRequestResult.Abort = true;

                try
                {
                    // attempt to log error

                    log?.Error(exp, $"Certificate request process failed: {exp}");

                    certRequestResult.Message = string.Format(Certify.Locales.CoreSR.CertifyManager_RequestFailed, managedCertificate.Name, exp.Message, exp);

                    LogMessage(managedCertificate.Id, certRequestResult.Message, LogItemType.CertificateRequestFailed);

                    ReportProgress(progress, new RequestProgressState(RequestState.Error, certRequestResult.Message, managedCertificate));

                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, certRequestResult.Message);
                }
                catch { }
            }
            finally
            {
                certRequestResult.ManagedItem = managedCertificate;

                // if request is not awaiting user and there are any post requests tasks, run them now
                if (managedCertificate.PostRequestTasks?.Any() == true && managedCertificate.Health != ManagedCertificateHealth.AwaitingUser)
                {

                    // run applicable deployment tasks (whether success or failed), powershell
                    LogMessage(managedCertificate.Id, $"Performing Post-Request (Deployment) Tasks..");

                    var results = await PerformTaskList(log, isPreviewOnly: false, skipDeferredTasks: true, certRequestResult, managedCertificate.PostRequestTasks);

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

                    certRequestResult.Actions.Add(postRequestTasks);

                    // certificate may already be deployed to some extent so this counts a completed with warnings
                    if (results.Any(r => r.HasError))
                    {
                        certRequestResult.IsSuccess = false;

                        var msg = $"Deployment Tasks did not complete successfully.";
                        certRequestResult.Message = msg;
                    }
                }

                // final state is either paused, success or error
                var finalState = managedCertificate.Health == ManagedCertificateHealth.AwaitingUser ?
                     RequestState.Paused :
                     (certRequestResult.IsSuccess ? RequestState.Success : RequestState.Error);

                ReportProgress(progress, new RequestProgressState(finalState, certRequestResult.Message, managedCertificate));

                if (string.IsNullOrEmpty(certRequestResult.Message) && !string.IsNullOrEmpty(managedCertificate.RenewalFailureMessage))
                {
                    certRequestResult.Message = managedCertificate.RenewalFailureMessage;
                }

                await UpdateManagedCertificateStatus(managedCertificate, finalState, certRequestResult.Message);
            }

            return certRequestResult;
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
        private async Task<CertificateRequestResult> PerformCertificateRequestProcessing(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, CertificateRequestResult result, CertRequestConfig config, bool isInteractive)
        {
            //primary domain and each subject alternative name must now be registered as an identifier with ACEM CA and validated
            LogMessage(managedCertificate.Id, $"{Util.GetUserAgent()}");

            var _acmeClientProvider = await GetACMEProvider(managedCertificate);

            if (_acmeClientProvider == null)
            {
                result.IsSuccess = false;
                result.Abort = true;
                result.Message = $"There is no matching ACME account for the currently selected Certificate Authority. Check you have added a {(managedCertificate.UseStagingMode ? "Staging" : "Production")} account for the CA under the app Settings.";

                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result });
                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                return result;
            }

            LogMessage(managedCertificate.Id, $"Beginning Certificate Request Process: {managedCertificate.Name} using ACME Provider:{_acmeClientProvider.GetProviderName()}");

            LogMessage(managedCertificate.Id, $"Requested identifiers to include on certificate: {string.Join(";", managedCertificate.GetCertificateDomains())}");

            ReportProgress(progress,
                new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RegisterDomainIdentity, managedCertificate)
            );

#pragma warning disable CS0618 // Type or member is obsolete
            if (config.ChallengeType == null && (config.Challenges == null || !config.Challenges.Any()))
#pragma warning restore CS0618 // Type or member is obsolete
            {
                config.Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                    new List<CertRequestChallengeConfig> {
                       new CertRequestChallengeConfig{
                           ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                            }
                        });
            }

            var identifierAuthorizations = new List<PendingAuthorization>();

            // start the validation process for each domain

            // begin authorization by registering the cert order. The response will include a list of
            // authorizations per domain. Authorizations may already be validated or we may still
            // have to complete the authorization challenge. When rate limits are encountered, this
            // step may fail.
            var pendingOrder = await _acmeClientProvider.BeginCertificateOrder(log, config);

            if (pendingOrder.IsPendingAuthorizations)
            {
                var authorizations = pendingOrder.Authorizations;

                if (authorizations.Any(a => a.IsFailure))
                {
                    //failed to begin the order
                    result.IsSuccess = false;
                    result.Abort = true;
                    result.Message = $"{authorizations.FirstOrDefault(a => a.IsFailure)?.AuthorizationError}";

                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result });
                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                    LogMessage(managedCertificate.Id, result.Message, LogItemType.CertificateRequestFailed);

                    return result;
                }
                else
                {
                    // store the Order Uri so we can resume the order later if required
                    managedCertificate.CurrentOrderUri = pendingOrder.OrderUri;
                }

                // perform all automated challenges (creating either http resources within the domain
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
                                    NotificationEmail = (await GetAccountDetailsForManagedItem(managedCertificate))?.Email,
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

                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result });
                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                    LogMessage(managedCertificate.Id, result.Message, LogItemType.CertificateRequestFailed);

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

            // perform any pending authorizations (submit challenges for validation) and complete processing
            return await CompleteCertificateRequestProcessing(log, managedCertificate, progress, pendingOrder);
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
        private async Task<CertificateRequestResult> CompleteCertificateRequestProcessing(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, PendingOrder pendingOrder)
        {
            var result = new CertificateRequestResult(managedCertificate);

            var _acmeClientProvider = await GetACMEProvider(managedCertificate);

            // if we don't have a pending order, load the details of the most recent order
            if (pendingOrder == null && managedCertificate.CurrentOrderUri != null)
            {
                pendingOrder = await _acmeClientProvider.BeginCertificateOrder(log, managedCertificate.RequestConfig, managedCertificate.CurrentOrderUri);
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

            if (pendingOrder.IsPendingAuthorizations)
            {
                var authorizations = pendingOrder.Authorizations;

                var distinctDomains = managedCertificate.GetCertificateDomains();

                if (!authorizations.All(a => a.IsValidated))
                {
                    // resume process, ask CA to check our challenge responses
                    foreach (var domain in distinctDomains)
                    {
                        var asciiDomain = _idnMapping.GetAscii(domain).ToLower();

                        var authorization = authorizations.FirstOrDefault(a => a.Identifier?.Dns == asciiDomain);

                        var challengeConfig = managedCertificate.GetChallengeConfig(domain);

                        if (authorization?.Identifier != null)
                        {
                            LogMessage(managedCertificate.Id, $"Attempting Challenge Response Validation for Domain: {domain}", LogItemType.CertificateRequestStarted);

                            ReportProgress(progress,
                                new RequestProgressState(RequestState.Running,
                                    string.Format(Certify.Locales.CoreSR.CertifyManager_RegisteringAndValidatingX0, domain),
                                    managedCertificate)
                            );

                            // check if authorization is pending, it may already be valid if an
                            // existing authorization was reused
                            if (authorization.Identifier.IsAuthorizationPending)
                            {
                                ReportProgress(progress,
                                    new RequestProgressState(
                                        RequestState.Running,
                                        $"Checking automated challenge response for Domain: {domain}",
                                        managedCertificate
                                    )
                                );

                                // ask LE to check our answer to their authorization challenge
                                // (http-01 or tls-sni-01), ACME CA will then attempt to fetch our answer,
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

                                    var submissionStatus = await _acmeClientProvider.SubmitChallenge(log, challengeConfig.ChallengeType, authorization);

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

                                            validationFailed = true;
                                        }
                                        else
                                        {
                                            ReportProgress(progress, new RequestProgressState(RequestState.Running, string.Format(CoreSR.CertifyManager_DomainValidationCompleted, domain), managedCertificate));
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

                                    LogMessage(managedCertificate.Id, failureSummaryMessage);
                                    validationFailed = true;
                                }
                                finally
                                {
                                    // clean up challenge answers (.well-known/acme-challenge/* files
                                    // for http-01 or iis bindings for tls-sni-01)

                                    authorization.Cleanup();
                                }
                            }
                            else
                            {
                                // we already have a completed authorization, check it's valid
                                if (authorization.IsValidated)
                                {
                                    LogMessage(managedCertificate.Id, string.Format(CoreSR.CertifyManager_DomainValidationSkipVerifed, domain));
                                }
                                else
                                {
                                    var errorMsg = "";
                                    if (authorization?.Identifier != null)
                                    {
                                        errorMsg = authorization.Identifier.ValidationError;
                                    }

                                    failureSummaryMessage = $"Domain validation failed: {domain} \r\n{errorMsg}";

                                    LogMessage(managedCertificate.Id, failureSummaryMessage);

                                    validationFailed = true;
                                }
                            }
                        }
                        else
                        {
                            // could not begin authorization

                            LogMessage(managedCertificate.Id,
                                $"Could not complete authorization for domain with the Certificate Authority: [{domain}] {(authorization?.AuthorizationError ?? "Could not register domain identifier")}");
                            failureSummaryMessage = $"[{domain}] : {authorization?.AuthorizationError}";

                            validationFailed = true;
                        }
                    }
                }
            }

            if (!validationFailed)
            {
                // all identifiers validated, request the certificate
                ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RequestCertificate, managedCertificate));

                // check item or settings for preferred chain option
                var preferredChain = managedCertificate.RequestConfig.PreferredChain;
                var acc = await GetAccountDetailsForManagedItem(managedCertificate);

                if (string.IsNullOrEmpty(preferredChain) && !string.IsNullOrEmpty(acc?.PreferredChain))
                {
                    preferredChain = acc.PreferredChain;
                }

                // if no other preferred chain set, check if this CA config has a default, if so use that if we are in production mode for the account
                var currentCaId = GetCurrentCAId(managedCertificate);
                _certificateAuthorities.TryGetValue(currentCaId, out var certAuthority);

                if (!string.IsNullOrEmpty(certAuthority?.DefaultPreferredChain) && acc?.IsStagingAccount != true)
                {
                    preferredChain = certAuthority.DefaultPreferredChain;
                }

                var pfxPwd = await GetPfxPassword(managedCertificate);

                var certRequestResult = await _acmeClientProvider.CompleteCertificateRequest(log, managedCertificate.RequestConfig, pendingOrder.OrderUri, pfxPwd, preferredChain);

                if (certRequestResult.IsSuccess)
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Success, CoreSR.CertifyManager_CompleteRequest, managedCertificate));

                    var primaryCertFilePath = certRequestResult.Result.ToString();

                    var certCleanupName = "";

                    // update managed site summary
                    try
                    {
                        var certInfo = CertificateManager.LoadCertificate(primaryCertFilePath, pfxPwd);

                        certCleanupName = certInfo.FriendlyName.Substring(0, certInfo.FriendlyName.IndexOf("]") + 1);
                        managedCertificate.DateStart = certInfo.NotBefore;
                        managedCertificate.DateExpiry = certInfo.NotAfter;
                        managedCertificate.DateRenewed = DateTime.Now;

                        managedCertificate.CertificatePath = primaryCertFilePath;
                        managedCertificate.CertificatePreviousThumbprintHash = managedCertificate.CertificateThumbprintHash;
                        managedCertificate.CertificateThumbprintHash = certInfo.Thumbprint;
                        managedCertificate.CertificateRevoked = false;

                    }
                    catch (Exception)
                    {
                        LogMessage(managedCertificate.Id, "Failed to parse certificate dates", LogItemType.GeneralError);
                    }

                    // deploy certificate as required
                    ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_AutoBinding, managedCertificate));

                    // Install certificate into certificate store and bind to matching sites on server
                    var deploymentManager = new BindingDeploymentManager();

                    // select required target service provider (e.g. IIS)
                    var serverProvider = GetTargetServerProvider(managedCertificate);

                    var actions = await deploymentManager.StoreAndDeploy(
                            serverProvider.GetDeploymentTarget(),
                            managedCertificate,
                            primaryCertFilePath,
                            pfxPwd,
                            isPreviewOnly: false,
                            CoreAppSettings.Current.DefaultCertificateStore
                        );

                    if (!actions.Any(a => a.HasError))
                    {
                        // certificate deployment was completed, log success
                        LogMessage(managedCertificate.Id, CoreSR.CertifyManager_CompleteRequestAndUpdateBinding, LogItemType.CertificateRequestSuccessful);

                        await UpdateManagedCertificateStatus(managedCertificate, RequestState.Success);

                        result.IsSuccess = true;

                        // depending on the deployment type the final result will vary
                        // string.Format(CoreSR.CertifyManager_CertificateInstalledAndBindingUpdated, config.PrimaryDomain);
                        result.Message = "Request completed";
                        ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate, false));

                        // perform cert cleanup (if enabled)
                        if (_useWindowsNativeFeatures && CoreAppSettings.Current.EnableCertificateCleanup && !string.IsNullOrEmpty(managedCertificate.CertificateThumbprintHash))
                        {
                            try
                            {
                                var mode = CoreAppSettings.Current.CertificateCleanupMode;

                                // default to After Expiry cleanup if no preference specified
                                if (mode == null)
                                {
                                    mode = CertificateCleanupMode.AfterExpiry;
                                }

                                // if pref is for full cleanup, use After Renewal just for this renewal cleanup
                                if (mode == CertificateCleanupMode.FullCleanup)
                                {
                                    mode = CertificateCleanupMode.AfterRenewal;
                                }

                                // cleanup certs based on the given cleanup mode
                                var certsRemoved = CertificateManager.PerformCertificateStoreCleanup(
                                   (CertificateCleanupMode)mode,
                                    DateTime.Now,
                                    matchingName: certCleanupName,
                                    excludedThumbprints: new List<string> { managedCertificate.CertificateThumbprintHash },
                                    log: _serviceLog, CoreAppSettings.Current.DefaultCertificateStore ?? CertificateManager.DEFAULT_STORE_NAME
                                );

                                if (certsRemoved.Any())
                                {
                                    foreach (var c in certsRemoved)
                                    {
                                        _serviceLog.Information($"Cleanup removed cert: {c}");
                                    }
                                }
                            }
                            catch (Exception exp)
                            {
                                // log exception
                                _serviceLog.Error("Failed to perform certificate cleanup: " + exp.ToString());
                            }
                        }
                    }
                    else
                    {
                        // we failed to install this cert or create/update the https binding
                        var msg = string.Join("\r\n",
                            actions.Where(s => s.HasError)
                           .Select(s => s.Description).ToArray()
                           );

                        result.Message = msg;

                        await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                        LogMessage(managedCertificate.Id, result.Message, LogItemType.GeneralError);
                    }
                }
                else
                {
                    // certificate request failed
                    result.IsSuccess = false;
                    result.Message = string.Format(CoreSR.CertifyManager_LetsEncryptServiceTimeout, certRequestResult.ErrorMessage ?? "");

                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                    LogMessage(managedCertificate.Id, result.Message, LogItemType.CertificateRequestFailed);
                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate));
                }
            }
            else
            {

                //failed to validate all identifiers
                result.IsSuccess = false;
                result.Message = string.Format(CoreSR.CertifyManager_ValidationForChallengeNotSuccess, (failureSummaryMessage ?? ""));

                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                LogMessage(managedCertificate.Id, result.Message, LogItemType.CertificateRequestFailed);

                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate));
            }

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

            var identifiers = new List<IdentifierItem>();

            var distinctDomains = managedCertificate.GetCertificateDomains();

            distinctDomains.ForEach(d => identifiers.Add(new IdentifierItem { Name = d, ItemType = "dns" }));

            managedCertificate.RequestConfig.SubjectIPAddresses?.ToList().ForEach(i => identifiers.Add(new IdentifierItem { Name = i, ItemType = "ip" }));

            foreach (var identifier in identifiers)
            {
                var asciiDomain = _idnMapping.GetAscii(identifier.Name).ToLower();

                var authorization = authorizations.FirstOrDefault(a => a.Identifier?.Dns == asciiDomain);

                var challengeConfig = managedCertificate.GetChallengeConfig(identifier.Name);

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

                            _tc?.TrackEvent("ChallengeResponse_HttpChallengeServer_Start");
                        }

                        if (_httpChallengeServerAvailable)
                        {
                            LogMessage(managedCertificate.Id, $"Http Challenge Server process available.", LogItemType.CertificateRequestStarted);
                        }
                        else
                        {
                            LogMessage(managedCertificate.Id, $"Http Challenge Server process unavailable.", LogItemType.CertificateRequestStarted);

                            _tc?.TrackEvent("ChallengeResponse_HttpChallengeServer_Unavailable");
                        }
                    }
                    else
                    {
                        LogMessage(managedCertificate.Id, $"Http Challenge Server process is disabled. Only filesystem based validation via system web server will be possible.", LogItemType.CertificateRequestStarted);
                    }
                }

                if (authorization?.Identifier != null)
                {
                    LogMessage(managedCertificate.Id, $"Attempting Domain Validation: {identifier.Name}", LogItemType.CertificateRequestStarted);

                    ReportProgress(progress, new RequestProgressState(RequestState.Running, string.Format(Certify.Locales.CoreSR.CertifyManager_RegisteringAndValidatingX0, identifier.Name), managedCertificate));

                    // check if authorization is pending, it may already be valid if an existing
                    // authorization was reused
                    if (authorization.Identifier.IsAuthorizationPending)
                    {
                        ReportProgress(progress,
                            new RequestProgressState(
                                RequestState.Running,
                                $"Preparing automated challenge responses ({identifier.Name})",
                                managedCertificate
                            )
                        );

                        // store cache of key/value responses for http challenge server use
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

                        // ask LE to check our answer to their authorization challenge (http-01 or
                        // tls-sni-01), LE will then attempt to fetch our answer, if all accessible
                        // and correct (authorized) LE will then allow us to request a certificate

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

                            LogMessage(managedCertificate.Id, msg, LogItemType.CertificateRequestFailed);
                            result.Message = msg;

                            switch (challengeConfig.ChallengeType)
                            {
                                case SupportedChallengeTypes.CHALLENGE_TYPE_HTTP:
                                    result.Message =
                                        string.Format(CoreSR.CertifyManager_AutomateConfigurationCheckFailed_HTTP, identifier.Name);
                                    break;

                                case SupportedChallengeTypes.CHALLENGE_TYPE_SNI:
                                    result.Message = Certify.Locales.CoreSR.CertifyManager_AutomateConfigurationCheckFailed_SNI;
                                    break;
                            }

                            ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result });

                            await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                            return;
                        }
                        else
                        {
                            ReportProgress(progress, new RequestProgressState(RequestState.Running, string.Format(CoreSR.CertifyManager_RequestValidationFromCertificateAuthority, identifier.Name), managedCertificate));
                        }
                    }
                    else
                    {
                        log.Information($"Authorization already valid for {identifier.Name}");
                    }
                }
                else
                {
                    // could not begin authorization

                    LogMessage(managedCertificate.Id, $"Could not begin authorization for identifier with the Certificate Authority: [{identifier.Name}] {(authorization?.AuthorizationError ?? "Could not register identifier")} ");

                    failureSummaryMessage = $"[{identifier.Name}] : {authorization?.AuthorizationError}";
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
        /// Fetch an existing certificate from the certificate authority
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="progress"></param>
        /// <param name="isPreviewOnly"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> FetchCertificate(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false)
        {

            if (!isPreviewOnly)
            {
                _tc?.TrackEvent("RefetchCertificate");
            }

            _serviceLog?.Information($"{(isPreviewOnly ? "Previewing" : "Performing")} Certificate Refetch: {managedCertificate.Name}");

            return await CompleteCertificateRequestProcessing(_serviceLog, managedCertificate, progress, null);
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

            var _acmeClientProvider = await GetACMEProvider(managedCertificate);
            if (_acmeClientProvider == null)
            {
                log?.Error($"Could not revoke certificate as no matching valid ACME account could be found.");
                return new StatusMessage { IsOK = false, Message = "Could not revoke certificate. No matching valid ACME account could be found" };
            }

            var result = await _acmeClientProvider.RevokeCertificate(log, managedCertificate);

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
