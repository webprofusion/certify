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
        private enum ExternalSubscriptionRequestMode
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

        private int _isExternalSubscriptionTaskRunning = 0;

        public static string GetExternalSubscriptionPfxLoadErrorMessage()
        {
            return "External certificate update could not be validated as deployable PFX data. The PFX may require a different password credential setting.";
        }

        private async Task PerformExternalCertificateSubscriptionTasks(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _isExternalSubscriptionTaskRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                if (IsInDegradedMode)
                {
                    return;
                }

                var allItems = await _itemManager.Find(ManagedCertificateFilter.ALL);

                var targetItems = allItems
                    .Where(i =>
                        i.ItemType == ManagedCertificateType.SSL_ExternallyManaged
                        && !string.IsNullOrWhiteSpace(i.ExternalSource?.ExternalReference)
                        && !string.IsNullOrWhiteSpace(i.ExternalSource?.SourceType)
                        )
                    .ToList();

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

                    try
                    {
                        await ProcessExternalManagedCertificate(item, ExternalSubscriptionRequestMode.Automatic, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _serviceLog?.Error(ex, "External certificate processing failed for {name} [{id}]", item.Name, item.Id);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isExternalSubscriptionTaskRunning, 0);
            }
        }

        private async Task<ActionResult> ProcessExternalManagedCertificate(ManagedCertificate candidate, ExternalSubscriptionRequestMode requestMode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(candidate.Id))
            {
                return new ActionResult("Managed cert ID is missing", false);
            }

            var item = await _itemManager.GetById(candidate.Id);
            if (item?.ExternalSource == null)
            {
                return new ActionResult("Managed cert external source not configured", false);
            }

            var sourceConfig = item.ExternalSource;
            var hasPendingSourceUpdate = HasPendingExternalSourceUpdate(sourceConfig);

            var canFetchFromSource = hasPendingSourceUpdate || IsPullModeEnabled(sourceConfig);
            if (!canFetchFromSource)
            {
                if (requestMode == ExternalSubscriptionRequestMode.Manual)
                {
                    return new ActionResult($"Manual request is not currently supported for external source type '{sourceConfig.SourceType}'.", false);
                }

                return new ActionResult("Source polling not enabled", false);
            }

            var shouldFetchFromSource = requestMode == ExternalSubscriptionRequestMode.Manual
                || hasPendingSourceUpdate
                || ShouldPollSource(item, sourceConfig);

            if (!shouldFetchFromSource)
            {
                return new ActionResult("Source polling not applicable [ShouldPollSource] ", false);
            }

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
                return new ActionResult(deferredMessage, false);
            }

            ClearPrimaryAndBindingRequestStatus(item);

            LogExternalSubscriptionStart(item, sourceConfig, requestMode);

            var fetchResult = await FetchExternalCertificateAsset(
                item,
                sourceConfig,
                pushedSourceVersion: hasPendingSourceUpdate ? sourceConfig.PendingSourceVersion : null,
                cancellationToken,
                ignoreCurrentVersion: requestMode == ExternalSubscriptionRequestMode.Manual);

            sourceConfig.DateLastPoll = DateTimeOffset.UtcNow;

            if (!fetchResult.IsSuccess)
            {
                sourceConfig.LastError = fetchResult.Message;
                LogMessage(item.Id, $"External certificate subscription {GetExternalActionNoun(requestMode)} failed: {fetchResult.Message}", LogItemType.GeneralError);
                SetPrimaryRequestStatus(item, null, RequestState.Error, fetchResult.Message ?? "Failed to retrieve certificate from external source.");

                await RecordPrimaryRequestFailure(item, fetchResult.Message ?? "Failed to retrieve certificate from external source.");
                return new ActionResult(fetchResult.Message, false);
            }

            if (!fetchResult.HasUpdate || fetchResult.CertificateData == null)
            {
                var message = "No updated certificate was available from Management Hub.";
                var noUpdateStatus = await RecordSubscriptionNoUpdate(item, message);
                LogMessage(item.Id, $"External certificate subscription {GetExternalActionNoun(requestMode)} completed with no update. Source version: {FormatSourceVersion(fetchResult.SourceVersion)}. Recorded status: {noUpdateStatus}; failure count: {item.RenewalFailureCount}.", noUpdateStatus == RequestState.Error ? LogItemType.GeneralError : LogItemType.CertificateRequestAttentionRequired);
                SetPrimaryRequestStatus(item, null, noUpdateStatus, message);

                ClearExternalManagedCertificateRenewalTrigger(item, sourceConfig, clearPendingSourceVersion: true);
                sourceConfig.LastError = noUpdateStatus == RequestState.Error ? message : null;
                await UpdateManagedCertificate(item);

                return new ActionResult(message, false);
            }

            LogMessage(item.Id, $"External certificate update detected. Source version: {FormatSourceVersion(fetchResult.SourceVersion)}.");

            var assetPath = await StoreExternalCertificateAsset(item, fetchResult.CertificateData);
            if (assetPath == null)
            {
                sourceConfig.LastError = "External certificate update was detected but could not be written to local storage.";
                LogMessage(item.Id, sourceConfig.LastError, LogItemType.GeneralError);
                SetPrimaryRequestStatus(item, null, RequestState.Error, sourceConfig.LastError);
                await RecordPrimaryRequestFailure(item, sourceConfig.LastError);
                return new ActionResult(sourceConfig.LastError, false);
            }

            var validationResult = await ValidateExternalCertificateAsset(item, assetPath);
            if (!validationResult.IsValid)
            {
                sourceConfig.LastError = validationResult.Message;
                LogMessage(item.Id, $"External certificate update rejected: {validationResult.Message}", LogItemType.GeneralError);
                SetPrimaryRequestStatus(item, null, RequestState.Error, validationResult.Message ?? "External certificate update failed validation.");
                await RecordPrimaryRequestFailure(item, validationResult.Message ?? "External certificate update failed validation.");
                return new ActionResult(validationResult.Message, false);
            }

            LogMessage(item.Id, $"External certificate asset validated. Thumbprint: {validationResult.Thumbprint}; valid until {validationResult.DateExpiry:u}; lifetime elapsed: {validationResult.PercentageElapsed}%.");
            SetPrimaryRequestStatus(item, null, RequestState.Success, "External certificate pulled from Management Hub.");

            var deploymentResult = await DeployExternalCertificateAsset(item, sourceConfig, assetPath, fetchResult.SourceVersion, requestMode == ExternalSubscriptionRequestMode.Manual ? "Manual external subscription request" : "External source update");

            if (deploymentResult.IsSuccess && requestMode == ExternalSubscriptionRequestMode.Manual)
            {
                return new ActionResult("External certificate pulled from Management Hub and deployment completed.", true);
            }

            return deploymentResult;
        }

        private async Task<ActionResult> MarkExternalManagedCertificateUpdateAvailable(string managedCertificateId, string? sourceVersion)
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

        private (bool IsWithinWindow, string Reason) GetMaintenanceWindowStatus(ManagedCertificate item)
        {
            var prefs = new RenewalPrefs
            {
                MaintenanceWindows = CoreAppSettings.Current.MaintenanceWindows ?? [],
                DefaultMaintenanceWindowId = CoreAppSettings.Current.DefaultMaintenanceWindowId
            };

            return RenewalManager.IsWithinMaintenanceWindow(item, prefs);
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

        internal static bool HasPendingExternalCertificateUpdate(ExternalCertificateSubscription? sourceConfig)
        {
            return HasPendingExternalSourceUpdate(sourceConfig);
        }

        internal static bool HasPendingExternalSourceUpdate(ExternalCertificateSubscription? sourceConfig)
        {
            return !string.IsNullOrWhiteSpace(sourceConfig?.PendingSourceVersion);
        }

        internal static bool ShouldProcessExternalManagedCertificate(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, DateTimeOffset? checkDate = null)
        {
            return HasPendingExternalCertificateUpdate(sourceConfig)
                   || ShouldPollSource(item, sourceConfig, checkDate);
        }

        internal static bool ShouldPollSource(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, DateTimeOffset? checkDate = null)
        {
            if (!IsPullModeEnabled(sourceConfig))
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
            // Management Hub subscriptions are expected to receive push update notifications;
            // polling is only used as fallback when normal renewal scheduling is due.
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

        internal static void ClearExternalManagedCertificateRenewalTrigger(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, bool clearPendingSourceVersion)
        {
            item.DateNextScheduledRenewalAttempt = null;

            if (clearPendingSourceVersion)
            {
                sourceConfig.PendingSourceVersion = null;
            }
        }

        private async Task<List<StatusMessage>> TestExternalSubscriptionAccess(Certify.Models.Providers.ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState>? progress = null)
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

        private static string GetExternalActionNoun(ExternalSubscriptionRequestMode requestMode)
        {
            return requestMode == ExternalSubscriptionRequestMode.Manual ? "request" : "pull";
        }

        private void LogExternalSubscriptionStart(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, ExternalSubscriptionRequestMode requestMode)
        {
            if (requestMode == ExternalSubscriptionRequestMode.Manual)
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

                return results;
            }
            catch (ApiException ex)
            {
                _serviceLog?.Warning("GetHubSubscribableManagedCertificates: hub returned {status}: {detail}", ex.StatusCode, ex.Response);
                return new();
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "GetHubSubscribableManagedCertificates failed.");
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
                        }

                        hubAssignedInstanceId = _serverConfig?.HubAssignedInstanceId ?? joinCheck.Result.HubAssignedInstanceId;
                    }
                    else
                    {
                        _serviceLog?.Warning("CreateManagementHubRequestContext: unable to refresh request auth secret before calling {hubApiBase}: {message}", hubApiBase, joinCheck.Message);
                    }
                }
                catch (Exception ex)
                {
                    _serviceLog?.Warning("CreateManagementHubRequestContext: failed to refresh request auth secret before calling {hubApiBase}: {message}", hubApiBase, ex.Message);
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

        private async Task<ExternalCertificateValidationResult> ValidateExternalCertificateAsset(ManagedCertificate item, string assetPath)
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
                    Message = GetExternalSubscriptionPfxLoadErrorMessage()
                };
            }
        }

        private async Task<ActionResult> DeployExternalCertificateAsset(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string assetPath, string? sourceVersion, string reason)
        {
            sourceConfig.PendingSourceVersion = sourceVersion ?? sourceConfig.PendingSourceVersion;

            var metadataApplied = await ApplyExternalCertificateMetadata(item, assetPath);
            if (!metadataApplied)
            {
                sourceConfig.LastError = GetExternalSubscriptionPfxLoadErrorMessage();
                LogMessage(item.Id, sourceConfig.LastError, LogItemType.GeneralError);
                SetBindingDeploymentStatus(item, RequestState.Error, sourceConfig.LastError);
                IncrementManagedCertificateRenewalFailureCount(item);
                item.LastRenewalStatus = RequestState.Warning;
                item.RenewalFailureMessage = sourceConfig.LastError;
                await UpdateManagedCertificate(item);
                return new ActionResult(sourceConfig.LastError, false);
            }

            _serviceLog?.Information("Deploying external certificate update for {name} [{id}] - {reason}", item.Name, item.Id, reason);

            var deployResult = await DeployCertificate(item, progress: null, isPreviewOnly: false, includeDeploymentTasks: true);

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
                ClearExternalManagedCertificateRenewalTrigger(item, sourceConfig, clearPendingSourceVersion: true);
                sourceConfig.LastSourceVersion = sourceVersion ?? sourceConfig.LastSourceVersion;
                sourceConfig.LastError = null;

                var successMessage = $"External certificate deployment completed successfully. Source version: {FormatSourceVersion(sourceVersion)}.";
                SetBindingDeploymentStatus(item, RequestState.Success, successMessage);
                LogMessage(item.Id, successMessage, LogItemType.CertificateRequestSuccessful);
                await UpdateManagedCertificate(item);
                return new ActionResult(successMessage, true);
            }
        }

        private async Task<bool> ApplyExternalCertificateMetadata(ManagedCertificate item, string assetPath)
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

        private async Task<CertificateRequestResult> PerformAutomaticExternalManagedCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress)
        {
            while (Interlocked.CompareExchange(ref _isExternalSubscriptionTaskRunning, 1, 0) != 0)
            {
                await Task.Delay(1000);
            }

            try
            {
                return await PerformExternalManagedCertificateRequest(managedCertificate, progress, ExternalSubscriptionRequestMode.Automatic);
            }
            finally
            {
                Interlocked.Exchange(ref _isExternalSubscriptionTaskRunning, 0);
            }
        }

        private async Task<CertificateRequestResult> PerformExternalManagedCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress)
        {
            return await PerformExternalManagedCertificateRequest(managedCertificate, progress, ExternalSubscriptionRequestMode.Manual);
        }

        private async Task<CertificateRequestResult> PerformExternalManagedCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, ExternalSubscriptionRequestMode requestMode)
        {
            var result = new CertificateRequestResult(managedCertificate)
            {
                IsSuccess = false
            };

            var sourceConfig = managedCertificate.ExternalSource;
            ClearPrimaryAndBindingRequestStatus(managedCertificate);
            if (sourceConfig == null)
            {
                result.Message = "External subscription is not configured for this managed certificate.";
                LogMessage(managedCertificate.Id, result.Message, LogItemType.GeneralError);
                SetPrimaryRequestStatus(managedCertificate, result, RequestState.Error, result.Message);
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            if (requestMode == ExternalSubscriptionRequestMode.Automatic && !ShouldProcessExternalManagedCertificate(managedCertificate, sourceConfig))
            {
                result.Message = "External certificate subscription is not due and has no pending certificate update.";
                return result;
            }

            var processResult = await ProcessExternalManagedCertificate(managedCertificate, requestMode, CancellationToken.None);
            var updatedManagedCertificate = await _itemManager.GetById(managedCertificate.Id) ?? managedCertificate;

            result.ManagedItem = updatedManagedCertificate;
            result.IsSuccess = processResult.IsSuccess;
            result.Message = processResult.Message;

            var finalState = RequestState.Warning;
            if (processResult.IsSuccess)
            {
                finalState = RequestState.Success;
            }
            else if (updatedManagedCertificate.LastRenewalStatus.HasValue)
            {
                finalState = updatedManagedCertificate.LastRenewalStatus.Value;
            }

            ReportProgress(progress, new RequestProgressState(finalState, result.Message, updatedManagedCertificate), logThisEvent: false);
            return result;
        }
    }
}
