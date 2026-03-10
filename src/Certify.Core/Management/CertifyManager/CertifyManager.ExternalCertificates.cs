#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Shared;
using Certify.Shared.Core.Utils.PKI;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private class ExternalCertificateFetchResult
        {
            public bool IsSuccess { get; set; }
            public bool HasUpdate { get; set; }
            public string? SourceVersion { get; set; }
            public byte[]? CertificateData { get; set; }
            public string? Message { get; set; }
        }

        private readonly ConcurrentDictionary<string, string?> _externalManagedCertificatePushQueue = new();
        private readonly ConcurrentDictionary<string, byte[]> _externalManagedCertificatePushData = new();
        private int _isExternalSubscriptionTaskRunning = 0;

        private void QueueExternalManagedCertificateUpdate(string managedCertificateId, string? sourceVersion = null, byte[]? pfxData = null)
        {
            if (!string.IsNullOrWhiteSpace(managedCertificateId))
            {
                _externalManagedCertificatePushQueue.AddOrUpdate(managedCertificateId, sourceVersion, (_, _) => sourceVersion);

                if (pfxData != null)
                {
                    _externalManagedCertificatePushData.AddOrUpdate(managedCertificateId, pfxData, (_, _) => pfxData);
                }
            }
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
                        && i.ExternalSource?.IsEnabled == true
                        && !string.IsNullOrWhiteSpace(i.ExternalSource.SourceType)
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
                        await ProcessExternalManagedCertificate(item, cancellationToken);
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

        private async Task<ActionResult> ProcessExternalManagedCertificate(ManagedCertificate candidate, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(candidate.Id))
            {
                return new ActionResult("Managed cert ID is missing", false);
            }

            var item = await _itemManager.GetById(candidate.Id);
            if (item?.ExternalSource == null || item.ExternalSource.IsEnabled != true)
            {
                return new ActionResult("Managed cert external source not enabled", false);
            }

            var sourceConfig = item.ExternalSource;
            var hasPushUpdate = _externalManagedCertificatePushQueue.TryRemove(item.Id, out var pushedSourceVersion);
            _externalManagedCertificatePushData.TryRemove(item.Id, out var pushedPfxData);

            if (await TryDeployPendingExternalCertificate(item, sourceConfig, cancellationToken))
            {
                return new ActionResult("Deployment of external cert failed", false);
            }

            if (!hasPushUpdate && !IsPullModeEnabled(sourceConfig) && !IsPushModeEnabled(sourceConfig))
            {
                return new ActionResult("Source push/pull mode not enabled", false);
            }

            if (!hasPushUpdate && !ShouldPollSource(sourceConfig))
            {
                return new ActionResult("Source polling not applicable [ShouldPollSource] ", false);
            }

            ExternalCertificateFetchResult fetchResult;

            if (hasPushUpdate && pushedPfxData != null)
            {
                // PFX bytes were supplied by the management hub push — use them directly and skip the HTTP fetch.
                LogMessage(item.Id, $"Using pushed PFX data for external certificate update for {item.Name} [{item.Id}]");
                fetchResult = new ExternalCertificateFetchResult
                {
                    IsSuccess = true,
                    HasUpdate = true,
                    SourceVersion = pushedSourceVersion,
                    CertificateData = pushedPfxData
                };
            }
            else
            {
                LogMessage(item.Id, $"Fetching external certificate asset");
                fetchResult = await FetchExternalCertificateAsset(item, sourceConfig, pushedSourceVersion, cancellationToken);
            }

            sourceConfig.DateLastPoll = DateTimeOffset.UtcNow;

            if (!fetchResult.IsSuccess)
            {
                sourceConfig.LastError = fetchResult.Message;
                item.LastRenewalStatus = RequestState.Warning;
                item.RenewalFailureMessage = fetchResult.Message;

                await UpdateManagedCertificate(item);
                return new ActionResult(fetchResult.Message, false);
            }

            if (!fetchResult.HasUpdate || fetchResult.CertificateData == null)
            {
                sourceConfig.LastError = null;

                if (hasPushUpdate)
                {
                    await UpdateManagedCertificate(item);
                }

                return new ActionResult("No change to source certificate or no data received", false);
            }

            var assetPath = await StoreExternalCertificateAsset(item, fetchResult.CertificateData);
            if (assetPath == null)
            {
                sourceConfig.LastError = "External certificate update was detected but could not be written to local storage.";
                item.LastRenewalStatus = RequestState.Warning;
                item.RenewalFailureMessage = sourceConfig.LastError;
                await UpdateManagedCertificate(item);
                return new ActionResult(sourceConfig.LastError, false);
            }

            var maintenanceWindowCheck = GetMaintenanceWindowStatus(item);
            if (!maintenanceWindowCheck.IsWithinWindow)
            {
                sourceConfig.PendingCertificatePath = assetPath;
                sourceConfig.PendingSourceVersion = fetchResult.SourceVersion;
                sourceConfig.LastError = null;

                item.LastRenewalStatus = RequestState.Warning;
                item.RenewalFailureMessage = $"External certificate update deferred: {maintenanceWindowCheck.Reason}";

                LogMessage(item.Id, $"Deferred external certificate deployment - {maintenanceWindowCheck.Reason}");
                await UpdateManagedCertificate(item);
                return new ActionResult(sourceConfig.LastError, false);
            }

            var deploymentResult = await DeployExternalCertificateAsset(item, sourceConfig, assetPath, fetchResult.SourceVersion, "External source update");

            return deploymentResult;
        }

        private async Task<bool> TryDeployPendingExternalCertificate(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceConfig.PendingCertificatePath))
            {
                return false;
            }

            if (!File.Exists(sourceConfig.PendingCertificatePath))
            {
                sourceConfig.PendingCertificatePath = null;
                sourceConfig.PendingSourceVersion = null;
                sourceConfig.LastError = "Pending external certificate asset file is no longer available.";

                await UpdateManagedCertificate(item);
                return false;
            }

            var maintenanceWindowCheck = GetMaintenanceWindowStatus(item);
            if (!maintenanceWindowCheck.IsWithinWindow)
            {
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            await DeployExternalCertificateAsset(item, sourceConfig, sourceConfig.PendingCertificatePath, sourceConfig.PendingSourceVersion, "Deferred external update");
            return true;
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

        private static bool ShouldPollSource(ExternalCertificateSubscription sourceConfig)
        {
            if (!IsPullModeEnabled(sourceConfig))
            {
                return false;
            }

            var pollIntervalMinutes = sourceConfig.PollIntervalMinutes <= 0 ? 30 : sourceConfig.PollIntervalMinutes;

            if (!sourceConfig.DateLastPoll.HasValue)
            {
                return true;
            }

            return sourceConfig.DateLastPoll.Value <= DateTimeOffset.UtcNow.AddMinutes(-pollIntervalMinutes);
        }

        private async Task<ExternalCertificateFetchResult> FetchExternalCertificateAsset(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string? pushedSourceVersion, CancellationToken cancellationToken)
        {
            var sourceType = sourceConfig.SourceType?.Trim() ?? string.Empty;

            if (sourceType.Equals(ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase))
            {
                return await FetchFromManagementHub(item, sourceConfig, pushedSourceVersion, cancellationToken);
            }

            return new ExternalCertificateFetchResult
            {
                IsSuccess = false,
                Message = $"Unsupported external certificate source type: {sourceType}"
            };
        }

        private async Task<ExternalCertificateFetchResult> FetchFromManagementHub(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string? pushedSourceVersion, CancellationToken cancellationToken)
        {
            if (!TryParseHubReference(sourceConfig.ExternalReference, out var sourceInstanceId, out var sourceManagedCertificateId))
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = "ManagementHub external reference must be in format '{instanceId}/{managedCertId}'."
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

            var secret = await GetHubClientSecret(sourceConfig.CredentialKey);
            if (secret == null || string.IsNullOrWhiteSpace(secret.ClientId) || string.IsNullOrWhiteSpace(secret.Secret))
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = "ManagementHub source credentials are not configured or could not be unlocked."
                };
            }

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            if (Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_ALLOW_UNTRUSTED") == "true")
            {
                handler.ServerCertificateCustomValidationCallback = null;
            }

            using var httpClient = new HttpClient(handler);
            var request = new HttpRequestMessage(HttpMethod.Get, $"{hubApiBase}/api/v1/certificate/{sourceInstanceId}/download/{sourceManagedCertificateId}/pfx");

            request.Headers.Add("X-Client-ID", secret.ClientId);
            request.Headers.Add("X-Client-Secret", secret.Secret);

            if (!string.IsNullOrWhiteSpace(sourceConfig.LastSourceVersion))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", sourceConfig.LastSourceVersion);
            }

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = true,
                    HasUpdate = false,
                    SourceVersion = sourceConfig.LastSourceVersion
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = $"ManagementHub source returned {(int)response.StatusCode}: {detail}"
                };
            }

            var certData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (certData.Length == 0)
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = false,
                    Message = "ManagementHub source returned an empty certificate payload."
                };
            }

            var sourceVersion = response.Headers.ETag?.Tag?.Replace("\"", string.Empty);
            sourceVersion ??= Convert.ToHexString(SHA256.HashData(certData)).ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(pushedSourceVersion)
                && !string.IsNullOrWhiteSpace(sourceVersion)
                && string.Equals(pushedSourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase)
                && string.Equals(sourceConfig.LastSourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase))
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = true,
                    HasUpdate = false,
                    SourceVersion = sourceVersion
                };
            }

            if (!string.IsNullOrWhiteSpace(sourceConfig.LastSourceVersion)
                && string.Equals(sourceConfig.LastSourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase))
            {
                return new ExternalCertificateFetchResult
                {
                    IsSuccess = true,
                    HasUpdate = false,
                    SourceVersion = sourceVersion
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
                var secret = await _credentialsManager.GetUnlockedCredential(MgmtHubJoiningCredId);
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

        private static (string SecretName, string? Version) ParseKeyVaultReference(string reference)
        {
            var parts = reference.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return (string.Empty, null);
            }

            if (parts.Length == 1)
            {
                return (parts[0], null);
            }

            return (parts[0], parts[1]);
        }

        private static bool TryParseHubReference(string? reference, out string instanceId, out string managedCertificateId)
        {
            instanceId = string.Empty;
            managedCertificateId = string.Empty;

            if (string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            var normalized = reference.Trim().Replace(':', '/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return false;
            }

            instanceId = parts[0];
            managedCertificateId = parts[1];

            return !string.IsNullOrWhiteSpace(instanceId) && !string.IsNullOrWhiteSpace(managedCertificateId);
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
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };

                using var httpClient = new HttpClient(handler);
                var request = new HttpRequestMessage(HttpMethod.Get, $"{hubApiBase}/internal/v1/hub/subscription/available");
                request.Headers.Add("X-Client-ID", secret.ClientId);
                request.Headers.Add("X-Client-Secret", secret.Secret);

                if (!string.IsNullOrWhiteSpace(_serverConfig?.HubAssignedInstanceId))
                {
                    request.Headers.Add("X-Certify-HubAssignedId", _serverConfig.HubAssignedInstanceId);
                }

                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync();
                    _serviceLog?.Warning("GetHubSubscribableManagedCertificates: hub returned {status}: {detail}", (int)response.StatusCode, detail);
                    return new();
                }

                var json = await response.Content.ReadAsStringAsync();
                return System.Text.Json.JsonSerializer.Deserialize<List<ManagedCertificateSummary>>(json, Certify.Shared.JsonOptions.DefaultJsonSerializerOptions) ?? new();
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

        private async Task<ActionResult> DeployExternalCertificateAsset(ManagedCertificate item, ExternalCertificateSubscription sourceConfig, string assetPath, string? sourceVersion, string reason)
        {
            var metadataApplied = await ApplyExternalCertificateMetadata(item, assetPath);
            if (!metadataApplied)
            {
                sourceConfig.LastError = "External certificate update could not be validated as deployable PFX data.";
                item.LastRenewalStatus = RequestState.Warning;
                item.RenewalFailureMessage = sourceConfig.LastError;
                await UpdateManagedCertificate(item);
                return new ActionResult(sourceConfig.LastError, false);
            }

            sourceConfig.PendingCertificatePath = null;
            sourceConfig.PendingSourceVersion = null;
            sourceConfig.LastSourceVersion = sourceVersion ?? sourceConfig.LastSourceVersion;
            sourceConfig.LastError = null;

            _serviceLog?.Information("Deploying external certificate update for {name} [{id}] - {reason}", item.Name, item.Id, reason);

            var deployResult = await DeployCertificate(item, progress: null, isPreviewOnly: false, includeDeploymentTasks: true);

            if (!deployResult.IsSuccess)
            {
                sourceConfig.LastError = deployResult.Message;
                item.LastRenewalStatus = RequestState.Warning;
                item.RenewalFailureMessage = deployResult.Message;

                await UpdateManagedCertificate(item);

                return new ActionResult(sourceConfig.LastError, false);
            }
            else
            {
                return new ActionResult("DeployExternalCertificateAsset:OK", true);
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

                certInfo.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                _serviceLog?.Error(ex, "Failed to validate or parse external certificate asset for {name} [{id}]", item.Name, item.Id);
                return false;
            }
        }

        private async Task<CertificateRequestResult> PerformExternalManagedCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress)
        {
            var result = new CertificateRequestResult(managedCertificate)
            {
                IsSuccess = false
            };

            var sourceConfig = managedCertificate.ExternalSource;
            if (sourceConfig == null || sourceConfig.IsEnabled != true)
            {
                result.Message = "External subscription is not enabled for this managed certificate.";
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            if (!string.Equals(sourceConfig.SourceType, ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase))
            {
                result.Message = $"Manual request is not currently supported for external source type '{sourceConfig.SourceType}'.";
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            if (!TryParseHubReference(sourceConfig.ExternalReference, out var sourceInstanceId, out var sourceManagedCertificateId))
            {
                result.Message = "Managed Hub external reference must be in format '{instanceId}/{managedCertId}'.";
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            var retrievalMode = sourceConfig.RetrievalMode ?? ExternalCertificateRetrievalModes.Push;
            if (retrievalMode.Equals(ExternalCertificateRetrievalModes.Auto, StringComparison.OrdinalIgnoreCase)
                || retrievalMode.Equals(ExternalCertificateRetrievalModes.Push, StringComparison.OrdinalIgnoreCase))
            {
                if (_managementServerClient?.IsConnected() != true)
                {
                    result.Message = "Cannot request an external push update because this instance is not connected to the Management Hub.";
                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                    return result;
                }

                var updateRequest = new ExternalManagedCertificateRequest
                {
                    TargetManagedCertificateId = managedCertificate.Id,
                    SourceInstanceId = sourceInstanceId,
                    SourceManagedCertificateId = sourceManagedCertificateId
                };

                _managementServerClient.SendNotificationToManagementHub(
                    ManagementHubCommands.NotificationRequestExternalManagedCertificateUpdate,
                    updateRequest);

                managedCertificate.LastRenewalStatus = RequestState.Running;
                managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow;
                managedCertificate.RenewalFailureMessage = "External certificate update requested from Management Hub. Awaiting push update.";
                await UpdateManagedCertificate(managedCertificate);

                result.IsSuccess = true;
                result.Message = managedCertificate.RenewalFailureMessage;
                ReportProgress(progress, new RequestProgressState(RequestState.Running, result.Message, managedCertificate), logThisEvent: false);

                return result;
            }

            if (string.IsNullOrWhiteSpace(sourceConfig.CredentialKey))
            {
                result.Message = "Pull mode requires a Management Hub certificate consumer credential.";
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            var fetchResult = await FetchFromManagementHub(managedCertificate, sourceConfig, pushedSourceVersion: null, CancellationToken.None);
            sourceConfig.DateLastPoll = DateTimeOffset.UtcNow;

            if (!fetchResult.IsSuccess)
            {
                sourceConfig.LastError = fetchResult.Message;
                managedCertificate.LastRenewalStatus = RequestState.Error;
                managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow;
                managedCertificate.RenewalFailureMessage = fetchResult.Message;

                await UpdateManagedCertificate(managedCertificate);

                result.Message = fetchResult.Message ?? "Failed to retrieve certificate from Management Hub.";
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            if (!fetchResult.HasUpdate || fetchResult.CertificateData == null)
            {
                sourceConfig.LastError = null;
                managedCertificate.LastRenewalStatus = RequestState.Success;
                managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow;
                managedCertificate.RenewalFailureMessage = "No updated certificate was available from Management Hub.";
                await UpdateManagedCertificate(managedCertificate);

                result.IsSuccess = true;
                result.Message = managedCertificate.RenewalFailureMessage;
                ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            var assetPath = await StoreExternalCertificateAsset(managedCertificate, fetchResult.CertificateData);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                result.Message = "External certificate update was detected but could not be written to local storage.";
                sourceConfig.LastError = result.Message;
                managedCertificate.LastRenewalStatus = RequestState.Error;
                managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow;
                managedCertificate.RenewalFailureMessage = result.Message;
                await UpdateManagedCertificate(managedCertificate);
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            await DeployExternalCertificateAsset(managedCertificate, sourceConfig, assetPath, fetchResult.SourceVersion, "Manual external subscription request");

            if (!string.IsNullOrWhiteSpace(sourceConfig.LastError))
            {
                result.Message = sourceConfig.LastError;
                result.IsSuccess = false;
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow;
            managedCertificate.LastRenewalStatus = RequestState.Success;
            managedCertificate.RenewalFailureMessage = "External certificate pulled from Management Hub and deployment completed.";
            await UpdateManagedCertificate(managedCertificate);

            result.IsSuccess = true;
            result.Message = managedCertificate.RenewalFailureMessage;

            ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate), logThisEvent: false);

            return result;
        }
    }
}
