using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Providers;

namespace Certify.Management
{
    public static class RenewalManager
    {
        /// <summary>
        /// The maximum number of certificate requests which will be attempted in a batch (Renew All)
        /// </summary>
        private const int MAX_CERTIFICATE_REQUEST_TASKS = 75;

        private const int DEFAULT_CERTIFICATE_REQUEST_TASKS = 50;

        public static async Task<List<CertificateRequestResult>> PerformRenewAll(
            ILog _serviceLog,
            IManagedItemStore _itemManager,
            RenewalSettings settings,
            RenewalPrefs prefs,
            Action<RequestProgressState> BeginTrackingProgress,
            Action<IProgress<RequestProgressState>, RequestProgressState, bool> ReportProgress,
            Func<string, Task<bool>> IsManagedCertificateRunning,
            Func<ManagedCertificate, IProgress<RequestProgressState>, bool, string, Task<CertificateRequestResult>> PerformCertificateRequest,
            ConcurrentDictionary<string, Progress<RequestProgressState>> progressTrackers = null
            )
        {
            // we can perform request in parallel but if processing many requests this can cause issues committing IIS bindings etc

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
            var renewalIntervalDays = prefs.RenewalIntervalDays;
            var renewalIntervalMode = prefs.RenewalIntervalMode ?? RenewalIntervalModes.DaysAfterLastRenewal;

            var numRenewalTasks = 0;
            var maxRenewalTasks = prefs.MaxRenewalRequests;

            var renewalTasks = new List<Task<CertificateRequestResult>>();

            if (progressTrackers == null)
            {
                progressTrackers = new ConcurrentDictionary<string, Progress<RequestProgressState>>();
            }

            if (managedCertificates.Count(c => c.LastRenewalStatus == RequestState.Error) > MAX_CERTIFICATE_REQUEST_TASKS)
            {
                _serviceLog?.Warning("Too many failed certificates outstanding. Fix failures or delete. Failures: " + managedCertificates.Count(c => c.LastRenewalStatus == RequestState.Error));
            }

            foreach (var managedCertificate in managedCertificates)
            {
                // if cert is not awaiting manual user input (manual DNS etc), proceed with renewal checks
                if (managedCertificate.LastRenewalStatus != RequestState.Paused)
                {
                    var progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCertificate);
                    var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

                    try
                    {
                        progressTrackers.TryAdd(managedCertificate.Id, progressIndicator);
                    }
                    catch
                    {
                        _serviceLog?.Error($"Failed to add progress tracker for {managedCertificate.Id}. Likely concurrency issue, skipping this managed cert during this run.");
                        continue;
                    }

                    BeginTrackingProgress(progressState);

                    // determine if this site currently requires renewal for auto mode (or renewals due mode)
                    // In auto mode we skip if recent failures, in Renewals Due mode we ignore recent failures

                    var renewalDueCheck = ManagedCertificate.CalculateNextRenewalAttempt(managedCertificate, renewalIntervalDays, renewalIntervalMode, checkFailureStatus: false);
                    var isRenewalRequired = (settings.Mode != RenewalMode.Auto && settings.Mode != RenewalMode.RenewalsDue) || renewalDueCheck.IsRenewalDue;

                    var renewalReason = renewalDueCheck.Reason;

                    if (settings.Mode == RenewalMode.All)
                    {
                        // on all mode, everything gets an attempted renewal
                        isRenewalRequired = true;
                        renewalReason = "Renewal Mode is set to All";
                    }

                    //if we care about stopped sites being stopped, check for that if a specific site is selected
                    var isSiteRunning = true;
                    if (prefs.IncludeStoppedSites && !string.IsNullOrEmpty(managedCertificate.ServerSiteId))
                    {
                        isSiteRunning = await IsManagedCertificateRunning(managedCertificate.Id);
                    }

                    if (!renewalDueCheck.IsRenewalOnHold && isRenewalRequired && isSiteRunning && !testModeOnly)
                    {
                        //get matching progress tracker for this site
                        IProgress<RequestProgressState> tracker = null;
                        if (progressTrackers != null)
                        {
                            tracker = progressTrackers[managedCertificate.Id];
                        }

                        // limit the number of renewal tasks to attempt in this pass either to custom setting or max allowed
                        if ((maxRenewalTasks == 0 && numRenewalTasks < DEFAULT_CERTIFICATE_REQUEST_TASKS)
                            || (maxRenewalTasks > 0 && numRenewalTasks < maxRenewalTasks && numRenewalTasks < MAX_CERTIFICATE_REQUEST_TASKS))
                        {

                            renewalTasks.Add(
                               new Task<CertificateRequestResult>(
                                () => PerformCertificateRequest(managedCertificate, tracker, settings.IsPreviewMode, renewalReason).Result,
                                TaskCreationOptions.LongRunning
                           ));

                            ReportProgress((IProgress<RequestProgressState>)progressTrackers[managedCertificate.Id], new RequestProgressState(RequestState.Queued, $"Queued for renewal: {renewalDueCheck.Reason}", managedCertificate), false);

                        }
                        else
                        {
                            if (!prefs.SuppressSkippedItems)
                            {
                                //send progress back to report skip
                                var progress = (IProgress<RequestProgressState>)progressTrackers[managedCertificate.Id];
                                ReportProgress(progress, new RequestProgressState(RequestState.NotRunning, "Skipped renewal because the max requests per batch has been reached. This request will be attempted again later.", managedCertificate, isSkipped: true), true);
                            }
                            else
                            {
                                _serviceLog.Debug($"Skipping item {managedCertificate.Id}:{managedCertificate.Name}, max batch size exceeded.");
                            }
                        }

                        // track number of tasks being attempted, not counting failures (otherwise cumulative failures can eventually exhaust allowed number of task)
                        if (managedCertificate.LastRenewalStatus != RequestState.Error)
                        {
                            numRenewalTasks++;
                        }
                    }
                    else
                    {
                        var msg = renewalDueCheck.Reason;
                        var requestState = RequestState.Success;

                        var logThisEvent = false;

                        if (isRenewalRequired && !isSiteRunning)
                        {
                            msg = CoreSR.CertifyManager_SiteStopped;
                        }

                        if (renewalDueCheck.IsRenewalOnHold)
                        {
                            logThisEvent = true;
                        }

                        if (progressTrackers != null)
                        {
                            if (!renewalDueCheck.IsRenewalDue || renewalDueCheck.IsRenewalOnHold && prefs.SuppressSkippedItems)
                            {
                                _serviceLog.Debug($"Skipping item {managedCertificate.Id}:{managedCertificate.Name}, UI reporting suppressed: {msg}");
                            }
                            else
                            {
                                //send progress back to report skip
                                /* var progress = (IProgress<RequestProgressState>)progressTrackers[managedCertificate.Id];
                                 ReportProgress(progress, new RequestProgressState(requestState, msg, managedCertificate, isSkipped: true), logThisEvent);*/
                            }
                        }
                    }
                }
            }

