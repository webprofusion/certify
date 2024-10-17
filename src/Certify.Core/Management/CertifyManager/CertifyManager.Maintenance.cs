﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.AccessControl;
using Certify.Models.Shared;

namespace Certify.Management
{
    public partial class CertifyManager
    {

        /// <summary>
        /// Upgrade/migrate settings from previous version if applicable
        /// </summary>
        /// <returns></returns>
        private async Task UpgradeSettings()
        {
            var systemVersion = Util.GetAppVersion().ToString();
            var previousVersion = CoreAppSettings.Current.CurrentServiceVersion;

            if (CoreAppSettings.Current.CurrentServiceVersion != systemVersion)
            {
                _tc?.TrackEvent("ServiceUpgrade", new Dictionary<string, string> {
                    { "previousVersion", previousVersion },
                    { "currentVersion", systemVersion }
                });

                // service has been updated, run any required migrations
                await PerformServiceUpgrades();

                CoreAppSettings.Current.CurrentServiceVersion = systemVersion;
                SettingsManager.SaveAppSettings();

                var accessControl = await GetCurrentAccessControl();

                if (await accessControl.IsInitialized() == false)
                {
                    await BootstrapTestAdminUserAndRoles(accessControl);
                }
                else
                {
                    await UpdateStandardRoles(accessControl);
                }
            }
        }

        private static async Task BootstrapTestAdminUserAndRoles(IAccessControl access)
        {
            // setup roles with policies
            await UpdateStandardRoles(access);

            var adminSp = new SecurityPrinciple
            {
                Id = "admin_01",
                Description = "Primary default admin",
                PrincipleType = SecurityPrincipleType.User,
                Username = "admin",
                Password = "admin",
                Provider = StandardIdentityProviders.INTERNAL
            };

            await access.AddSecurityPrinciple(adminSp.Id, adminSp, bypassIntegrityCheck: true);

            // assign security principles to roles
            var assignedRoles = new List<AssignedRole> {
                 // administrator
                 new AssignedRole{
                     Id= Guid.NewGuid().ToString(),
                     RoleId=StandardRoles.Administrator.Id,
                     SecurityPrincipleId=adminSp.Id
                 }
            };

            foreach (var r in assignedRoles)
            {
                // add roles and policy assignments to store
                await access.AddAssignedRole(r);
            }
        }

        /// <summary>
        /// Add/update standard system roles, policies and resource actions
        /// </summary>
        /// <param name="access"></param>
        /// <returns></returns>
        private static async Task UpdateStandardRoles(IAccessControl access)
        {
            // setup roles with policies

            var actions = Policies.GetStandardResourceActions();

            foreach (var action in actions)
            {
                await access.AddResourceAction(action);
            }

            // setup policies with actions

            var policies = Policies.GetStandardPolicies();

            // add policies to store
            foreach (var r in policies)
            {
                _ = await access.AddResourcePolicy(null, r, bypassIntegrityCheck: true);
            }

            // setup roles with policies
            var roles = Policies.GetStandardRoles();

            foreach (var r in roles)
            {
                // add roles and policy assignments to store
                await access.AddRole(r);
            }
        }

        /// <summary>
        /// When called, perform daily cache cleanup, cert cleanup, diagnostics and maintenance
        /// </summary>
        /// <returns></returns>
        public async Task<bool> PerformDailyMaintenanceTasks()
        {
            try
            {
                _serviceLog?.Information($"Checking for daily tasks..");

                _tc?.TrackEvent("ServiceDailyTaskCheck");

                // clear old cache of challenge responses
                _currentChallenges = new ConcurrentDictionary<string, SimpleAuthorizationChallengeItem>();

                // use latest settings
                SettingsManager.LoadAppSettings();

                // perform expired cert cleanup (if enabled)
                if (CoreAppSettings.Current.EnableCertificateCleanup)
                {
                    await PerformCertificateCleanup();
                }

                // perform diagnostics and status notifications if required
                await PerformScheduledDiagnostics();

                // perform item db maintenance
                await _itemManager.PerformMaintenance();

                // PerformCAMaintenance();

                ApplyLatestAutoUpdateScript();

            }
            catch (Exception exp)
            {
                _tc?.TrackException(exp);

                _serviceLog?.Error($"Exception during daily task check..: {exp}");

                return await Task.FromResult(false);
            }

            return await Task.FromResult(true);
        }

