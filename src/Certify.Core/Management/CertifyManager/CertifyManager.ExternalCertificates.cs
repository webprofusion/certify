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

        private class ExternalCertificateFetchResult
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
        private class SubscriptionProcessResult
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

        private async Task PerformSubscriptionTasks(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _isSubscriptionTaskRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                if (IsInDegradedMode)
                {
                    return;
                }

                var targetItems = await GetSubscriptionTargets();

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

                    // a renewal driven request for this item holds its place in _renewalsInProgress for the whole
                    // request, including its post-request deployment tasks, which run after the subscription gate is
                    // released. Skipping the item here is what keeps the two paths off the same item, the gate alone
                    // only covers fetch and deployment
                    if (_renewalsInProgress.ContainsKey(item.Id))
                    {
                        _serviceLog?.Verbose("Skipping subscription poll for {name} [{id}], a certificate request is already in progress for it.", item.Name, item.Id);
                        continue;
                    }

                    // the source has no update waiting for us and is not yet due to be polled, so there is nothing
                    // for this pass to do. The item is left untouched rather than run through a request which would
                    // only record a no-op status against it and report that to connected UI clients
                    if (!ShouldProcessSubscription(item, item.ExternalSource))
                    {
                        _serviceLog?.Verbose("Skipping subscription poll for {name} [{id}], the source is not due to be polled and has no pending certificate update.", item.Name, item.Id);
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
                }
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "PerformSubscriptionTasks: unhandled exception while processing external certificate subscriptions");
            }
            finally
            {
                Interlocked.Exchange(ref _isSubscriptionTaskRunning, 0);
            }
        }

        /// <summary>
        /// Perform a scheduled check of an external certificate subscription, deploying any available update and
        /// performing the applicable post-request deployment tasks. This uses the same task trigger evaluation as a
        /// renewal driven request, so the outcome does not depend on which scheduled process picked up the item first.
        /// Only called from <see cref="PerformSubscriptionTasks"/>, which already holds the subscription processing gate
        /// (<see cref="_isSubscriptionTaskRunning"/>), so it does not take it again, and which skips any item with a
        /// certificate request already in progress
        /// </summary>
        /// <param name="item"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ProcessSubscriptionPoll(ManagedCertificate item, CancellationToken cancellationToken)
        {
            // preserve the failure count from before the request, since the request itself may reset it
            var currentFailureCount = item.RenewalFailureCount;

            var result = await PerformSubscriptionRequest(item, progress: null, SubscriptionRequestMode.Automatic, cancellationToken);

            await PerformPostRequestTasksIfApplicable(log: null, result.ManagedItem ?? item, result, skipTasks: false, currentFailureCount, persistTaskState: true);
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
                    sourceConfig.LastError = null;

                    var deferredMessage = $"External certificate update deferred: {maintenanceWindowCheck.Reason}";
                    SetBindingDeploymentStatus(item, RequestState.Warning, deferredMessage);
                    item.LastRenewalStatus = RequestState.Warning;
                    item.RenewalFailureMessage = deferredMessage;

                    LogMessage(item.Id, $"Deferred external certificate fetch and deployment - {maintenanceWindowCheck.Reason}");
                    await UpdateManagedCertificate(item);

                    // deployment has been deliberately deferred until the next maintenance window, so no deployment tasks apply
                    return new SubscriptionProcessResult(deferredMessage, SubscriptionRequestOutcome.Deferred);
                }
            }

            ClearPrimaryAndBindingRequestStatus(item);

            LogSubscriptionStart(item, sourceConfig, requestMode);

            var fetchResult = await FetchExternalCertificateAsset(
                item,
                sourceConfig,
                pushedSourceVersion: hasPendingSourceUpdate ? sourceConfig.PendingSourceVersion : null,
                cancellationToken,
                ignoreCurrentVersion: requestMode == SubscriptionRequestMode.Manual);

            sourceConfig.DateLastPoll = DateTimeOffset.UtcNow;

            if (!fetchResult.IsSuccess)
            {
                sourceConfig.LastError = fetchResult.Message;
                LogMessage(item.Id, $"External certificate subscription {GetExternalActionNoun(requestMode)} failed: {fetchResult.Message}", LogItemType.GeneralError);
                SetPrimaryRequestStatus(item, null, RequestState.Error, fetchResult.Message ?? "Failed to retrieve certificate from external source.");

                await RecordPrimaryRequestFailure(item, fetchResult.Message ?? "Failed to retrieve certificate from external source.");
                return new SubscriptionProcessResult(fetchResult.Message, SubscriptionRequestOutcome.Failed);
            }

            if (!fetchResult.HasUpdate || fetchResult.CertificateData == null)
            {
                var message = "No updated certificate was available from Management Hub.";
                var noUpdateStatus = await RecordSubscriptionNoUpdate(item, message);
                LogMessage(item.Id, $"External certificate subscription {GetExternalActionNoun(requestMode)} completed with no update. Source version: {FormatSourceVersion(fetchResult.SourceVersion)}. Recorded status: {noUpdateStatus}; failure count: {item.RenewalFailureCount}.", noUpdateStatus == RequestState.Error ? LogItemType.GeneralError : LogItemType.CertificateRequestAttentionRequired);
                SetPrimaryRequestStatus(item, null, noUpdateStatus, message);

                ClearSubscriptionRenewalTrigger(item, sourceConfig, clearPendingSourceVersion: true);
                sourceConfig.LastError = noUpdateStatus == RequestState.Error ? message : null;
                await UpdateManagedCertificate(item);

                // an automatic check simply tries again later. A manual request still performs its deployment tasks,
                // deploying the certificate we already hold - unless the subscription is now overdue for an update,
                // which is a genuine failure to report
                var noUpdateOutcome = requestMode == SubscriptionRequestMode.Automatic
                    ? SubscriptionRequestOutcome.Deferred
                    : noUpdateStatus == RequestState.Error
                        ? SubscriptionRequestOutcome.Failed
                        : SubscriptionRequestOutcome.Completed;

                return new SubscriptionProcessResult(message, noUpdateOutcome);
            }

            LogMessage(item.Id, $"External certificate update detected. Source version: {FormatSourceVersion(fetchResult.SourceVersion)}.");

            var assetPath = await StoreExternalCertificateAsset(item, fetchResult.CertificateData);
            if (assetPath == null)
            {
                sourceConfig.LastError = "External certificate update was detected but could not be written to local storage.";
                LogMessage(item.Id, sourceConfig.LastError, LogItemType.GeneralError);
                SetPrimaryRequestStatus(item, null, RequestState.Error, sourceConfig.LastError);
                await RecordPrimaryRequestFailure(item, sourceConfig.LastError);
                return new SubscriptionProcessResult(sourceConfig.LastError, SubscriptionRequestOutcome.Failed);
            }

            var validationResult = await ValidateExternalCertificateAsset(item, sourceConfig, assetPath);
            if (!validationResult.IsValid)
            {
                sourceConfig.LastError = validationResult.Message;
                LogMessage(item.Id, $"External certificate update rejected: {validationResult.Message}", LogItemType.GeneralError);
                SetPrimaryRequestStatus(item, null, RequestState.Error, validationResult.Message ?? "External certificate update failed validation.");
                await RecordPrimaryRequestFailure(item, validationResult.Message ?? "External certificate update failed validation.");
                return new SubscriptionProcessResult(validationResult.Message, SubscriptionRequestOutcome.Failed);
            }

            LogMessage(item.Id, $"External certificate asset validated. Thumbprint: {validationResult.Thumbprint}; valid until {validationResult.DateExpiry:u}; lifetime elapsed: {validationResult.PercentageElapsed}%.");
            SetPrimaryRequestStatus(item, null, RequestState.Success, "External certificate pulled from Management Hub.");

            var deploymentResult = await DeployExternalCertificateAsset(item, sourceConfig, assetPath, fetchResult.SourceVersion, requestMode == SubscriptionRequestMode.Manual ? "Manual external subscription request" : "External source update");

            if (deploymentResult.IsSuccess && requestMode == SubscriptionRequestMode.Manual)
            {
                return new SubscriptionProcessResult("External certificate pulled from Management Hub and deployment completed.", SubscriptionRequestOutcome.Completed);
            }

            return new SubscriptionProcessResult(
                deploymentResult.Message,
                deploymentResult.IsSuccess ? SubscriptionRequestOutcome.Completed : SubscriptionRequestOutcome.Failed);
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

            var pendingVersion = string.IsNullOrWhiteSpace(sourceVersion)
                ? DateTimeOffset.UtcNow.UtcTicks.ToString()
                : sourceVersion;

            if (string.Equals(sourceConfig.LastSourceVersion, pendingVersion, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sourceConfig.PendingSourceVersion, pendingVersion, StringComparison.OrdinalIgnoreCase))
            {
                return new ActionResult("External managed certificate update already recorded.", true);
            }

            sourceConfig.PendingSourceVersion = pendingVersion;
            sourceConfig.LastError = null;
            item.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow;

            await UpdateManagedCertificate(item);

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

            return HasPendingSubscriptionUpdate(sourceConfig)
                   || ShouldPollSource(item, sourceConfig, checkDate);
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

        internal static bool IsAutomaticSubscriptionRetryDue(ManagedCertificate item, DateTimeOffset? checkDate = null)
        {
            var now = checkDate ?? DateTimeOffset.UtcNow;
            var renewalIntervalMode = CoreAppSettings.Current.RenewalIntervalMode ?? RenewalIntervalModes.DaysAfterLastRenewal;
            var renewalCheck = ManagedCertificate.CalculateNextRenewalAttempt(item, CoreAppSettings.Current.RenewalIntervalDays, renewalIntervalMode, testDateTime: now);

            return renewalCheck?.IsRenewalDue == true && !renewalCheck.IsRenewalOnHold;
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

            var fetchResult = await FetchExternalCertificateAsset(managedCertificate, sourceConfig, pushedSourceVersion: null, CancellationToken.None, ignoreCurrentVersion: true);

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

        private async Task<ExternalCertificateFetchResult> FetchExternalCertificateAsset(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string? pushedSourceVersion, CancellationToken cancellationToken, bool ignoreCurrentVersion = false)
        {
            var sourceType = sourceConfig.SourceType?.Trim() ?? string.Empty;

            if (sourceType.Equals(ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase))
            {
                return await FetchFromManagementHub(item, sourceConfig, pushedSourceVersion, cancellationToken, ignoreCurrentVersion);
            }

            return new ExternalCertificateFetchResult
            {
                IsSuccess = false,
                Message = $"Unsupported external certificate source type: {sourceType}"
            };
        }

        private async Task<ExternalCertificateFetchResult> FetchFromManagementHub(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string? pushedSourceVersion, CancellationToken cancellationToken, bool ignoreCurrentVersion = false)
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

                var certData = await ReadHubApiFileResponse(response, cancellationToken);
                if (certData.Length == 0)
                {
                    return new ExternalCertificateFetchResult
                    {
                        IsSuccess = false,
                        Message = "ManagementHub source returned an empty certificate payload."
                    };
                }

                var sourceVersion = GetHubApiHeaderValue(response, "ETag");
                sourceVersion ??= Convert.ToHexString(SHA256.HashData(certData)).ToLowerInvariant();

                if (!ignoreCurrentVersion
                    && !string.IsNullOrWhiteSpace(pushedSourceVersion)
                    && !string.IsNullOrWhiteSpace(sourceVersion)
                    && string.Equals(pushedSourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(sourceConfig.LastSourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return new ExternalCertificateFetchResult
                    {
                        IsSuccess = true,
                        HasUpdate = false,
                        SourceVersion = sourceVersion,
                        CertificateData = certData
                    };
                }

                if (!ignoreCurrentVersion
                    && !string.IsNullOrWhiteSpace(sourceConfig.LastSourceVersion)
                    && string.Equals(sourceConfig.LastSourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return new ExternalCertificateFetchResult
                    {
                        IsSuccess = true,
                        HasUpdate = false,
                        SourceVersion = sourceVersion,
                        CertificateData = certData
                    };
                }

                return new ExternalCertificateFetchResult
                {
                    IsSuccess = true,
                    HasUpdate = true,
                    SourceVersion = sourceVersion,
                    CertificateData = certData
                };
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

        private async Task<ActionResult> DeployExternalCertificateAsset(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string assetPath, string? sourceVersion, string reason)
        {
            sourceConfig.PendingSourceVersion = sourceVersion ?? sourceConfig.PendingSourceVersion;

            var metadataApplied = await ApplyExternalCertificateMetadata(item, sourceConfig, assetPath);
            if (!metadataApplied)
            {
                sourceConfig.LastError = SubscriptionPfxLoadErrorMessage;
                LogMessage(item.Id, sourceConfig.LastError, LogItemType.GeneralError);
                SetBindingDeploymentStatus(item, RequestState.Error, sourceConfig.LastError);
                IncrementManagedCertificateRenewalFailureCount(item);
                item.LastRenewalStatus = RequestState.Warning;
                item.RenewalFailureMessage = sourceConfig.LastError;
                await UpdateManagedCertificate(item);
                return new ActionResult(sourceConfig.LastError, false);
            }

            _serviceLog?.Information("Deploying external certificate update for {name} [{id}] - {reason}", item.Name, item.Id, reason);

            // deployment tasks are performed by the caller once the overall request outcome is known, so that status
            // based task triggers are evaluated once using the mode applicable to this request
            var deployResult = await DeployCertificate(item, progress: null, isPreviewOnly: false, includeDeploymentTasks: false);

            if (!deployResult.IsSuccess)
            {
                sourceConfig.LastError = deployResult.Message;
                LogMessage(item.Id, $"External certificate deployment failed after certificate metadata was applied: {deployResult.Message}", LogItemType.CertificateRequestAttentionRequired);
                SetBindingDeploymentStatus(item, RequestState.Error, deployResult.Message);
                IncrementManagedCertificateRenewalFailureCount(item);
                item.LastRenewalStatus = RequestState.Warning;
                item.RenewalFailureMessage = deployResult.Message;

                await UpdateManagedCertificate(item);

                return new ActionResult(sourceConfig.LastError, false);
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
            // deciding whether a fetch is applicable for this request mode

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