            if (!renewalTasks.Any())
            {
                //nothing to do

                return new List<CertificateRequestResult>();
            }
            else
            {
                _serviceLog.Information($"Attempting {renewalTasks.Count} renewal tasks. Max renewal tasks is set to {maxRenewalTasks}, max supported tasks is {MAX_CERTIFICATE_REQUEST_TASKS}");
            }

            if (prefs.PerformParallelRenewals)
            {
                renewalTasks.ForEach(t => t.Start());

                var allTaskResults = await Task.WhenAll(renewalTasks);

                return allTaskResults.ToList();
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

                return results;
            }
        }

        /// <summary>
        /// Select subset of CA accounts which support the required features for this certificate (or have unknown features)
        /// </summary>
        /// <param name="item"></param>
        /// <param name="defaultCA"></param>
        /// <param name="certificateAuthorities"></param>
        /// <param name="accounts"></param>
        /// <returns></returns>
        private static List<AccountDetails> GetAccountsWithRequiredCAFeatures(ManagedCertificate item, string defaultCA, ICollection<CertificateAuthority> certificateAuthorities, List<AccountDetails> accounts)
        {
            var requiredCAFeatures = new List<CertAuthoritySupportedRequests>();
            var identifiers = item.GetCertificateIdentifiers();

            if (identifiers.Any(i => i.IdentifierType == CertIdentifierType.Dns && i.Value.StartsWith("*")))
            {
                requiredCAFeatures.Add(CertAuthoritySupportedRequests.DOMAIN_WILDCARD);
            }

            if (identifiers.Count(i => i.IdentifierType == CertIdentifierType.Dns) == 1)
            {
                requiredCAFeatures.Add(CertAuthoritySupportedRequests.DOMAIN_SINGLE);
            }

            if (identifiers.Count(i => i.IdentifierType == CertIdentifierType.Dns) > 2)
            {
                requiredCAFeatures.Add(CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN);
            }

            if (identifiers.Any(i => i.IdentifierType == CertIdentifierType.Ip))
            {
                requiredCAFeatures.Add(CertAuthoritySupportedRequests.IP_SINGLE);
            }

            if (identifiers.Count(i => i.IdentifierType == CertIdentifierType.Ip) > 1)
            {
                requiredCAFeatures.Add(CertAuthoritySupportedRequests.IP_MULTIPLE);
            }

            if (identifiers.Any(i => i.IdentifierType == CertIdentifierType.TnAuthList))
            {
                requiredCAFeatures.Add(CertAuthoritySupportedRequests.TNAUTHLIST);
            }

            if (item.RequestConfig.PreferredExpiryDays > 0)
            {
                requiredCAFeatures.Add(CertAuthoritySupportedRequests.OPTIONAL_LIFETIME_DAYS);
            }

            var fallbackCandidateAccounts = accounts.Where(a => a.CertificateAuthorityId != defaultCA && a.IsStagingAccount == item.UseStagingMode);
            var fallbackAccounts = new List<AccountDetails>();

            if (fallbackCandidateAccounts.Any())
            {
                // select a candidate based on features required by the certificate. If a CA has no known features we assume it supports all the ones we might be interested in
                foreach (var ca in certificateAuthorities)
                {
                    if (!ca.SupportedFeatures.Any() || requiredCAFeatures.All(r => ca.SupportedFeatures.Contains(r.ToString())))
                    {
                        fallbackAccounts.AddRange(fallbackCandidateAccounts.Where(f => f.CertificateAuthorityId == ca.Id));
                    }
                }
            }

            return fallbackAccounts;
        }