        public async Task<List<ActionResult>> PerformCertificateMaintenanceTasks(string managedItemId = null)
        {
            if (_isRenewAllInProgress)
            {
                return new List<ActionResult> { new ActionResult("Skipped OCSP and ARI Checks. Renewals in progress.", true) };
            }

            var steps = new List<ActionResult>();

            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                cancellationTokenSource.CancelAfter(30 * 60 * 1000); // 30 min auto-cancellation
                await PerformCertificateStatusChecks(cancellationTokenSource.Token, managedItemId);
                steps.Add(new ActionResult("Performed OCSP and ARI Checks", true));
            }

            return steps;
        }

        private DateTimeOffset? _lastStatusCheckInProgress = null;

        /// <summary>
        /// When called, perform OCSP checks and ACME Renewal Info (ARI) checks on all managed certs or a subsample, or a single item
        /// </summary>
        private async Task PerformCertificateStatusChecks(CancellationToken cancelToken, string managedItemId = null)
        {
            if (_lastStatusCheckInProgress != null)
            {
                _serviceLog.Warning("PerformCertificateStatusChecks: already in progress, skipping..");
                return;
            }

            cancelToken.Register(() =>
            {
                // clear tracking of in-progress status checks
                _lastStatusCheckInProgress = null;
            });

            _lastStatusCheckInProgress = DateTimeOffset.UtcNow;

            try
            {
                // perform OCSP checks for every active cert, possibly once per day. If revoked, immediately renew.
                // perform ARI RenewalInfo checks (where supported by the CA), possibly once per day, if suggested renewal much less than planned renewal then set planned renewal date in window or immediate
                _serviceLog.Verbose("Performing Certificate Status Checks");

                var batchSize = 100;
                var checkThrottleMS = 2500;
                var lastCheckOlderThanMinutes = 12 * 60;
                var ocspItemsToCheck = await _itemManager.Find(new ManagedCertificateFilter { LastOCSPCheckMins = (managedItemId == null ? lastCheckOlderThanMinutes : (int?)null), MaxResults = batchSize, Id = managedItemId });

                var completedOcspUpdateChecks = new List<string>();
                var completedRenewalInfoChecks = new List<string>();
                var itemsWhichRequireRenewal = new List<string>();

                var itemsOcspRevoked = new List<string>();
                var itemsOcspExpired = new List<string>();
                var itemsViaARI = new Dictionary<string, DateTimeOffset>();

                if (ocspItemsToCheck?.Any() == true)
                {
                    _serviceLog.Information(template: $"Checking OCSP for {ocspItemsToCheck.Count} items");

                    foreach (var item in ocspItemsToCheck)
                    {
                        if (cancelToken.IsCancellationRequested)
                        {
                            break;
                        }

                        if (!string.IsNullOrEmpty(item.CertificatePath) && File.Exists(item.CertificatePath))
                        {
                            // perform OCSP check
                            _serviceLog.Verbose($"Checking {item.Name} : {item.Id} ");

                            var ocspCheck = await CertificateManager.CheckOcspRevokedStatus(item.CertificatePath, await GetPfxPassword(item), _serviceLog);

                            if (ocspCheck == Models.Certify.Models.CertificateStatusType.Revoked || ocspCheck == Models.Certify.Models.CertificateStatusType.Expired)
                            {
                                // this item requires a renewal attempt
                                _serviceLog.Verbose($"Item {item.Name} failed the OCSP check [{ocspCheck}] and will be queued for a renewal attempt");
                                if (!itemsWhichRequireRenewal.Contains(item.Id))
                                {
                                    itemsWhichRequireRenewal.Add(item.Id);
                                    if (ocspCheck == Models.Certify.Models.CertificateStatusType.Revoked)
                                    {
                                        itemsOcspRevoked.Add(item.Id);
                                    }
                                    else if (ocspCheck == Models.Certify.Models.CertificateStatusType.Expired)
                                    {
                                        itemsOcspExpired.Add(item.Id);
                                    }
                                }
                            }
                            else
                            {
                                if (ocspCheck != Models.Certify.Models.CertificateStatusType.TryLater)
                                {
                                    completedOcspUpdateChecks.Add(item.Id);
                                }
                            }
                        }

                        await Task.Delay(checkThrottleMS);
                    }

                    _serviceLog.Verbose("Completed OCSP status checks");
                }

                if (!cancelToken.IsCancellationRequested)
                {
                    var renewalInfoItemsToCheck = await _itemManager.Find(new ManagedCertificateFilter { LastRenewalInfoCheckMins = (managedItemId == null ? lastCheckOlderThanMinutes : (int?)null), MaxResults = batchSize, Id = managedItemId });

                    if (renewalInfoItemsToCheck?.Any() == true)
                    {
                        _serviceLog.Information($"Performing Renewal Info checks for {renewalInfoItemsToCheck.Count} items");

                        var directoryInfoCache = new Dictionary<string, AcmeDirectoryInfo>();

                        foreach (var item in renewalInfoItemsToCheck)
                        {
                            if (cancelToken.IsCancellationRequested)
                            {
                                break;
                            }

                            try
                            {
                                var caAccount = await GetAccountDetails(item, allowFailover: false, isResumedOrder: true);
                                var provider = await GetACMEProvider(item, caAccount);

                                if (provider != null)
                                {
                                    var providerKey = provider.GetAcmeBaseURI();
                                    directoryInfoCache.TryGetValue(providerKey, out var directoryInfo);

                                    if (directoryInfo == null)
                                    {
                                        directoryInfo = await provider?.GetAcmeDirectory();

                                        if (directoryInfo != null && directoryInfo.NewAccount != null)
                                        {
                                            try
                                            {
                                                directoryInfoCache.Add(providerKey, directoryInfo);
                                            }
                                            catch { }
                                        }
                                    }

                                    if (directoryInfo?.RenewalInfo != null && !string.IsNullOrWhiteSpace(item.CertificateThumbprintHash))
                                    {
                                        _serviceLog.Verbose($"Checking renewal info for {item.Name}");

                                        if (item.ARICertificateId != null && !item.ARICertificateId.Contains("."))
                                        {
                                            // ARI certificate ID not current format, will need to be recomputed.
                                            item.ARICertificateId = null;
                                        }

                                        var x509Cert2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(File.ReadAllBytes(item.CertificatePath), await GetPfxPassword(item));
                                        var ariCertId = item.ARICertificateId ?? Certify.Shared.Core.Utils.PKI.CertUtils.GetARICertIdBase64(x509Cert2);
                                        var info = await provider.GetRenewalInfo(ariCertId);

                                        var nextRenewal = ManagedCertificate.CalculateNextRenewalAttempt(item, CoreAppSettings.Current.RenewalIntervalDays, CoreAppSettings.Current.RenewalIntervalMode ?? RenewalIntervalModes.DaysAfterLastRenewal);

                                        if (info != null && nextRenewal?.DateNextRenewalAttempt != null)
                                        {
                                            // if planned next renewal is beyond the suggested window, set new scheduled renewal date. This allows the user to prefer their own earlier renewal but lets the CA suggest that an even earlier renewal is required (e.g. revocation)
                                            // in the future would could add a pref for the user to "Let the CA decide when best to renew" in order to more strictly keep the renewal within the "suggested" window.
                                            if (nextRenewal.DateNextRenewalAttempt > info.SuggestedWindow?.Start || nextRenewal?.DateNextRenewalAttempt > info.SuggestedWindow?.End)
                                            {
                                                var dateSpan = info.SuggestedWindow.End - info.SuggestedWindow.Start;
                                                var randomMinsInSlot = new Random().Next((int)dateSpan.Value.TotalMinutes);

                                                var scheduledRenewalDate = info.SuggestedWindow?.Start.Value.AddMinutes(randomMinsInSlot) ?? nextRenewal.DateNextRenewalAttempt;

                                                if (scheduledRenewalDate.HasValue)
                                                {
                                                    _serviceLog.Information($"Random renewal date {scheduledRenewalDate} within ARI renewal window [{info.SuggestedWindow?.Start} to {info.SuggestedWindow?.End}] has been set for {item.Name} ");

                                                    itemsViaARI.Add(item.Id, scheduledRenewalDate.Value);

                                                    if (scheduledRenewalDate < DateTimeOffset.Now)
                                                    {
                                                        // item requires immediate renewal
                                                        if (!itemsWhichRequireRenewal.Contains(item.Id))
                                                        {
                                                            itemsWhichRequireRenewal.Add(item.Id);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            _serviceLog.Verbose($"Renewal info unavailable or not supported for {item.Name}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _serviceLog.Warning("Failed to perform renewal info check for {itemName} : {ex}", item.Name, ex);
                            }

                            completedRenewalInfoChecks.Add(item.Id);

                            await Task.Delay(checkThrottleMS);
                        }
                    }
                }

                var allItemsToUpdate = new List<string>(completedOcspUpdateChecks);

                allItemsToUpdate.AddRange(completedRenewalInfoChecks);

                foreach (var i in allItemsToUpdate.Distinct())
                {
                    var item = await _itemManager.GetById(i);

                    if (item != null)
                    {
                        // remember that we have checked these items recently
                        if (completedOcspUpdateChecks.Contains(i))
                        {
                            item.DateLastOcspCheck = DateTimeOffset.UtcNow;
                        }

                        if (completedRenewalInfoChecks.Contains(i))
                        {
                            item.DateLastRenewalInfoCheck = DateTimeOffset.UtcNow;
                        }

                        if (itemsViaARI.ContainsKey(item.Id))
                        {
                            item.DateNextScheduledRenewalAttempt = itemsViaARI[item.Id];
                        }

                        // if item requires immediate renewal, schedule next renewal attempt now.
                        if (itemsWhichRequireRenewal.Contains(item.Id) && item.IncludeInAutoRenew)
                        {
                            if (item.DateExpiry > DateTimeOffset.UtcNow.AddHours(1))
                            {
                                var reason = "Expiry";

                                if (itemsViaARI.ContainsKey(item.Id))
                                {
                                    reason = "ARI Suggested Window";
                                    item.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow;
                                }
                                else if (itemsOcspRevoked.Contains(item.Id))
                                {
                                    reason = "OCSP status (Revoked)";
                                    item.CertificateRevoked = true;
                                    item.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow;
                                }
                                else if (itemsOcspExpired.Contains(item.Id))
                                {
                                    reason = "OCSP status (Expired)";
                                    item.DateNextScheduledRenewalAttempt = DateTimeOffset.UtcNow;
                                }

                                _serviceLog.Information($"Expediting renewal for {item.Name} due to: {reason}");
                            }
                        }

                        await _itemManager.Update(item);
                    }
                    else
                    {
                        _serviceLog.Warning($"Status Checks: Item {i} was not present in database for update. ");
                    }
                }

                _lastStatusCheckInProgress = null;
                _serviceLog.Verbose("Completed Certificate Status Checks");
            }
            catch (Exception ex)
            {
                _lastStatusCheckInProgress = null;
                _serviceLog.Error(ex, "Certificate Status Checks Failed: {err}", ex.Message);
            }
        }

        /// <summary>
        /// //if _AutoUpdate.ps1 file exists, use it to replace AutoUpdate.ps1
        /// </summary>
        private void ApplyLatestAutoUpdateScript()
        {
            if (_useWindowsNativeFeatures)
            {
                var updateScriptLocation = Path.Combine(Environment.CurrentDirectory, "Scripts", "AutoUpdate");
                var pendingUpdateScript = Path.Combine(updateScriptLocation, "_AutoUpdate.ps1");
                if (File.Exists(pendingUpdateScript))
                {
                    try
                    {
                        // move update script
                        var destScript = Path.Combine(updateScriptLocation, "AutoUpdate.ps1");
                        if (File.Exists(destScript))
                        {
                            File.Delete(destScript);
                        }

                        File.Move(pendingUpdateScript, destScript);

                        _serviceLog.Information($"Pending update script {pendingUpdateScript} was found and moved to destination {updateScriptLocation}");
                    }
                    catch
                    {
                        _serviceLog.Warning($"Pending update script {pendingUpdateScript} was found but could not be moved to destination {updateScriptLocation}");
                    }
                }
            }
        }

        /// <summary>
        /// If applicable, perform CA trust store maintenance relevant to our supported set of certificate authorities
        /// </summary>
        private void PerformCAMaintenance()
        {
            if (_useWindowsNativeFeatures)
            {
                try
                {
                    foreach (var ca in _certificateAuthorities.Values)
                    {
                        // check for any intermediate to disable (by thumbprint)
                        if (ca.DisabledIntermediates?.Any() == true)
                        {
                            // check we have disabled usage on all required intermediates
                            foreach (var i in ca.DisabledIntermediates)
                            {
                                try
                                {
                                    // local machine store
                                    CertificateManager.DisableCertificateUsage(i, CertificateManager.CA_STORE_NAME, useMachineStore: true);

                                    // local user store (service user)
                                    CertificateManager.DisableCertificateUsage(i, CertificateManager.CA_STORE_NAME, useMachineStore: false);
                                }
                                catch (Exception ex)
                                {
                                    _serviceLog?.Error(ex, "CA Maintenance: Failed to disable CA certificate usage. {thumb}", i);
                                }

                                try
                                {
                                    // local machine store
                                    if (CertificateManager.MoveCertificate(i, CertificateManager.CA_STORE_NAME, CertificateManager.DISALLOWED_STORE_NAME, useMachineStore: true))
                                    {
                                        _serviceLog?.Information("CA Maintenance: Intermediate CA certificate moved to Disallowed (machine) store. {thumb}", i);
                                    }

                                    if (CertificateManager.MoveCertificate(i, CertificateManager.CA_STORE_NAME, CertificateManager.DISALLOWED_STORE_NAME, useMachineStore: false))
                                    {
                                        _serviceLog?.Information("CA Maintenance: Intermediate CA certificate moved to Disallowed (user) store. {thumb}", i);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _serviceLog?.Error(ex, "CA Maintenance: Failed to move intermediate to Disallowed store. {thumb}", i);
                                }
                            }
                        }

                        // check for any trusted roots to add
                        if (ca.TrustedRoots?.Any() == true)
                        {
                            foreach (var root in ca.TrustedRoots)
                            {
                                if (CertificateManager.GetCertificateByThumbprint(root.Key, CertificateManager.ROOT_STORE_NAME, useMachineStore: true) == null)
                                {
                                    CertificateManager.StoreCertificateFromPem(root.Value, CertificateManager.ROOT_STORE_NAME, useMachineStore: true);
                                }
                            }
                        }

                        // check for any intermediates to add
                        if (ca.Intermediates?.Any() == true)
                        {
                            foreach (var intermediate in ca.Intermediates)
                            {
                                if (CertificateManager.GetCertificateByThumbprint(intermediate.Key, CertificateManager.CA_STORE_NAME, useMachineStore: true) == null)
                                {
                                    CertificateManager.StoreCertificateFromPem(intermediate.Value, CertificateManager.CA_STORE_NAME, useMachineStore: true);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _serviceLog?.Error(ex, "Failed to perform CA maintenance");
                }
            }
        }

        /// <summary>
        /// Perform a subset of diagnostics, report failures if status reporting is enabled.
        /// </summary>
        /// <returns></returns>
        public async Task PerformScheduledDiagnostics()
        {
            try
            {
                _serviceLog.Information("Performing system diagnostics.");

                var diagnosticResults = await PerformServiceDiagnostics();
                if (diagnosticResults.Any(d => d.IsSuccess == false))
                {
                    var reportingEmail = (await GetAccountDetails(null))?.Email;

                    foreach (var d in diagnosticResults.Where(di => di.IsSuccess == false && di.Result != null))
                    {
                        _serviceLog.Warning("Diagnostic Check Failed: " + d.Message);

                        // report diagnostic failures (if enabled)
                        if (reportingEmail != null && CoreAppSettings.Current.EnableStatusReporting && _pluginManager.DashboardClient != null)
                        {

                            try
                            {
                                await _pluginManager.DashboardClient.ReportUserActionRequiredAsync(new Models.Shared.ItemActionRequired
                                {
                                    InstanceId = null,
                                    ManagedItemId = null,
                                    ItemTitle = "Diagnostic Check Failed",
                                    ActionType = "diagnostic:" + d.Result.ToString(),
                                    InstanceTitle = Environment.MachineName,
                                    Message = d.Message,
                                    NotificationEmail = reportingEmail,
                                    AppVersion = Util.GetAppVersion().ToString() + ";" + Environment.OSVersion.ToString()
                                });
                            }
                            catch (Exception)
                            {
                                _serviceLog.Warning("Failed to send diagnostic status report to API.");
                            }
                        }
                    }
                }
                else
                {
                    _serviceLog.Information("Diagnostics - OK.");
                }
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Diagnostics Error");
            }
        }

        /// <summary>
        /// Perform certificate cleanup (files and store) as per cleanup preferences
        /// </summary>
        /// <returns></returns>
        public async Task PerformCertificateCleanup()
        {
            try
            {
                var mode = CoreAppSettings.Current.CertificateCleanupMode;
                if (mode == null)
                {
                    mode = CertificateCleanupMode.AfterExpiry;
                }

                if (mode != CertificateCleanupMode.None)
                {
                    var excludedCertThumprints = new List<string>();

                    // excluded thumbprints are all certs currently tracked as managed certs
                    var managedCerts = await GetManagedCertificates(ManagedCertificateFilter.ALL);

                    foreach (var c in managedCerts)
                    {
                        if (!string.IsNullOrEmpty(c.CertificateThumbprintHash))
                        {
                            excludedCertThumprints.Add(c.CertificateThumbprintHash.ToLower());
                        }
                    }

                    if (mode == CertificateCleanupMode.FullCleanup)
                    {

                        // cleanup old pfx files in asset store(s), if any
                        var assetPath = Path.Combine(EnvironmentUtil.CreateAppDataPath(), "certes", "assets");
                        if (Directory.Exists(assetPath))
                        {
                            var ext = new List<string> { ".pfx" };
                            DeleteOldCertificateFiles(assetPath, ext);
                        }

                        assetPath = Path.Combine(EnvironmentUtil.CreateAppDataPath(), "assets");
                        if (Directory.Exists(assetPath))
                        {
                            var ext = new List<string> { ".pfx", ".key", ".crt", ".pem" };
                            DeleteOldCertificateFiles(assetPath, ext);
                        }
                    }

                    // this will only perform expiry cleanup, as no specific thumbprint provided
                    var certsRemoved = CertificateManager.PerformCertificateStoreCleanup(
                            (CertificateCleanupMode)mode,
                            DateTimeOffset.UtcNow,
                            matchingName: null,
                            excludedThumbprints: excludedCertThumprints,
                            log: _serviceLog,
                            CoreAppSettings.Current.DefaultCertificateStore ?? CertificateManager.DEFAULT_STORE_NAME
                        );
                }
            }
            catch (Exception exp)
            {
                // log exception
                _serviceLog?.Error("Failed to perform certificate cleanup: " + exp.ToString());
            }
        }

        /// <summary>
        /// Perform cleanup of old certificate asset files
        /// </summary>
        /// <param name="assetPath"></param>
        /// <param name="ext"></param>
        private static void DeleteOldCertificateFiles(string assetPath, List<string> ext)
        {
            // performs a simple delete of certificate files under the assets path where the file creation time is more than 1 year ago

            var allFiles = Directory.GetFiles(assetPath, "*.*", SearchOption.AllDirectories)
                 .Where(s => ext.Contains(Path.GetExtension(s)));

            foreach (var f in allFiles)
            {
                try
                {
                    var createdAt = System.IO.File.GetCreationTime(f);
                    if (createdAt < DateTimeOffset.UtcNow.AddMonths(-12))
                    {
                        //remove old file
                        System.IO.File.Delete(f);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Perform basic service diagnostics to check host machine configuration
        /// </summary>
        /// <returns></returns>
        public async Task<List<ActionResult>> PerformServiceDiagnostics()
        {
            return await Certify.Management.Util.PerformAppDiagnostics(includeTempFileCheck: true, ntpServer: CoreAppSettings.Current.NtpServer);
        }
    }
}
