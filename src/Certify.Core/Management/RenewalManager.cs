using System;
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
            Func<ManagedCertificate, IProgress<RequestProgressState>, bool, Task<CertificateRequestResult>> PerformCertificateRequest,
            Dictionary<string, Progress<RequestProgressState>> progressTrackers = null
            )
        {
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
            var renewalIntervalDays = prefs.RenewalIntervalDays;
            var renewalIntervalMode = prefs.RenewalIntervalMode ?? RenewalIntervalModes.DaysAfterLastRenewal;

            var numRenewalTasks = 0;
            var maxRenewalTasks = prefs.MaxRenewalRequests;

            var renewalTasks = new List<Task<CertificateRequestResult>>();

            if (progressTrackers == null)
            {
                progressTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            }

            if (managedCertificates.Count(c => c.LastRenewalStatus == RequestState.Error) > MAX_CERTIFICATE_REQUEST_TASKS)
            {
                _serviceLog?.Warning("Too many failed certificates outstanding. Fix failures or delete. Failures: " + managedCertificates.Count(c => c.LastRenewalStatus == RequestState.Error));
            }

            foreach (var managedCertificate in managedCertificates)
            {
                var progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCertificate);
                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

                try
                {
                    progressTrackers.Add(managedCertificate.Id, progressIndicator);
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

                var isRenewalOnHold = false;

                if (isRenewalRequired && settings.Mode == RenewalMode.Auto)
                {
                    //check if we have renewal failures, if so wait a bit longer.
                    isRenewalOnHold = !ManagedCertificate.IsRenewalRequired(managedCertificate, renewalIntervalDays, renewalIntervalMode, checkFailureStatus: true);

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
                if (prefs.IncludeStoppedSites && !string.IsNullOrEmpty(managedCertificate.ServerSiteId))
                {
                    isSiteRunning = await IsManagedCertificateRunning(managedCertificate.Id);
                }

                if ((isRenewalRequired && isSiteRunning) && !testModeOnly)
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
                            () => PerformCertificateRequest(managedCertificate, tracker, settings.IsPreviewMode).Result,
                            TaskCreationOptions.LongRunning
                       ));

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

                return new List<CertificateRequestResult>();
            }

            if (performRequestsInParallel)
            {
                var results = new List<CertificateRequestResult>();
                foreach (var t in renewalTasks)
                {
                    t.Start();
                    results.Add(await t);
                }

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

                return results;
            }
        }
    }
}