        /// <summary>
        /// select certificate authority account for fallback if required, based on type of certificate being request and CA features
        /// </summary>
        /// <param name="accounts"></param>
        /// <param name="item"></param>
        /// <param name="defaultMatchingAccount"></param>
        /// <returns></returns>
        public static AccountDetails SelectCAWithFailover(ICollection<CertificateAuthority> certificateAuthorities, List<AccountDetails> accounts, ManagedCertificate item, AccountDetails defaultMatchingAccount)
        {
            if (accounts.Count == 1)
            {
                // nothing else to choose from, can't perform failover
                return defaultMatchingAccount;
            }

            // If item has been failing recently, decide if we should attempt failover to a fallback account with another CA

            if (item.LastRenewalStatus == RequestState.Error && item.RenewalFailureCount > 2)
            {
                // decide features we prefer the target CA to support
                var fallbackAccounts = GetAccountsWithRequiredCAFeatures(item, defaultMatchingAccount?.CertificateAuthorityId, certificateAuthorities, accounts);

                if (fallbackAccounts.Any())
                {
                    // use the next suitable fallback account
                    var nextFallback = fallbackAccounts.FirstOrDefault(f => f.CertificateAuthorityId != item.LastAttemptedCA && f.CertificateAuthorityId != defaultMatchingAccount?.CertificateAuthorityId);

                    if (nextFallback != null)
                    {
                        nextFallback.IsFailoverSelection = true;
                        return nextFallback;
                    }
                }
            }

            // no fallback required/possible, use default
            return defaultMatchingAccount;
        }
    }
}
