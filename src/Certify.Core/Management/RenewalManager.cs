using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        private static Progress<RequestProgressState> SetupProgressTracker(
            ManagedCertificate item, string renewalReason,
            ConcurrentDictionary<string, Progress<RequestProgressState>> progressTrackers,
            Action<IProgress<RequestProgressState>, RequestProgressState, bool> reportProgress
        )
        {

            // track progress
            var progressState = new RequestProgressState(RequestState.Running, "Starting..", item);
            var progressTracker = new Progress<RequestProgressState>(progressState.ProgressReport);

            progressTrackers.TryAdd(item.Id, progressTracker);

            reportProgress(progressTracker, new RequestProgressState(RequestState.Queued, $"Queued for renewal: {renewalReason}", item), false);

            return progressTracker;
        }

        public static async Task<List<CertificateRequestResult>> PerformRenewAll(
                ILog serviceLog,
                IManagedItemStore itemManager,
                RenewalSettings settings,
                RenewalPrefs prefs,
                Action<IProgress<RequestProgressState>, RequestProgressState, bool> reportProgress,
                Func<string, Task<bool>> isManagedCertificateRunning,
                Func<ManagedCertificate, IProgress<RequestProgressState>, bool, string, Task<CertificateRequestResult>> performCertificateRequest,
                ConcurrentDictionary<string, Progress<RequestProgressState>> progressTrackers = null
                )
        {

            var maxRenewalTasks = prefs.MaxRenewalRequests;
            if (maxRenewalTasks <= 0)
            {
                maxRenewalTasks = DEFAULT_CERTIFICATE_REQUEST_TASKS;
            }

            var renewalTasks = new List<Task<CertificateRequestResult>>();

            if (progressTrackers == null)
            {
                progressTrackers = new ConcurrentDictionary<string, Progress<RequestProgressState>>();
            }

            List<ManagedCertificate> managedCertificateBatch;

            if (settings.TargetManagedCertificates?.Any() == true)
            {
                // prepare renewal batch using just the selected set of target items
                var targetCerts = new List<ManagedCertificate>();

                foreach (var id in settings.TargetManagedCertificates)
                {
                    targetCerts.Add(await itemManager.GetById(id));
                }

                managedCertificateBatch = targetCerts;

                foreach (var item in managedCertificateBatch)
                {
                    var progressTracker = SetupProgressTracker(item, "", progressTrackers, reportProgress);

                    renewalTasks.Add(
                    new Task<CertificateRequestResult>(
                            () => performCertificateRequest(item, progressTracker, settings.IsPreviewMode, "Renewal requested").Result,
                            TaskCreationOptions.LongRunning
                            )
                    );
                }
            }
            else
            {

                // prepare batch of renewals until we have reached the limit of tasks we will perform in one pass, or run out of items to attempt

                // auto renew enabled sites in order of oldest date renewed (or earliest attempted), items not yet attempted are first.
                var filter = new ManagedCertificateFilter
                {
                    IncludeOnlyNextAutoRenew = (settings.Mode == RenewalMode.Auto),
                    OrderBy = ManagedCertificateFilter.SortMode.RENEWAL_ASC
                };

                /*   if (settings.Mode == RenewalMode.Auto || settings.Mode == RenewalMode.RenewalsDue)
                    {

                        // if mode is just RenewalDue then we also include items that are not marked auto renew (the user may be controlling when to perform renewal).

                        managedCertificateBatch = managedCertificateBatch.Where(s => s.IncludeInAutoRenew == true || settings.Mode == RenewalMode.RenewalsDue)
                                     .OrderBy(s => s.DateRenewed ?? s.DateLastRenewalAttempt ?? DateTimeOffset.MinValue);
                    }
                    else if (settings.Mode == RenewalMode.NewItems)
                    {
                        // new items not yet completed in order of oldest renewal attempt first
                        managedCertificateBatch = managedCertificateBatch.Where(s => s.DateRenewed == null)
                                      .OrderBy(s => s.DateLastRenewalAttempt ?? DateTimeOffset.UtcNow.AddHours(-48));
                    }
                    else if (settings.Mode == RenewalMode.RenewalsWithErrors)
                    {
                        // items with current errors in order of oldest renewal attempt first
                        managedCertificateBatch = managedCertificateBatch.Where(s => s.LastRenewalStatus == RequestState.Error)
                                      .OrderBy(s => s.DateLastRenewalAttempt ?? DateTimeOffset.UtcNow.AddHours(-1));
                    }*/

                var totalRenewalCandidates = await itemManager.CountAll(filter);

                var renewalIntervalDays = prefs.RenewalIntervalDays;
                var renewalIntervalMode = prefs.RenewalIntervalMode ?? RenewalIntervalModes.DaysAfterLastRenewal;

                filter.PageSize = MAX_CERTIFICATE_REQUEST_TASKS;
                filter.PageIndex = 0;

                var batch = new List<ManagedCertificate>();
                var resultsRemaining = totalRenewalCandidates;

                // identify items we will attempt and begin tracking progress
                while (batch.Count < maxRenewalTasks && resultsRemaining > 0)
                {
                    var results = await itemManager.Find(filter);
                    resultsRemaining = results.Count;

                    foreach (var item in results)
                    {
                        if (batch.Count < maxRenewalTasks)
                        {
                            // if cert is not awaiting manual user input (manual DNS etc), proceed with renewal checks
                            if (item.LastRenewalStatus != RequestState.Paused)
                            {
                                // check if item is due for renewal based on current settings

                                var renewalDueCheck = ManagedCertificate.CalculateNextRenewalAttempt(item, renewalIntervalDays, renewalIntervalMode, checkFailureStatus: false);
                                var isRenewalRequired = (settings.Mode != RenewalMode.Auto && settings.Mode != RenewalMode.RenewalsDue) || renewalDueCheck.IsRenewalDue;

                                var renewalReason = renewalDueCheck.Reason;

                                if (settings.Mode == RenewalMode.All)
                                {
                                    // on all mode, everything gets an attempted renewal
                                    isRenewalRequired = true;
                                    renewalReason = "Renewal Mode is set to All";
                                }

                                // if we care about stopped sites being stopped, check if a specific site is selected and if it's running
                                if (!prefs.IncludeStoppedSites && !string.IsNullOrEmpty(item.ServerSiteId) && item.RequestConfig.DeploymentSiteOption == DeploymentOption.SingleSite)
                                {
                                    var isSiteRunning = await isManagedCertificateRunning(item.Id);

                                    if (!isSiteRunning)
                                    {
                                        isRenewalRequired = false;
                                        renewalReason = "Target site is not running and 'Include Stopped Sites' preference is False. Renewal will not be attempted.";
                                    }
                                }

                                if (isRenewalRequired && !renewalDueCheck.IsRenewalOnHold)
                                {
                                    batch.Add(item);

                                    var progressTracker = SetupProgressTracker(item, "", progressTrackers, reportProgress);

                                    renewalTasks.Add(
                                        new Task<CertificateRequestResult>(
                                                () => performCertificateRequest(item, progressTracker, settings.IsPreviewMode, renewalReason).Result,
                                                TaskCreationOptions.LongRunning
                                                )
                                        );
                                }
                            }
                        }
                    }

                    filter.PageIndex++;
                }

                managedCertificateBatch = batch;
            }

            if (managedCertificateBatch.Count(c => c.LastRenewalStatus == RequestState.Error) > MAX_CERTIFICATE_REQUEST_TASKS)
            {
                serviceLog?.Warning("Too many failed certificates outstanding. Fix failures or delete. Failures: " + managedCertificateBatch.Count(c => c.LastRenewalStatus == RequestState.Error));
            }

            if (!renewalTasks.Any())
            {
                //nothing to do

                return new List<CertificateRequestResult>();
            }
            else
            {
                serviceLog?.Information($"Attempting {renewalTasks.Count} renewal tasks. Max renewal tasks is set to {maxRenewalTasks}, max supported tasks is {MAX_CERTIFICATE_REQUEST_TASKS}");
            }

            if (prefs.PerformParallelRenewals)
            {
                renewalTasks.ForEach(t => t.Start());
                return (await Task.WhenAll(renewalTasks)).ToList();
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
            var requiredCaFeatures = new List<CertAuthoritySupportedRequests>();
            var identifiers = item.GetCertificateIdentifiers();

            if (identifiers.Any(i => i.IdentifierType == CertIdentifierType.Dns && i.Value.StartsWith("*")))
            {
                requiredCaFeatures.Add(CertAuthoritySupportedRequests.DOMAIN_WILDCARD);
            }

            if (identifiers.Count(i => i.IdentifierType == CertIdentifierType.Dns) == 1)
            {
                requiredCaFeatures.Add(CertAuthoritySupportedRequests.DOMAIN_SINGLE);
            }

            if (identifiers.Count(i => i.IdentifierType == CertIdentifierType.Dns) > 2)
            {
                requiredCaFeatures.Add(CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN);
            }

            if (identifiers.Count(i => i.IdentifierType == CertIdentifierType.Ip) == 1)
            {
                requiredCaFeatures.Add(CertAuthoritySupportedRequests.IP_SINGLE);
            }

            if (identifiers.Count(i => i.IdentifierType == CertIdentifierType.Ip) > 1)
            {
                requiredCaFeatures.Add(CertAuthoritySupportedRequests.IP_MULTIPLE);
            }

            if (identifiers.Any(i => i.IdentifierType == CertIdentifierType.TnAuthList))
            {
                requiredCaFeatures.Add(CertAuthoritySupportedRequests.TNAUTHLIST);
            }

            if (item.RequestConfig.PreferredExpiryDays > 0)
            {
                requiredCaFeatures.Add(CertAuthoritySupportedRequests.OPTIONAL_LIFETIME_DAYS);
            }

            var fallbackCandidateAccounts = accounts.Where(a => a.CertificateAuthorityId != defaultCA && a.IsStagingAccount == item.UseStagingMode);
            var fallbackAccounts = new List<AccountDetails>();

            if (fallbackCandidateAccounts.Any())
            {
                // select a candidate based on features required by the certificate. If a CA has no known features we assume it supports all the ones we might be interested in
                foreach (var ca in certificateAuthorities)
                {
                    if (!ca.SupportedFeatures.Any() || requiredCaFeatures.All(r => ca.SupportedFeatures.Contains(r.ToString())))
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
                    var nextFallback = fallbackAccounts.FirstOrDefault(f => f.CertificateAuthorityId != item.LastAttemptedCA);

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
