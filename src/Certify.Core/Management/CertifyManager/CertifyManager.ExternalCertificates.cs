#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Server.Hub.Api;
using Certify.Shared;
using Certify.Shared.Core.Utils.PKI;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        internal enum SubscriptionRequestMode
        {
            Automatic,
            Manual
        }

        internal class ExternalCertificateFetchResult
        {
            public bool IsSuccess { get; set; }
            public bool HasUpdate { get; set; }
            public string? SourceVersion { get; set; }
            public byte[]? CertificateData { get; set; }
            public string? Message { get; set; }
        }

        private class ExternalCertificateValidationResult
        {
            public bool IsValid { get; set; }
            public string? Message { get; set; }
            public DateTimeOffset? DateStart { get; set; }
            public DateTimeOffset? DateExpiry { get; set; }
            public int? PercentageElapsed { get; set; }
            public string? Thumbprint { get; set; }
        }

        /// <summary>
        /// The outcome of an attempt to fetch and deploy a certificate update from an external (subscription) source.
        /// This drives deployment task trigger evaluation, so the three cases are kept distinct rather than inferred
        /// from a success flag plus the item's stored status - a failed request must never be able to present itself
        /// as a success left over from a previous run
        /// </summary>
        internal enum SubscriptionRequestOutcome
        {
            /// <summary>
            /// The request ran to completion and the certificate we now hold is the one to deploy. ON_SUCCESS
            /// deployment tasks apply
            /// </summary>
            Completed,

            /// <summary>
            /// Nothing was attempted or applied, because no update was available yet, the subscription was not due,
            /// deployment was deliberately deferred, or the subscription is not configured well enough to attempt.
            /// The source will be checked again later, so no deployment and no deployment tasks apply
            /// </summary>
            Deferred,

            /// <summary>
            /// The request was attempted against the source and failed. ON_ERROR deployment tasks apply
            /// </summary>
            Failed
        }

        /// <summary>
        /// The outcome of an attempt to fetch and deploy a certificate update from an external (subscription) source
        /// </summary>
        internal class SubscriptionProcessResult
        {
            public SubscriptionProcessResult(string? msg, SubscriptionRequestOutcome outcome)
            {
                Message = msg ?? string.Empty;
                Outcome = outcome;
            }

            public string Message { get; }
            public SubscriptionRequestOutcome Outcome { get; }
        }

        private int _isSubscriptionTaskRunning = 0;
        private int _isSubscriptionPassRequested = 0;

        /// <summary>
        /// Subscriptions currently waiting for a maintenance window before an available update can be applied, and when
        /// each wait began. The wait is re-evaluated on every pass, so this is what keeps the item log to one entry per
        /// wait rather than one per pass. It is in memory only - a deferral is derived from the item and the current
        /// time, so nothing is lost by rebuilding it after a restart
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _subscriptionsAwaitingMaintenanceWindow = new();

        /// <summary>
        /// Message reported when a certificate fetched from an external source could not be loaded as a PFX
        /// </summary>
        public const string SubscriptionPfxLoadErrorMessage = "External certificate update could not be validated as deployable PFX data. The PFX may require a different password credential setting.";

        private async Task<List<ManagedCertificate>> GetSubscriptionTargets()
        {
            var allItems = await _itemManager.Find(ManagedCertificateFilter.ALL);

            return allItems
                .Where(i => i.IsActionableSubscription)
                .ToList();
        }

        /// <summary>
        /// Request a subscription pass to pick up an update we have just been notified about, rather than leaving it
        /// until the next scheduled pass. Requests made while a pass is running are coalesced into a single follow up
        /// pass, so a batch of updates arriving together does not queue a pass each
        /// </summary>
        private void RequestSubscriptionPass()
        {
            Interlocked.Exchange(ref _isSubscriptionPassRequested, 1);

            _ = Task.Run(() => PerformSubscriptionTasks(CancellationToken.None));
        }

        /// <summary>
        /// Check each configured subscription and apply any available certificate update. A pass which is already
        /// running services any request made while it runs, so only one pass takes place at a time
        /// </summary>
        /// <param name="cancellationToken"></param>
        private async Task PerformSubscriptionTasks(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _isSubscriptionTaskRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                do
                {
                    // cleared before items are selected, so an update stored during this pass requests another one
                    Interlocked.Exchange(ref _isSubscriptionPassRequested, 0);

                    await RunSubscriptionPass(cancellationToken);
                }
                while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _isSubscriptionPassRequested) == 1);
            }
            finally
            {
                Interlocked.Exchange(ref _isSubscriptionTaskRunning, 0);
            }
        }

        /// <summary>
        /// Perform a single pass over the configured subscriptions. Callers hold the subscription processing gate
        /// (<see cref="_isSubscriptionTaskRunning"/>)
        /// </summary>
        /// <param name="cancellationToken"></param>
        private async Task RunSubscriptionPass(CancellationToken cancellationToken)
        {
            try
            {
                if (IsInDegradedMode)
                {
                    return;
                }

                var targetItems = await GetSubscriptionTargets();

                // drop maintenance window waits for items which are no longer subscriptions we process, so the tracking
                // does not accumulate entries for items which have since been deleted or reconfigured
                if (!_subscriptionsAwaitingMaintenanceWindow.IsEmpty)
                {
                    var currentTargetIds = targetItems.Select(i => i.Id).ToHashSet();

                    foreach (var trackedId in _subscriptionsAwaitingMaintenanceWindow.Keys.Where(id => !currentTargetIds.Contains(id)).ToList())
                    {
                        _subscriptionsAwaitingMaintenanceWindow.TryRemove(trackedId, out _);
                    }
                }

                if (!targetItems.Any())
                {
                    return;
                }

                foreach (var item in targetItems)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // the source has no update waiting for us and is not yet due to be polled, so there is nothing
                    // for this pass to do. The item is left untouched rather than run through a request which would
                    // only record a no-op status against it and report that to connected UI clients
                    if (!ShouldProcessSubscription(item, item.ExternalSource))
                    {
                        _serviceLog?.Verbose("Skipping subscription check for {name} [{id}]: no update or poll is due, or attempts are being spaced out after repeated failures.", item.Name, item.Id);
                        continue;
                    }

                    // the item's place in _renewalsInProgress is what keeps this pass and a renewal driven request off
                    // the same item: the subscription gate only covers this pass, and a redeployment of the certificate
                    // already held goes through the renewal pass without taking it. A renewal driven request holds its
                    // place for the whole request, including its post-request deployment tasks, which run after the
                    // subscription gate is released, and this pass holds its place for the same span
                    if (!TryBeginRequest(item))
                    {
                        _serviceLog?.Verbose("Skipping subscription poll for {name} [{id}], a certificate request is already in progress for it.", item.Name, item.Id);
                        continue;
                    }

                    try
                    {
                        await ProcessSubscriptionPoll(item, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _serviceLog?.Error(ex, "External certificate processing failed for {name} [{id}]", item.Name, item.Id);
                    }
                    finally
                    {
                        _renewalsInProgress.TryRemove(item.Id, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "RunSubscriptionPass: unhandled exception while processing external certificate subscriptions");
            }
        }

        /// <summary>
        /// Perform a scheduled check of an external certificate subscription, deploying any available update and
        /// performing the applicable post-request deployment tasks. This uses the same task trigger evaluation as a
        /// renewal driven request, so the outcome does not depend on which scheduled process picked up the item first.
        /// Only called from <see cref="PerformSubscriptionTasks"/>, which already holds the subscription processing gate
        /// (<see cref="_isSubscriptionTaskRunning"/>), so it does not take it again, and which holds the item's place
        /// in <see cref="_renewalsInProgress"/> for the duration so no other request runs against the item meanwhile
        /// </summary>
        /// <param name="item"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ProcessSubscriptionPoll(ManagedCertificate item, CancellationToken cancellationToken)
        {
            // preserve the failure count from before the request, since the request itself may reset it
            var currentFailureCount = item.RenewalFailureCount;

            var result = await PerformSubscriptionRequest(item, progress: null, SubscriptionRequestMode.Automatic, cancellationToken);

            // the pass has no progress tracker of its own, the outcome is broadcast to connected UI clients regardless
            await PerformPostRequestTasksIfApplicable(log: null, result.ManagedItem ?? item, result, skipTasks: false, currentFailureCount, isFinalRequestStage: true, progress: null);
        }

        /// <summary>
        /// Fetch and deploy an updated certificate for a certificate subscription item.
        /// Post-request deployment tasks are not performed here; the caller runs them once the overall request
        /// outcome is known so that status based task triggers are evaluated once, using the applicable evaluation mode.
        /// </summary>
        /// <param name="candidate"></param>
        /// <param name="requestMode"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<SubscriptionProcessResult> ProcessSubscription(ManagedCertificate candidate, SubscriptionRequestMode requestMode, CancellationToken cancellationToken)
        {
            // a precondition failure means nothing was attempted against the source, so the request is deferred rather
            // than failed - firing ON_ERROR deployment tasks here would raise an alert for a request we never made
            if (string.IsNullOrWhiteSpace(candidate.Id))
            {
                return new SubscriptionProcessResult("Managed cert ID is missing", SubscriptionRequestOutcome.Deferred);
            }

            var item = await _itemManager.GetById(candidate.Id);
            if (item?.ExternalSource == null)
            {
                return new SubscriptionProcessResult("Managed cert external source not configured", SubscriptionRequestOutcome.Deferred);
            }

            var sourceConfig = item.ExternalSource;
            var hasPendingSourceUpdate = HasPendingSubscriptionUpdate(sourceConfig);

            var canFetchFromSource = hasPendingSourceUpdate || IsPullModeEnabled(sourceConfig);
            if (!canFetchFromSource)
            {
                if (requestMode == SubscriptionRequestMode.Manual)
                {
                    // a push-only source has nothing for us to pull on demand, so nothing was attempted
                    return new SubscriptionProcessResult($"Manual request is not currently supported for external source type '{sourceConfig.SourceType}'.", SubscriptionRequestOutcome.Deferred);
                }

                return new SubscriptionProcessResult("External certificate source is not configured for polling and has no pending certificate update.", SubscriptionRequestOutcome.Deferred);
            }

            var shouldFetchFromSource = requestMode == SubscriptionRequestMode.Manual
                || hasPendingSourceUpdate
                || ShouldPollSource(item, sourceConfig);

            if (!shouldFetchFromSource)
            {
                return new SubscriptionProcessResult("External certificate source is not due to be checked and has no pending certificate update.", SubscriptionRequestOutcome.Deferred);
            }

            // a manual request is an explicit user override of automatic renewal scheduling, so it fetches and deploys
            // immediately regardless of any configured maintenance window
            if (requestMode != SubscriptionRequestMode.Manual)
            {
                var maintenanceWindowCheck = GetMaintenanceWindowStatus(item);
                if (!maintenanceWindowCheck.IsWithinWindow)
                {
                    return DeferSubscriptionForMaintenanceWindow(item, maintenanceWindowCheck.Reason);
                }

                // the window is open, so a later wait for it is logged again
                _subscriptionsAwaitingMaintenanceWindow.TryRemove(item.Id, out _);
            }

            LogSubscriptionStart(item, sourceConfig, requestMode);

            // stamped before the fetch rather than after it, so a fetch which fails in a way that leaves this method
            // early still counts as an attempt. Otherwise the poll interval never advances and the source is contacted
            // on every pass instead of on its own schedule
            sourceConfig.DateLastPoll = DateTimeOffset.UtcNow;

            // preserved before anything is recorded, so however many times this request records a failure the count
            // only advances by one
            var currentFailureCount = item.RenewalFailureCount;

            // set once the source has supplied a certificate, so a failure after that point is recorded against
            // deploying it rather than against the source
            var certificateObtained = false;

            try
            {
                var fetchResult = await FetchExternalCertificateAsset(
                    item,
                    sourceConfig,
                    cancellationToken,
                    ignoreCurrentVersion: requestMode == SubscriptionRequestMode.Manual);

                if (!fetchResult.IsSuccess)
                {
                    var failureMessage = $"External certificate subscription {GetExternalActionNoun(requestMode)} failed: {fetchResult.Message ?? "Failed to retrieve certificate from external source."}";

                    await RecordSubscriptionRequestFailure(item, sourceConfig, failureMessage, currentFailureCount);
                    return new SubscriptionProcessResult(failureMessage, SubscriptionRequestOutcome.Failed);
                }

                if (!fetchResult.HasUpdate || fetchResult.CertificateData == null)
                {
                    return await RecordSubscriptionNoUpdate(item, sourceConfig, fetchResult.SourceVersion, requestMode);
                }

                // a new certificate is about to be stored and deployed, so the outcome of the previous request no longer
                // describes the item
                ClearPrimaryAndBindingRequestStatus(item);

                LogMessage(item.Id, $"External certificate update detected. Source version: {FormatSourceVersion(fetchResult.SourceVersion)}.");

                var assetPath = await StoreExternalCertificateAsset(item, fetchResult.CertificateData);
                if (assetPath == null)
                {
                    var failureMessage = "External certificate update was detected but could not be written to local storage.";

                    await RecordSubscriptionRequestFailure(item, sourceConfig, failureMessage, currentFailureCount);
                    return new SubscriptionProcessResult(failureMessage, SubscriptionRequestOutcome.Failed);
                }

                var validationResult = await ValidateExternalCertificateAsset(item, sourceConfig, assetPath);
                if (!validationResult.IsValid)
                {
                    var failureMessage = $"External certificate update rejected: {validationResult.Message ?? "External certificate update failed validation."}";

                    await RecordSubscriptionRequestFailure(item, sourceConfig, failureMessage, currentFailureCount);
                    return new SubscriptionProcessResult(failureMessage, SubscriptionRequestOutcome.Failed);
                }

                LogMessage(item.Id, $"External certificate asset validated. Thumbprint: {validationResult.Thumbprint}; valid until {validationResult.DateExpiry:u}; lifetime elapsed: {validationResult.PercentageElapsed}%.");
                SetPrimaryRequestStatus(item, null, RequestState.Success, "External certificate pulled from Management Hub.");
                certificateObtained = true;

                var deploymentResult = await DeployExternalCertificateAsset(item, sourceConfig, assetPath, fetchResult.SourceVersion, requestMode == SubscriptionRequestMode.Manual ? "Manual external subscription request" : "External source update", currentFailureCount);

                if (deploymentResult.IsSuccess && requestMode == SubscriptionRequestMode.Manual)
                {
                    return new SubscriptionProcessResult("External certificate pulled from Management Hub and deployment completed.", SubscriptionRequestOutcome.Completed);
                }

                return new SubscriptionProcessResult(
                    deploymentResult.Message,
                    deploymentResult.IsSuccess ? SubscriptionRequestOutcome.Completed : SubscriptionRequestOutcome.Failed);
            }
            catch (Exception exp)
            {
                // fetching, storing, parsing or deploying the certificate can all throw. The failure is recorded here,
                // against the copy of the item this request has been working on, so it is paced and reported like any
                // other failure instead of being repeated on every pass and written over a stale copy
                _tc?.TrackException(exp);
                _serviceLog?.Error(exp, "External certificate request failed for {name} [{id}]", item.Name, item.Id);

                var failureMessage = $"External certificate request failed: {exp.Message}";

                if (certificateObtained)
                {
                    await RecordSubscriptionDeploymentFailure(item, sourceConfig, failureMessage, currentFailureCount);
                }
                else
                {
                    await RecordSubscriptionRequestFailure(item, sourceConfig, failureMessage, currentFailureCount);
                }

                return new SubscriptionProcessResult(failureMessage, SubscriptionRequestOutcome.Failed);
            }
        }


        /// <summary>
        /// Record a failed attempt to obtain an update from the subscription source: the source could not be reached or
        /// read, or what it supplied could not be stored or validated. Recorded as a failed primary request which
        /// replaces the outcome of the previous request; the item's failure count then paces the next attempt
        /// </summary>
        /// <param name="item"></param>
        /// <param name="sourceConfig"></param>
        /// <param name="message"></param>
        /// <param name="failureCount">the count held before the request began, see <see cref="IncrementManagedCertificateRenewalFailureCount"/></param>
        private async Task RecordSubscriptionRequestFailure(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string message, int? failureCount)
        {
            LogMessage(item.Id, message, LogItemType.GeneralError);

            SetSubscriptionSourceFailure(item, sourceConfig, message);

            await RecordPrimaryRequestFailure(item, message, failureCount);
        }

        /// <summary>
        /// Record a source failure against the item's primary request stage. Only that stage is replaced: the item may
        /// still hold a certificate the source supplied earlier which was not fully deployed, and that deployment
        /// failure is left in place so the item is redeployed once the source answers again. Clearing it here would
        /// leave the item looking healthy as soon as the source failure resolved, with the certificate never installed
        /// </summary>
        /// <param name="item"></param>
        /// <param name="sourceConfig"></param>
        /// <param name="message"></param>
        internal static void SetSubscriptionSourceFailure(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string message)
        {
            sourceConfig.LastError = message;

            SetPrimaryRequestStatus(item, null, RequestState.Error, message);
        }

        /// <summary>
        /// Record a failure to apply or deploy a certificate the source has supplied. The certificate was obtained, so
        /// this is recorded against the deployment stage rather than the request: the item stays eligible for the
        /// deployment retry pass, and a later check which finds nothing newer at the source leaves the failure in place
        /// </summary>
        /// <param name="item"></param>
        /// <param name="sourceConfig"></param>
        /// <param name="message"></param>
        /// <param name="failureCount">the count held before the request began, see <see cref="IncrementManagedCertificateRenewalFailureCount"/></param>
        private async Task RecordSubscriptionDeploymentFailure(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string message, int? failureCount)
        {
            sourceConfig.LastError = message;

            LogMessage(item.Id, message, LogItemType.CertificateRequestAttentionRequired);

            SetBindingDeploymentStatus(item, RequestState.Error, message);

            await RecordDeploymentFailure(item, message, failureCount);
        }

        /// <summary>
        /// Record a check which found the source has nothing newer than the certificate the item already holds.
        /// The source answered, so attempts against it are no longer failing and any pending update notification is
        /// satisfied. Nothing was deployed, so the item's own status is left describing what last happened to it: a
        /// certificate which was fetched but not fully deployed stays recorded that way, with the failure count which
        /// paces its deployment retries intact. The one thing this check does resolve is an earlier failure to reach
        /// or read the source
        /// </summary>
        /// <param name="item"></param>
        /// <param name="sourceConfig"></param>
        /// <param name="sourceVersion"></param>
        /// <param name="requestMode"></param>
        /// <returns></returns>
        private async Task<SubscriptionProcessResult> RecordSubscriptionNoUpdate(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string? sourceVersion, SubscriptionRequestMode requestMode)
        {
            const string message = "No updated certificate was available from Management Hub.";

            ClearSubscriptionRenewalTrigger(item, sourceConfig, clearPendingSourceVersion: true);
            sourceConfig.LastError = null;

            LogMessage(item.Id, $"External certificate subscription {GetExternalActionNoun(requestMode)} completed with no update. Source version: {FormatSourceVersion(sourceVersion)}.");

            if (HasRecordedSourceFailure(item))
            {
                // the source answered and the item already holds its current certificate, so the recorded failure to
                // reach or read it is resolved. The overall status is then recomputed from every recorded stage rather
                // than set to success, because nothing was deployed: a failure to deploy the certificate the item holds,
                // or a failed deployment task, stands until a deployment succeeds, and the failure count which paces the
                // retry of that deployment stays as it is
                SetPrimaryRequestStatus(item, null, RequestState.Success, message);
                await StoreRecomputedRenewalStatus(item);
            }
            else
            {
                await UpdateManagedCertificate(item);
            }

            // an automatic check simply tries again later. A manual request still performs its deployment tasks,
            // deploying the certificate we already hold
            var outcome = requestMode == SubscriptionRequestMode.Automatic
                ? SubscriptionRequestOutcome.Deferred
                : SubscriptionRequestOutcome.Completed;

            return new SubscriptionProcessResult(message, outcome);
        }

        /// <summary>
        /// Record that the source has a version of the certificate which the item does not yet hold, so the next pass
        /// fetches and deploys it. Returns false if that version is already held or is already recorded as pending.
        /// A version which is genuinely new is new work rather than a repeat of whatever has been failing, so the failure
        /// count which paces retries is cleared: the update is attempted on the next pass even if the item was backing
        /// off after repeated failures, which matters most for the short lifetime certificates push mode exists for.
        /// Should the new version fail in the same way, the count climbs again and the back off resumes within a few passes
        /// </summary>
        /// <param name="item"></param>
        /// <param name="sourceConfig"></param>
        /// <param name="sourceVersion">the version announced by the source, or null if it did not say</param>
        /// <returns>whether a new pending update was recorded</returns>
        internal static bool TryRecordPendingSubscriptionUpdate(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string? sourceVersion)
        {
            var pendingVersion = string.IsNullOrWhiteSpace(sourceVersion)
                ? DateTimeOffset.UtcNow.UtcTicks.ToString()
                : sourceVersion;

            if (string.Equals(sourceConfig.LastSourceVersion, pendingVersion, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sourceConfig.PendingSourceVersion, pendingVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            sourceConfig.PendingSourceVersion = pendingVersion;
            sourceConfig.LastError = null;
            item.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow;
            item.RenewalFailureCount = 0;

            return true;
        }

        private async Task<ActionResult> MarkSubscriptionUpdateAvailable(string managedCertificateId, string? sourceVersion)
        {
            if (string.IsNullOrWhiteSpace(managedCertificateId))
            {
                return new ActionResult("Managed cert ID is missing", false);
            }

            var item = await _itemManager.GetById(managedCertificateId);
            if (item?.ExternalSource == null)
            {
                return new ActionResult("Managed cert external source not configured", false);
            }

            var sourceConfig = item.ExternalSource;
            if (!IsPushModeEnabled(sourceConfig))
            {
                return new ActionResult("Source push mode not enabled", false);
            }

            if (!TryRecordPendingSubscriptionUpdate(item, sourceConfig, sourceVersion))
            {
                return new ActionResult("External managed certificate update already recorded.", true);
            }

            await UpdateManagedCertificate(item);

            // the point of being told about an update is that it is applied when we hear about it, rather than up to a
            // full scheduled interval later, which matters most for the short lifetime certificates push mode exists for
            RequestSubscriptionPass();

            return new ActionResult("External managed certificate update is available.", true);
        }

        /// <summary>
        /// Resolve the overall state of an external (subscription) certificate request from its outcome. The stored
        /// status is only ever consulted to recover the specific failure recorded during this request - it can never
        /// promote a request to success, so a failed request cannot inherit a success left over from a previous run
        /// </summary>
        /// <param name="outcome"></param>
        /// <param name="storedRenewalStatus"></param>
        /// <returns></returns>
        internal static RequestState ResolveSubscriptionRequestState(SubscriptionRequestOutcome outcome, RequestState? storedRenewalStatus)
        {
            if (outcome == SubscriptionRequestOutcome.Completed)
            {
                return RequestState.Success;
            }

            var recordedFailure = storedRenewalStatus.HasValue && storedRenewalStatus != RequestState.Success
                ? storedRenewalStatus
                : null;

            // a deferred request did not attempt anything, so it reports the item's existing state rather than a new
            // problem; a failed request always reports a problem even when nothing specific was recorded
            return recordedFailure ?? (outcome == SubscriptionRequestOutcome.Failed ? RequestState.Warning : RequestState.Success);
        }

        /// <summary>
        /// Determine whether the outcome of an external (subscription) certificate request is worth reporting as request
        /// progress. Progress is always broadcast to connected UI clients (the app and the hub) regardless of whether the
        /// caller supplied a progress tracker, so a deferred automatic request - which attempted nothing against the
        /// source and left the item unchanged - would only add a no-op entry to the request progress they display.
        /// A manual request always reports back, the user is waiting on the outcome of the request they started
        /// </summary>
        /// <param name="requestMode"></param>
        /// <param name="outcome"></param>
        /// <returns></returns>
        internal static bool ShouldReportSubscriptionRequestProgress(SubscriptionRequestMode requestMode, SubscriptionRequestOutcome outcome)
        {
            return requestMode != SubscriptionRequestMode.Automatic || outcome != SubscriptionRequestOutcome.Deferred;
        }

        private (bool IsWithinWindow, string Reason) GetMaintenanceWindowStatus(ManagedCertificate item)
        {
            return RenewalScheduleCalculator.IsWithinMaintenanceWindow(item, GetRenewalPrefs());
        }

        /// <summary>
        /// Defer a subscription update which cannot be applied yet because the item is outside its maintenance window.
        /// Waiting for a window is normal operation rather than a failure, so this deliberately leaves the item exactly
        /// as it is: recording a warning against it would report a problem the operator cannot act on, and would
        /// overwrite the recorded status of the deployment which actually took place. The renewal plan already reports
        /// the deferral and its reason to the UI.
        /// Nothing is stored and the source is not polled, so the wait is re-evaluated on every subscription pass until
        /// the window opens. The item log therefore records the start of the wait rather than one entry per pass
        /// </summary>
        /// <param name="item"></param>
        /// <param name="windowReason">why the item is currently outside its window, and when the window next opens</param>
        /// <returns></returns>
        internal SubscriptionProcessResult DeferSubscriptionForMaintenanceWindow(ManagedCertificate item, string windowReason)
        {
            if (_subscriptionsAwaitingMaintenanceWindow.TryAdd(item.Id, DateTimeOffset.UtcNow))
            {
                LogMessage(item.Id, $"Deferred external certificate fetch and deployment - {windowReason}");
            }

            return new SubscriptionProcessResult($"External certificate update deferred: {windowReason}", SubscriptionRequestOutcome.Deferred);
        }

        /// <summary>
        /// Whether the given item is currently recorded as waiting for its maintenance window
        /// </summary>
        internal bool IsSubscriptionAwaitingMaintenanceWindow(string managedCertificateId)
        {
            return _subscriptionsAwaitingMaintenanceWindow.ContainsKey(managedCertificateId);
        }

        private static bool IsPullModeEnabled(ExternalCertificateSubscription sourceConfig)
        {
            var mode = sourceConfig.RetrievalMode ?? ExternalCertificateRetrievalModes.Pull;
            return mode.Equals(ExternalCertificateRetrievalModes.Pull, StringComparison.OrdinalIgnoreCase)
                   || mode.Equals(ExternalCertificateRetrievalModes.Auto, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPushModeEnabled(ExternalCertificateSubscription sourceConfig)
        {
            var mode = sourceConfig.RetrievalMode ?? ExternalCertificateRetrievalModes.Pull;
            return mode.Equals(ExternalCertificateRetrievalModes.Push, StringComparison.OrdinalIgnoreCase)
                   || mode.Equals(ExternalCertificateRetrievalModes.Auto, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determine whether the subscription source has notified us of a certificate update which has not yet been applied
        /// </summary>
        /// <param name="sourceConfig"></param>
        /// <returns></returns>
        internal static bool HasPendingSubscriptionUpdate(ExternalCertificateSubscription? sourceConfig)
        {
            return !string.IsNullOrWhiteSpace(sourceConfig?.PendingSourceVersion);
        }

        /// <summary>
        /// Whether a subscription item has a recorded failure to reach or read its source. For a subscription the
        /// primary request stage is the attempt to obtain the certificate from the source, so this is simply whether
        /// that stage is recorded as failed. It is deliberately independent of the deployment stage and the deployment
        /// tasks: an item can carry both a source failure and an older deployment failure, and a check which reaches the
        /// source resolves the former whatever the state of the latter, which only a successful deployment resolves
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        internal static bool HasRecordedSourceFailure(ManagedCertificate item)
        {
            return item.LastPrimaryRequest?.Status == RequestState.Error;
        }

        /// <summary>
        /// Determine whether a subscription should be processed now. A subscription with no source configuration
        /// cannot be processed, so callers may pass the item's external source without checking it first
        /// </summary>
        /// <param name="item"></param>
        /// <param name="sourceConfig"></param>
        /// <param name="checkDate"></param>
        /// <returns></returns>
        internal static bool ShouldProcessSubscription(ManagedCertificate item, ExternalCertificateSubscription? sourceConfig, DateTimeOffset? checkDate = null)
        {
            if (sourceConfig == null)
            {
                return false;
            }

            // an attempt which keeps failing is spaced out by the same hold as any other attempt for the item, whether
            // it is fetching an update the source has announced or polling the source on its own interval. Without the
            // hold the source would be contacted on every pass for as long as the problem lasted
            if (ManagedCertificate.IsHeldByFailureBackoff(item, checkDate))
            {
                return false;
            }

            return HasPendingSubscriptionUpdate(sourceConfig) || ShouldPollSource(item, sourceConfig, checkDate);
        }

        internal static bool ShouldPollSource(ManagedCertificate item, ExternalCertificateSubscription? sourceConfig, DateTimeOffset? checkDate = null)
        {
            if (sourceConfig == null || !IsPullModeEnabled(sourceConfig))
            {
                return false;
            }

            var now = checkDate ?? DateTimeOffset.UtcNow;

            if (ShouldRequireRenewalDueForSourcePolling(sourceConfig)
                && !IsAutomaticSubscriptionRetryDue(item, now))
            {
                return false;
            }

            var pollIntervalMinutes = sourceConfig.PollIntervalMinutes <= 0 ? 30 : sourceConfig.PollIntervalMinutes;

            if (!sourceConfig.DateLastPoll.HasValue)
            {
                return true;
            }

            return sourceConfig.DateLastPoll.Value <= now.AddMinutes(-pollIntervalMinutes);
        }

        private static bool ShouldRequireRenewalDueForSourcePolling(ExternalCertificateSubscription sourceConfig)
        {
            // a pull only subscription is never notified of an update by its source, so it has to poll on its own
            // interval - otherwise it would only ever pick up an update when its own renewal happened to fall due
            if (!IsPushModeEnabled(sourceConfig))
            {
                return false;
            }

            // Management Hub subscriptions which can also receive push update notifications are told when an update is
            // available, so for them polling is only used as fallback when normal renewal scheduling is due.
            return string.IsNullOrWhiteSpace(sourceConfig.SourceType)
                || sourceConfig.SourceType.Equals(ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether an automatic attempt against the subscription source is due under renewal scheduling: renewal is
        /// due and is not held by the failure back off. A redeployment of the certificate already held is not an
        /// attempt against the source, so an item which is due only for that is not due to fetch or poll - the renewal
        /// pass redeploys it, and polling it as well would put both passes on the same item at once
        /// </summary>
        /// <param name="item"></param>
        /// <param name="checkDate"></param>
        /// <returns></returns>
        internal static bool IsAutomaticSubscriptionRetryDue(ManagedCertificate item, DateTimeOffset? checkDate = null)
        {
            var now = checkDate ?? DateTimeOffset.UtcNow;
            var renewalIntervalMode = CoreAppSettings.Current.RenewalIntervalMode ?? RenewalIntervalModes.DaysAfterLastRenewal;
            var renewalCheck = ManagedCertificate.CalculateNextRenewalAttempt(item, CoreAppSettings.Current.RenewalIntervalDays, renewalIntervalMode, testDateTime: now);

            return renewalCheck?.IsRenewalDue == true && !renewalCheck.IsRenewalOnHold && !renewalCheck.IsRedeployOnly;
        }

        internal static void ClearSubscriptionRenewalTrigger(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, bool clearPendingSourceVersion)
        {
            item.DateNextScheduledRenewalAttempt = null;

            if (clearPendingSourceVersion)
            {
                sourceConfig.PendingSourceVersion = null;
            }
        }

        private async Task<List<StatusMessage>> TestSubscriptionAccess(Certify.Models.Providers.ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState>? progress = null)
        {
            var results = new List<StatusMessage>();

            var sourceConfig = managedCertificate.ExternalSource;
            if (sourceConfig == null)
            {
                results.Add(new StatusMessage
                {
                    IsOK = false,
                    Message = "External subscription is not configured for this managed certificate."
                });

                ReportProgress(progress, new RequestProgressState(RequestState.Error, "External subscription test failed", managedCertificate, isPreviewMode: true));
                return results;
            }

            log?.Information("Testing download access for external certificate subscription {managedItem}", managedCertificate);

            var fetchResult = await FetchExternalCertificateAsset(managedCertificate, sourceConfig, CancellationToken.None, ignoreCurrentVersion: true);

            if (!fetchResult.IsSuccess)
            {
                results.Add(new StatusMessage
                {
                    IsOK = false,
                    Message = fetchResult.Message ?? "Failed to access the configured external certificate source."
                });

                ReportProgress(progress, new RequestProgressState(RequestState.Error, "External subscription test failed", managedCertificate, isPreviewMode: true));
                return results;
            }

            var remoteItem = sourceConfig.SourceItemName.AsNullWhenBlank() ?? sourceConfig.ExternalReference.AsNullWhenBlank() ?? "selected certificate";
            var sourceType = sourceConfig.SourceType.AsNullWhenBlank() ?? "external source";

            results.Add(new StatusMessage
            {
                IsOK = true,
                Message = $"Verified download access to '{remoteItem}' via {sourceType}. No certificate changes were applied."
            });

            if (!string.IsNullOrWhiteSpace(fetchResult.SourceVersion))
            {
                results.Add(new StatusMessage
                {
                    IsOK = true,
                    Message = $"Source version: {fetchResult.SourceVersion}"
                });
            }

            if (fetchResult.CertificateData?.Length > 0)
            {
                results.Add(new StatusMessage
                {
                    IsOK = true,
                    Message = $"Downloaded {fetchResult.CertificateData.Length} bytes for validation only."
                });
            }

            ReportProgress(progress, new RequestProgressState(RequestState.Success, "External subscription test completed", managedCertificate, isPreviewMode: true));

            return results;
        }

        private async Task<ExternalCertificateFetchResult> FetchExternalCertificateAsset(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, CancellationToken cancellationToken, bool ignoreCurrentVersion = false)
        {
            var sourceType = sourceConfig.SourceType?.Trim() ?? string.Empty;

            if (sourceType.Equals(ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase))
            {
                return await FetchFromManagementHub(item, sourceConfig, cancellationToken, ignoreCurrentVersion);
            }

            return new ExternalCertificateFetchResult
            {
                IsSuccess = false,
                Message = $"Unsupported external certificate source type: {sourceType}"
            };
        }

        private async Task<ExternalCertificateFetchResult> FetchFromManagementHub(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, CancellationToken cancellationToken, bool ignoreCurrentVersion = false)
        {
            if (!ManagedCertificate.TryParseManagementHubReference(sourceConfig.ExternalReference, out var sourceInstanceId, out var sourceManagedCertificateId))
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = "Management Hub source certificate must be in format '{instanceId}/{managedCertId}'."
                };
            }

            var hubApiBase = sourceConfig.SourceConnection?.Trim().TrimEnd('/')
                             ?? _serverConfig?.ManagementServerHubAPI?.Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(hubApiBase))
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = "ManagementHub source connection is not configured."
                };
            }

            var useHubJoiningCredentials = string.IsNullOrWhiteSpace(sourceConfig.CredentialKey);
            var secret = await GetHubClientSecret(sourceConfig.CredentialKey);
            if (secret == null || string.IsNullOrWhiteSpace(secret.ClientId) || string.IsNullOrWhiteSpace(secret.Secret))
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = "ManagementHub source credentials are not configured or could not be unlocked."
                };
            }

            var requestContext = await CreateManagementHubRequestContext(
                hubApiBase,
                secret,
                useHubJoiningCredentials,
                ignoreCurrentVersion ? null : sourceConfig.LastSourceVersion);

            try
            {
                using var response = await UseHubApiClient(
                    hubApiBase,
                    requestContext,
                    (client, ct) => client.DownloadAsync(sourceInstanceId, sourceManagedCertificateId, "pfx", ct),
                    cancellationToken);

                return ResolveFetchedCertificate(
                    await ReadHubApiFileResponse(response, cancellationToken),
                    GetHubApiHeaderValue(response, "ETag"),
                    sourceConfig.LastSourceVersion,
                    ignoreCurrentVersion);
            }
            catch (ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotModified)
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = true,
                    HasUpdate = false,
                    SourceVersion = sourceConfig.LastSourceVersion
                };
            }
            catch (ApiException ex)
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = $"ManagementHub source returned {ex.StatusCode}: {ex.Response}"
                };
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = $"Could not connection to management hub to fetch updated certificate ({hubApiBase}) check hub is available and connectivity is allowed."
                };
            }
            catch (Exception ex)
            {
                // anything else the fetch can throw is still a failed fetch and has to be reported as one. A request
                // timeout surfaces as TaskCanceledException rather than HttpRequestException, and reading the response
                // or resolving credentials can fail in their own ways - none of which the caller can distinguish from
                // success unless they are turned into a result here
                _serviceLog?.Error(ex, "Unexpected error fetching external certificate for {name} [{id}] from {hubApiBase}", item.Name, item.Id, hubApiBase);

                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = $"Unexpected error retrieving certificate from management hub ({hubApiBase}): {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Decide whether what the source returned is a certificate the item does not already hold. The version marker
        /// is the source's own if it supplied one, otherwise a digest of the payload, so a source which does not
        /// version its certificates still only reports an update when the certificate itself changes.
        /// This is the whole of the update decision, deliberately kept apart from fetching: what the source returned is
        /// judged the same way however it was obtained, and a change here cannot depend on the transport
        /// </summary>
        /// <param name="certificateData">the payload the source returned</param>
        /// <param name="sourceETag">the version the source declared, if any</param>
        /// <param name="lastSourceVersion">the version the item last deployed</param>
        /// <param name="ignoreCurrentVersion">true for a manual request or an access test, which fetch regardless of the version held</param>
        /// <returns></returns>
        internal static ExternalCertificateFetchResult ResolveFetchedCertificate(
            byte[] certificateData,
            string? sourceETag,
            string? lastSourceVersion,
            bool ignoreCurrentVersion)
        {
            if (certificateData == null || certificateData.Length == 0)
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = "ManagementHub source returned an empty certificate payload."
                };
            }

            // a source which declares a blank version has effectively declared none, so it falls back to the digest
            // rather than recording an empty marker the next check would compare against
            var sourceVersion = sourceETag.AsNullWhenBlank()
                ?? Convert.ToHexString(SHA256.HashData(certificateData)).ToLowerInvariant();

            // hub versions are ETags, whose case is not significant
            var alreadyHeld = !ignoreCurrentVersion
                && !string.IsNullOrWhiteSpace(lastSourceVersion)
                && string.Equals(lastSourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase);

            return new ExternalCertificateFetchResult
            {
                IsSuccess = true,
                HasUpdate = !alreadyHeld,
                SourceVersion = sourceVersion,
                CertificateData = certificateData
            };
        }

        private async Task<ClientSecret?> GetHubClientSecret(string? sourceCredentialKey)
        {
            if (!string.IsNullOrWhiteSpace(sourceCredentialKey))
            {
                var credentials = await _credentialsManager.GetUnlockedCredentialsDictionary(sourceCredentialKey);
                if (credentials != null)
                {
                    var sourceClientId = GetCredentialValue(credentials, "clientid", "client_id");
                    var sourceSecret = GetCredentialValue(credentials, "secret", "client_secret", "password");

                    if (!string.IsNullOrWhiteSpace(sourceClientId) && !string.IsNullOrWhiteSpace(sourceSecret))
                    {
                        return new ClientSecret
                        {
                            ClientId = sourceClientId,
                            Secret = sourceSecret
                        };
                    }
                }
            }

            if (_mgmtHubJoiningSecret != null)
            {
                return _mgmtHubJoiningSecret;
            }

            try
            {
                var secret = await _credentialsManager.GetUnlockedCredential(Certify.Models.Hub.HubSharedConstants.MgmtHubJoiningCredId);
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    return JsonSerializer.Deserialize<ClientSecret>(secret, JsonOptions.DefaultJsonSerializerOptions);
                }
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "Failed to resolve Management Hub joining secret for external source retrieval.");
            }

            return null;
        }

        private static string? GetCredentialValue(Dictionary<string, string> credentials, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (credentials.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string GetExternalSourceLogDescription(ExternalCertificateSubscription sourceConfig)
        {
            var sourceType = sourceConfig.SourceType.AsNullWhenBlank() ?? "external source";
            var sourceItem = sourceConfig.SourceItemName.AsNullWhenBlank() ?? sourceConfig.ExternalReference.AsNullWhenBlank() ?? "unselected source item";

            return $"{sourceType} item '{sourceItem}'";
        }

        private static string GetExternalActionNoun(SubscriptionRequestMode requestMode)
        {
            return requestMode == SubscriptionRequestMode.Manual ? "request" : "pull";
        }

        private void LogSubscriptionStart(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, SubscriptionRequestMode requestMode)
        {
            if (requestMode == SubscriptionRequestMode.Manual)
            {
                LogMessage(item.Id, $"Starting manual external certificate subscription request for {GetExternalSourceLogDescription(sourceConfig)}. Last source version: {FormatSourceVersion(sourceConfig.LastSourceVersion)}.");

                if (string.Equals(sourceConfig.SourceType, ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase)
                    && ManagedCertificate.TryParseManagementHubReference(sourceConfig.ExternalReference, out var sourceInstanceId, out var sourceManagedCertificateId))
                {
                    LogMessage(item.Id, $"Fetching external certificate asset from Management Hub instance '{sourceInstanceId}', managed certificate '{sourceManagedCertificateId}'.");
                }

                return;
            }

            LogMessage(item.Id, $"Starting external certificate subscription pull from {GetExternalSourceLogDescription(sourceConfig)}. Last source version: {FormatSourceVersion(sourceConfig.LastSourceVersion)}.");
        }

        private static string FormatSourceVersion(string? sourceVersion)
        {
            return string.IsNullOrWhiteSpace(sourceVersion) ? "none" : sourceVersion;
        }

        /// <summary>
        /// Queries the management hub for the list of managed certificates this instance is permitted to pull.
        /// Uses the same client secret as the hub joining credential.
        /// </summary>
        public async Task<List<ManagedCertificateSummary>> GetHubSubscribableManagedCertificates()
        {
            var hubApiBase = _serverConfig?.ManagementServerHubAPI?.Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(hubApiBase))
            {
                _serviceLog?.Warning("GetHubSubscribableManagedCertificates: hub API base URL is not configured.");
                return new();
            }

            var secret = _mgmtHubJoiningSecret;
            if (secret == null || string.IsNullOrWhiteSpace(secret.ClientId) || string.IsNullOrWhiteSpace(secret.Secret))
            {
                _serviceLog?.Warning("GetHubSubscribableManagedCertificates: hub joining credentials are not available.");
                return new();
            }

            var hubAssignedInstanceId = _serverConfig?.HubAssignedInstanceId;
            var requestAuthSecretCached = !string.IsNullOrWhiteSpace(await GetManagementHubRequestAuthSecret());

            _serviceLog?.Debug(
                "GetHubSubscribableManagedCertificates: starting query against {hubApiBase} using managed instance security principal {hubAssignedInstanceId}. Hub joining credential client id configured: {hasClientId}; request auth secret cached: {requestAuthSecretCached}.",
                hubApiBase,
                string.IsNullOrWhiteSpace(hubAssignedInstanceId) ? "<none>" : hubAssignedInstanceId,
                !string.IsNullOrWhiteSpace(secret.ClientId),
                requestAuthSecretCached);

            try
            {
                var requestContext = await CreateManagementHubRequestContext(
                    hubApiBase,
                    secret,
                    useHubJoiningCredentials: true,
                    ifNoneMatch: null);

                var results = await UseHubApiClient(
                    hubApiBase,
                    requestContext,
                    async (client, ct) => (await client.GetSubscribableManagedCertificatesAsync(ct)).ToList(),
                    CancellationToken.None);

                _serviceLog?.Debug(
                    "GetHubSubscribableManagedCertificates: query succeeded for managed instance security principal {hubAssignedInstanceId}; returned {count} subscribable certificates.",
                    string.IsNullOrWhiteSpace(hubAssignedInstanceId) ? "<none>" : hubAssignedInstanceId,
                    results.Count);

                return results;
            }
            catch (ApiException ex)
            {
                _serviceLog?.Warning(
                    "GetHubSubscribableManagedCertificates: hub returned {status} for managed instance security principal {hubAssignedInstanceId} against {hubApiBase}: {detail}",
                    ex.StatusCode,
                    string.IsNullOrWhiteSpace(hubAssignedInstanceId) ? "<none>" : hubAssignedInstanceId,
                    hubApiBase,
                    ex.Response);
                return new();
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex,
                    "GetHubSubscribableManagedCertificates failed for managed instance security principal {hubAssignedInstanceId} against {hubApiBase}: {message}",
                    string.IsNullOrWhiteSpace(hubAssignedInstanceId) ? "<none>" : hubAssignedInstanceId,
                    hubApiBase,
                    ex.Message);
                return new();
            }
        }

        private async Task<string?> StoreExternalCertificateAsset(ManagedCertificate item, byte[] pfxData)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                return null;
            }

            try
            {
                var outputDir = Path.Combine(EnvironmentUtil.EnsuredAppDataPath(), "assets", "external");
                Directory.CreateDirectory(outputDir);

                var outputPath = Path.Combine(outputDir, $"{item.Id}.pfx");
                await File.WriteAllBytesAsync(outputPath, pfxData);

                return outputPath;
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "Failed to write external certificate asset for {name} [{id}]", item.Name, item.Id);
                return null;
            }
        }

        private async Task<HubApiRequestContext> CreateManagementHubRequestContext(string hubApiBase, ClientSecret secret, bool useHubJoiningCredentials, string? ifNoneMatch)
        {
            var hubAssignedInstanceId = useHubJoiningCredentials ? _serverConfig?.HubAssignedInstanceId : null;
            var requestAuthSecret = useHubJoiningCredentials ? await GetManagementHubRequestAuthSecret() : null;

            if (useHubJoiningCredentials
                && !string.IsNullOrWhiteSpace(hubAssignedInstanceId)
                && string.IsNullOrWhiteSpace(requestAuthSecret))
            {
                _serviceLog?.Debug(
                    "CreateManagementHubRequestContext: managed instance security principal {hubAssignedInstanceId} does not have a cached request auth secret. Attempting refresh before calling {hubApiBase}.",
                    hubAssignedInstanceId,
                    hubApiBase);

                try
                {
                    var joinCheck = await CheckManagementHubCredentials(hubApiBase, secret);
                    if (joinCheck.IsSuccess && joinCheck.Result != null)
                    {
                        if (!string.IsNullOrWhiteSpace(joinCheck.Result.HubAssignedInstanceId)
                            && joinCheck.Result.HubAssignedInstanceId != _serverConfig?.HubAssignedInstanceId)
                        {
                            SetHubAssignedInstanceId(joinCheck.Result.HubAssignedInstanceId);
                        }

                        if (!string.IsNullOrWhiteSpace(joinCheck.Result.RequestAuthSecret))
                        {
                            await StoreManagementHubRequestAuthSecret(joinCheck.Result.RequestAuthSecret);
                            requestAuthSecret = joinCheck.Result.RequestAuthSecret;
                            _serviceLog?.Debug(
                                "CreateManagementHubRequestContext: refreshed request auth secret for managed instance security principal {hubAssignedInstanceId} before calling {hubApiBase}.",
                                hubAssignedInstanceId,
                                hubApiBase);
                        }

                        hubAssignedInstanceId = _serverConfig?.HubAssignedInstanceId ?? joinCheck.Result.HubAssignedInstanceId;
                    }
                    else
                    {
                        _serviceLog?.Warning(
                            "CreateManagementHubRequestContext: unable to refresh request auth secret for managed instance security principal {hubAssignedInstanceId} before calling {hubApiBase}: {message}",
                            hubAssignedInstanceId,
                            hubApiBase,
                            joinCheck.Message);
                    }
                }
                catch (Exception ex)
                {
                    _serviceLog?.Warning(
                        "CreateManagementHubRequestContext: failed to refresh request auth secret for managed instance security principal {hubAssignedInstanceId} before calling {hubApiBase}. {exceptionType}: {message}",
                        hubAssignedInstanceId,
                        hubApiBase,
                        ex.GetType().Name,
                        ex.Message);
                }
            }

            return new HubApiRequestContext
            {
                ClientId = secret.ClientId,
                Secret = secret.Secret,
                HubAssignedInstanceId = hubAssignedInstanceId,
                RequestAuthSecret = requestAuthSecret,
                IfNoneMatch = ifNoneMatch
            };
        }

        private async Task<ExternalCertificateValidationResult> ValidateExternalCertificateAsset(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string assetPath)
        {
            try
            {
                var certPwd = await GetPfxPassword(item);
                using var certInfo = CertificateManager.LoadCertificate(assetPath, certPwd, throwOnError: true);

                var dateStart = new DateTimeOffset(certInfo.NotBefore);
                var dateExpiry = new DateTimeOffset(certInfo.NotAfter);
                var lifetime = new Lifetime(dateStart, dateExpiry);
                var percentageElapsed = lifetime.GetPercentageElapsed(DateTimeOffset.UtcNow);

                if (dateExpiry <= DateTimeOffset.UtcNow)
                {
                    return new ExternalCertificateValidationResult
                    {
                        IsValid = false,
                        Message = $"External certificate from Management Hub has expired ({dateExpiry:u}) and will not be deployed.",
                        DateStart = dateStart,
                        DateExpiry = dateExpiry,
                        PercentageElapsed = percentageElapsed,
                        Thumbprint = certInfo.Thumbprint
                    };
                }

                if (percentageElapsed >= LifetimeHealthThresholds.PercentageDanger)
                {
                    return new ExternalCertificateValidationResult
                    {
                        IsValid = false,
                        Message = $"External certificate from Management Hub has exceeded {LifetimeHealthThresholds.PercentageDanger}% of its lifetime and will not be deployed.",
                        DateStart = dateStart,
                        DateExpiry = dateExpiry,
                        PercentageElapsed = percentageElapsed,
                        Thumbprint = certInfo.Thumbprint
                    };
                }

                return new ExternalCertificateValidationResult
                {
                    IsValid = true,
                    DateStart = dateStart,
                    DateExpiry = dateExpiry,
                    PercentageElapsed = percentageElapsed,
                    Thumbprint = certInfo.Thumbprint
                };
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "Failed to validate external certificate asset lifetime for {name} [{id}]", item.Name, item.Id);
                return new ExternalCertificateValidationResult
                {
                    IsValid = false,
                    Message = SubscriptionPfxLoadErrorMessage
                };
            }
        }

        private async Task<ActionResult> DeployExternalCertificateAsset(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string assetPath, string? sourceVersion, string reason, int? currentFailureCount)
        {
            sourceConfig.PendingSourceVersion = sourceVersion ?? sourceConfig.PendingSourceVersion;

            var metadataApplied = await ApplyExternalCertificateMetadata(item, sourceConfig, assetPath);
            if (!metadataApplied)
            {
                await RecordSubscriptionDeploymentFailure(item, sourceConfig, SubscriptionPfxLoadErrorMessage, currentFailureCount);
                return new ActionResult(SubscriptionPfxLoadErrorMessage, false);
            }

            _serviceLog?.Information("Deploying external certificate update for {name} [{id}] - {reason}", item.Name, item.Id, reason);

            // deployment tasks are performed by the caller once the overall request outcome is known, so that status
            // based task triggers are evaluated once using the mode applicable to this request
            var deployResult = await DeployCertificate(item, progress: null, isPreviewOnly: false, includeDeploymentTasks: false);

            if (!deployResult.IsSuccess)
            {
                // the deployment has already recorded its own failure against the item, so the preserved count keeps
                // this from counting the same failure twice
                var failureMessage = $"External certificate deployment failed: {deployResult.Message}";

                await RecordSubscriptionDeploymentFailure(item, sourceConfig, failureMessage, currentFailureCount);
                return new ActionResult(failureMessage, false);
            }
            else
            {
                ClearSubscriptionRenewalTrigger(item, sourceConfig, clearPendingSourceVersion: true);
                sourceConfig.LastSourceVersion = sourceVersion ?? sourceConfig.LastSourceVersion;
                sourceConfig.LastError = null;

                var successMessage = $"External certificate deployment completed successfully. Source version: {FormatSourceVersion(sourceVersion)}.";
                SetBindingDeploymentStatus(item, RequestState.Success, successMessage);
                LogMessage(item.Id, successMessage, LogItemType.CertificateRequestSuccessful);
                await UpdateManagedCertificate(item);
                return new ActionResult(successMessage, true);
            }
        }

        private async Task<bool> ApplyExternalCertificateMetadata(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string assetPath)
        {
            try
            {
                var certPwd = await GetPfxPassword(item);
                var certInfo = CertificateManager.LoadCertificate(assetPath, certPwd, throwOnError: true);

                item.CertificatePath = assetPath;
                item.CertificatePreviousThumbprintHash = item.CertificateThumbprintHash;
                item.CertificateThumbprintHash = certInfo.Thumbprint;
                item.CertificateFriendlyName = certInfo.FriendlyName;
                item.CertificatePEM = null;

                item.DateStart = new DateTimeOffset(certInfo.NotBefore);
                item.DateExpiry = new DateTimeOffset(certInfo.NotAfter);
                item.DateRenewed = DateTimeOffset.UtcNow;
                item.DateRetrieved = DateTimeOffset.UtcNow;
                item.DateLastRenewalAttempt = DateTimeOffset.UtcNow;
                item.CertificateRevoked = false;

                // a new certificate has been retrieved, so any previously scheduled renewal attempt no longer applies
                item.DateNextScheduledRenewalAttempt = null;

                item.ARICertificateId = CertUtils.GetARICertIdBase64(certInfo);

                // Populate domain/IP identifiers from the certificate's SAN extension so that
                // BindingDeploymentManager can match server hostname bindings correctly.
                var certIdentifiers = ExtractIdentifiersFromCertificate(certInfo);
                if (certIdentifiers.Count > 0)
                {
                    item.ApplySourceIdentifiers(certIdentifiers);
                }

                certInfo.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "Failed to validate or parse external certificate asset for {name} [{id}]", item.Name, item.Id);
                return false;
            }
        }

        /// <summary>
        /// Extracts DNS and IP identifiers from an X.509 certificate's Subject Alternative Name
        /// extension, falling back to the Subject CN when no SAN DNS names are present.
        /// </summary>
        private static List<CertIdentifierItem> ExtractIdentifiersFromCertificate(X509Certificate2 cert)
        {
            var identifiers = new List<CertIdentifierItem>();

            var sanExt = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
            if (sanExt != null)
            {
                foreach (var dns in sanExt.EnumerateDnsNames())
                {
                    identifiers.Add(new CertIdentifierItem(CertIdentifierType.Dns, dns));
                }

                foreach (var ip in sanExt.EnumerateIPAddresses())
                {
                    identifiers.Add(new CertIdentifierItem(CertIdentifierType.Ip, ip.ToString()));
                }
            }

            // Fallback: use CN when no SAN DNS names were found
            if (!identifiers.Any(i => i.IdentifierType == CertIdentifierType.Dns))
            {
                var cn = cert.GetNameInfo(X509NameType.DnsName, false);
                if (!string.IsNullOrWhiteSpace(cn))
                {
                    identifiers.Insert(0, new CertIdentifierItem(CertIdentifierType.Dns, cn));
                }
            }

            return identifiers;
        }

        /// <summary>
        /// Perform an automatic (renewal driven) subscription request, waiting for any scheduled subscription poll to
        /// finish first so that a fetch and deployment is not run by both at once.
        /// This gate covers fetch and deployment only - the post-request deployment tasks run after it is released, by
        /// the caller. The poll skips items with a request in progress, which is what keeps the two paths off the same
        /// item for the whole request
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        private async Task<CertificateRequestResult> PerformAutomaticSubscriptionRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress)
        {
            while (Interlocked.CompareExchange(ref _isSubscriptionTaskRunning, 1, 0) != 0)
            {
                await Task.Delay(1000);
            }

            try
            {
                return await PerformSubscriptionRequest(managedCertificate, progress, SubscriptionRequestMode.Automatic);
            }
            finally
            {
                Interlocked.Exchange(ref _isSubscriptionTaskRunning, 0);
            }
        }

        /// <summary>
        /// Perform an external (subscription) certificate request. Post-request deployment tasks are not performed here,
        /// the caller performs them once the overall request outcome is known
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="progress"></param>
        /// <param name="requestMode"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<CertificateRequestResult> PerformSubscriptionRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, SubscriptionRequestMode requestMode, CancellationToken cancellationToken = default)
        {
            var result = new CertificateRequestResult(managedCertificate)
            {
                IsSuccess = false
            };

            var sourceConfig = managedCertificate.ExternalSource;
            ClearPrimaryAndBindingRequestStatus(managedCertificate);
            if (sourceConfig == null)
            {
                // the misconfiguration is surfaced as an error, but nothing was attempted against a source so the
                // request is deferred and no deployment tasks apply
                result.Message = "External subscription is not configured for this managed certificate.";
                result.IsSubscriptionUpdateDeferred = true;
                LogMessage(managedCertificate.Id, result.Message, LogItemType.GeneralError);
                SetPrimaryRequestStatus(managedCertificate, result, RequestState.Error, result.Message);
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            // the not-due and no-pending-update checks are applied by ProcessSubscription, which is the single place
            // deciding whether a fetch is applicable for this request mode. It also records any failure against the
            // stored copy of the item it works on, so nothing is recorded here against the copy the caller supplied
            var processResult = await ProcessSubscription(managedCertificate, requestMode, cancellationToken);

            var updatedManagedCertificate = await _itemManager.GetById(managedCertificate.Id) ?? managedCertificate;

            result.ManagedItem = updatedManagedCertificate;
            result.IsSubscriptionUpdateDeferred = processResult.Outcome == SubscriptionRequestOutcome.Deferred;
            result.Message = processResult.Message;

            var finalState = ResolveSubscriptionRequestState(processResult.Outcome, updatedManagedCertificate.LastRenewalStatus);

            result.PrimaryRequest = new RequestStageStatus { Status = finalState, Message = result.Message };
            result.IsSuccess = finalState == RequestState.Success;

            if (ShouldReportSubscriptionRequestProgress(requestMode, processResult.Outcome))
            {
                ReportProgress(progress, new RequestProgressState(finalState, result.Message, updatedManagedCertificate), logThisEvent: false);
            }

            return result;
        }
    }
}
