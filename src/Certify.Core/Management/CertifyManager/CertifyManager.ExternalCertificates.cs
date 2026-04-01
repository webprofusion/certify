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
        private class ExternalCertificateFetchResult
        {
            public bool IsSuccess { get; set; }
            public bool HasUpdate { get; set; }
            public string? SourceVersion { get; set; }
            public byte[]? CertificateData { get; set; }
            public string? Message { get; set; }
        }

        private int _isExternalSubscriptionTaskRunning = 0;

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
                        await ProcessExternalManagedCertificate(item, isInteractive: false, cancellationToken);
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

        private async Task<ActionResult> ProcessExternalManagedCertificate(ManagedCertificate candidate, bool isInteractive, CancellationToken cancellationToken)
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

            if (await TryDeployPendingExternalCertificate(item, sourceConfig, cancellationToken))
            {
                return new ActionResult("Deployment of external cert failed", false);
            }

            if (!IsPullModeEnabled(sourceConfig))
            {
                return new ActionResult("Source polling not enabled", false);
            }

            if (!isInteractive && !ShouldPollSource(sourceConfig))
            {
                return new ActionResult("Source polling not applicable [ShouldPollSource] ", false);
            }

            LogMessage(item.Id, $"Fetching external certificate asset");
            var fetchResult = await FetchExternalCertificateAsset(item, sourceConfig, pushedSourceVersion: null, cancellationToken);

            sourceConfig.DateLastPoll = DateTimeOffset.UtcNow;

            if (!fetchResult.IsSuccess)
            {
                sourceConfig.LastError = fetchResult.Message;
                item.LastRenewalStatus = RequestState.Warning;
                item.RenewalFailureMessage = fetchResult.Message;

                await UpdateManagedCertificate(item);
                return new ActionResult(fetchResult.Message, false);
            }

            if ((!isInteractive && !fetchResult.HasUpdate) || fetchResult.CertificateData == null)
            {
                sourceConfig.LastError = null;

                await UpdateManagedCertificate(item);

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

        private async Task<ActionResult> MarkExternalManagedCertificateUpdateAvailable(string managedCertificateId, string? sourceVersion)
        {
            if (string.IsNullOrWhiteSpace(managedCertificateId))
            {
                return new ActionResult("Managed cert ID is missing", false);
            }

            var item = await _itemManager.GetById(managedCertificateId);
            if (item?.ExternalSource == null || item.ExternalSource.IsEnabled != true)
            {
                return new ActionResult("Managed cert external source not enabled", false);
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

        private async Task<List<StatusMessage>> TestExternalSubscriptionAccess(Certify.Models.Providers.ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState>? progress = null)
        {
            var results = new List<StatusMessage>();

            var sourceConfig = managedCertificate.ExternalSource;
            if (sourceConfig == null || sourceConfig.IsEnabled != true)
            {
                results.Add(new StatusMessage
                {
                    IsOK = false,
                    Message = "External subscription is not enabled for this managed certificate."
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

                if (!string.IsNullOrWhiteSpace(pushedSourceVersion)
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

                if (!string.IsNullOrWhiteSpace(sourceConfig.LastSourceVersion)
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

            // Manual requests should explicitly ask the Management Hub for the latest exportable PFX
            // instead of depending on the timing of an asynchronous push update.
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

                if (string.IsNullOrWhiteSpace(sourceConfig.PendingCertificatePath))
                {
                    sourceConfig.PendingSourceVersion = null;
                }

                managedCertificate.LastRenewalStatus = RequestState.Success;
                managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow;
                managedCertificate.RenewalFailureMessage = "";
                await UpdateManagedCertificate(managedCertificate);

                result.IsSuccess = true;
                result.Message = "No updated certificate was available from Management Hub.";
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

                managedCertificate.LastRenewalStatus = RequestState.Error;
                managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow;
                managedCertificate.RenewalFailureMessage = result.Message;
                await UpdateManagedCertificate(managedCertificate);

                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate), logThisEvent: false);
                return result;
            }

            managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow;
            managedCertificate.LastRenewalStatus = RequestState.Success;
            managedCertificate.RenewalFailureMessage = "";
            await UpdateManagedCertificate(managedCertificate);

            result.IsSuccess = true;
            result.Message = "External certificate pulled from Management Hub and deployment completed.";

            ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate), logThisEvent: false);

            return result;
        }
    }
}
