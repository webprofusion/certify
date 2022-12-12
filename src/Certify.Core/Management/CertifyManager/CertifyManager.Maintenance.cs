using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Newtonsoft.Json;

namespace Certify.Management
{
    public partial class CertifyManager : ICertifyManager, IDisposable
    {

        /// <summary>
        /// When called, perform daily cache cleanup, cert cleanup, diagnostics and maintenance
        /// </summary>
        /// <returns></returns>
        public async Task<bool> PerformDailyTasks()
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

                PerformCAMaintenance();

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

        public async Task<List<ActionResult>> PerformCertificateMaintenance()
        {
            var steps = new List<ActionResult>();

            await PerformCertificateStatusChecks(false);
            steps.Add(new ActionResult { Message = "Performed OCSP Checks" });

            return steps;
        }
        /// <summary>
        /// When called, perform OCSP checks and ACME Renewal Info (ARI) checks on all managed certs or a subsample
        /// </summary>
        private async Task PerformCertificateStatusChecks(bool checkAll)
        {

            var itemsToRenew = new List<ManagedCertificate>();

            // perform OCSP checks for every active cert, possibly once per day. If revoked, immediately renew.
            // perform ARI RenewalInfo checks (where supported by the CA), possibly once per day, if suggested renewal much less than planned renewal then set planned renewal date in window or immediate
            _serviceLog.Information("Performing OCSP status checks");

            var batchSize = 100;
            var ocspItemsToCheck = await _itemManager.Find(new ManagedCertificateFilter { LastOCSPCheckHrs = 3, MaxResults = batchSize });
            if (ocspItemsToCheck?.Any() == true)
            {
                _serviceLog.Information($"Checking OCSP for {ocspItemsToCheck.Count} items");

                foreach (var item in ocspItemsToCheck)
                {
                    bool itemRenewalRequired = false;

                    if (!string.IsNullOrEmpty(item.CertificatePath) && File.Exists(item.CertificatePath))
                    {
                        // perform OCSP check
                        _serviceLog.Verbose($"Checking {item.Name} : {item.Id} ");

                        var ocspCheck = await CertificateManager.CheckOcspRevokedStatus(item.CertificatePath, await GetPfxPassword(item));

                        if (ocspCheck == Models.Certify.Models.CertificateStatusType.Revoked || ocspCheck == Models.Certify.Models.CertificateStatusType.Expired)
                        {
                            // this item requires a renewal attempt
                            _serviceLog.Verbose($"Item {item.Name} failed the OCSP check [{ocspCheck}] and will be queued for a renewal attempt");
                            itemsToRenew.Add(item);
                        }
                        else if (ocspCheck == Models.Certify.Models.CertificateStatusType.Unknown)
                        {
                            _serviceLog.Verbose($"Item {item.Name} failed the OCSP check [{ocspCheck}] and will be queued for a renewal attempt");
                        }
                    }

                    //TODO: update last OCSP check date
                }
            }

            _serviceLog.Information("Completed OCSP status checks");

            var renewalInfoItemsToCheck = await _itemManager.Find(new ManagedCertificateFilter { LastRenewalInfoCheckHrs = 3, MaxResults = batchSize });
            if (renewalInfoItemsToCheck?.Any() == true)
            {
                _serviceLog.Information("Performing Renewal Info checks");

                foreach (var item in renewalInfoItemsToCheck)
                {
                    try
                    {
                        var provider = await GetACMEProvider(item);
                        var directoryInfo = await provider?.GetAcmeDirectory();

                        if (provider != null && directoryInfo?.RenewalInfo != null)
                        {
                            if (item.CertificatePath != null)
                            {

                                _serviceLog.Verbose($"Checking renewal info for {item.Name}");

                                var certId = Certify.Shared.Core.Utils.PKI.CertUtils.GetCertIdBase64(File.ReadAllBytes(item.CertificatePath), await GetPfxPassword(item));
                                var info = await provider.GetRenewalInfo(certId);

                                if (info != null && item.DateExpiry != null)
                                {

                                    var nextRenewal = new DateTimeOffset((DateTime)item.DateExpiry);
                                    if (info.SuggestedWindow?.Start < nextRenewal)
                                    {
                                        itemsToRenew.Add(item);
                                    }
                                }
                            }
                            // TODO: update last renewl info check date
                        }
                    }
                    catch (Exception ex)
                    {
                        _serviceLog.Debug("Could not check item renewal info [itemName] : {exp}", item.Name, ex);
                    }
                }
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
                    var reportingEmail = (await GetAccountDetailsForManagedItem(null))?.Email;

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
                        var assetPath = Path.Combine(EnvironmentUtil.GetAppDataFolder(), "certes", "assets");
                        if (Directory.Exists(assetPath))
                        {
                            var ext = new List<string> { ".pfx" };
                            DeleteOldCertificateFiles(assetPath, ext);
                        }

                        assetPath = Path.Combine(EnvironmentUtil.GetAppDataFolder(), "assets");
                        if (Directory.Exists(assetPath))
                        {
                            var ext = new List<string> { ".pfx", ".key", ".crt", ".pem" };
                            DeleteOldCertificateFiles(assetPath, ext);
                        }
                    }

                    // this will only perform expiry cleanup, as no specific thumbprint provided
                    var certsRemoved = CertificateManager.PerformCertificateStoreCleanup(
                            (CertificateCleanupMode)mode,
                            DateTime.Now,
                            matchingName: null,
                            excludedThumbprints: excludedCertThumprints,
                            log: _serviceLog
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
                    if (createdAt < DateTime.Now.AddMonths(-12))
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
