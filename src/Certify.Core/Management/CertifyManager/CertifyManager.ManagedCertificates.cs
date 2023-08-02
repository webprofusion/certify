using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.API;
using Certify.Models.Providers;
using Certify.Models.Shared;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        /// <summary>
        /// Get managed certificate details by ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<ManagedCertificate> GetManagedCertificate(string id) => await _itemManager.GetById(id);

        /// <summary>
        /// Get list of managed certificates based on then given filter criteria
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public async Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter)
        {
            var list = await _itemManager.Find(filter);

            if (filter?.IncludeExternal == true)
            {
                if (_pluginManager?.CertificateManagerProviders?.Any() == true)
                {
                    // TODO: cache providers/results
                    // check if we have any external sources of managed certificates
                    foreach (var p in _pluginManager.CertificateManagerProviders)
                    {
                        if (p != null)
                        {
                            var pluginType = p.GetType();
                            var providers = p.GetProviders(pluginType);
                            foreach (var cp in providers)
                            {
                                try
                                {
                                    if (cp.IsEnabled)
                                    {
                                        var certManager = p.GetProvider(pluginType, cp.Id);
                                        var certs = await certManager.GetManagedCertificates(filter);

                                        list.AddRange(certs);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _serviceLog?.Error($"Failed to query certificate manager plugin {cp.Title} {ex}");
                                }
                            }
                        }
                        else
                        {
                            _serviceLog?.Error($"Failed to create one or more certificate manager plugins");
                        }
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Get list of managed certificates based on then given filter criteria, as search result with total count
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public async Task<ManagedCertificateSearchResult> GetManagedCertificateResults(ManagedCertificateFilter filter)
        {
            var result = new ManagedCertificateSearchResult();

            var list = await _itemManager.Find(filter);
            if (filter.PageSize > 0)
            {
                // TODO: implement count on provider directly
                filter.PageSize = null;
                filter.PageIndex = null;
                var all = await _itemManager.Find(filter);
                result.TotalResults = all.Count;
            }

            result.Results = list;

            return result;
        }
        /// <summary>
        /// Update the stored details for the given managed certificate and report update to client(s)
        /// </summary>
        /// <param name="managedCert"></param>
        /// <returns></returns>
        public async Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate managedCert)
        {
            // migrate item settings as source can include legacy settings (e.g. CSV import) - TODO: remove when legacy sources no longer supported
            managedCert = MigrateManagedCertificateSettings(managedCert);

            // store managed cert in database store
            managedCert = await _itemManager.Update(managedCert);

            // report request state to status hub clients
            _statusReporting?.ReportManagedCertificateUpdated(managedCert);

            return managedCert;
        }

        /// <summary>
        /// After a renewal attempt, update the "final" stored details and status for the given managed certificate and report update to client(s)
        /// </summary>
        /// <param name="managedCert"></param>
        /// <returns></returns>
        private async Task UpdateManagedCertificateStatus(ManagedCertificate managedCertificate, RequestState status,
            string msg = null, int? failureCount = null)
        {
            managedCertificate.DateLastRenewalAttempt = DateTimeOffset.UtcNow;

            if (status == RequestState.Success)
            {
                managedCertificate.RenewalFailureCount = 0;
                managedCertificate.LastRenewalStatus = RequestState.Success;
                managedCertificate.RenewalFailureMessage = "";
            }
            else if (status == RequestState.Paused)
            {
                managedCertificate.RenewalFailureCount = 0;
                managedCertificate.LastRenewalStatus = RequestState.Paused;
                managedCertificate.RenewalFailureMessage = msg;
            }
            else
            {
                if (failureCount > managedCertificate.RenewalFailureCount)
                {
                    managedCertificate.RenewalFailureCount = ((int)failureCount) + 1;
                }
                else
                {
                    managedCertificate.RenewalFailureCount++;
                }

                managedCertificate.RenewalFailureMessage = msg;

                managedCertificate.LastRenewalStatus = RequestState.Error;
            }

            try
            {
                managedCertificate = await _itemManager.Update(managedCertificate);
            }
            catch (Exception exp)
            {

                // failed to store update, e.g. database problem or disk space has run out
                managedCertificate.LastRenewalStatus = RequestState.Error;
                managedCertificate.RenewalFailureMessage = "Error: Cannot store certificate status update to the Data Store. Check there is enough disk space and permission for database writes. If this problem persists, contact support. " + exp;
            }

            // report request state to status hub clients
            _statusReporting?.ReportManagedCertificateUpdated(managedCertificate);

            // if reporting api enabled, send report

            if (managedCertificate.RequestConfig?.EnableFailureNotifications == true && CoreAppSettings.Current.EnableStatusReporting)
            {
                await ReportManagedCertificateStatus(managedCertificate);
            }

            _tc?.TrackEvent("UpdateManagedCertificatesStatus_" + status.ToString());
        }

        private ConcurrentDictionary<string, RenewalStatusReport> _statusReportQueue { get; set; } = new ConcurrentDictionary<string, RenewalStatusReport>();
        private bool _useStatusReportQueue { get; set; } = true;
        /// <summary>
        /// Optionally send current managed certificate status to the reporting dashboard
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        private async Task ReportManagedCertificateStatus(ManagedCertificate managedCertificate)
        {

            var reportedCert = Newtonsoft.Json.JsonConvert.DeserializeObject<ManagedCertificate>(Newtonsoft.Json.JsonConvert.SerializeObject(managedCertificate));

            // remove anything we don't want to report to the dashboard

            reportedCert.RequestConfig.CustomCSR = null;
            reportedCert.RequestConfig.CustomPrivateKey = null;

            reportedCert.RequestConfig.Challenges
                .Where(c => c.ChallengeProvider == "DNS01.API.CertifyDns")
                .Select(s => s.Parameters
                    .Where(p => p.Key == "credentials_json").Select(p => p.Value = null));

            var report = new Models.Shared.RenewalStatusReport
            {
                InstanceId = CoreAppSettings.Current.InstanceId,
                MachineName = Environment.MachineName,
                PrimaryContactEmail = (await GetAccountDetails(managedCertificate, allowFailover: false))?.Email,
                ManagedSite = reportedCert,
                AppVersion = Util.GetAppVersion().ToString()
            };

            if (!_useStatusReportQueue)
            {
                await SendStatusReport(report);
            }
            else
            {
                _statusReportQueue.AddOrUpdate(managedCertificate.Id, report, (k, v) => report);
            }
        }

        private async Task SendStatusReport(RenewalStatusReport report)
        {
            if (CoreAppSettings.Current.EnableStatusReporting && _pluginManager?.DashboardClient != null)
            {
                try
                {
                    await _pluginManager.DashboardClient.ReportRenewalStatusAsync(report);
                }
                catch (Exception)
                {
                    // failed to report status
                    LogMessage(report?.ManagedSite?.Id, "Failed to send renewal status report.",
                        LogItemType.GeneralWarning);
                }
            }
        }

        private async Task SendQueuedStatusReports()
        {
            try
            {
                if (_statusReportQueue.Any())
                {
                    var itemsSent = 0;
                    foreach (var k in _statusReportQueue.Keys)
                    {
                        if (_statusReportQueue.TryRemove(k, out var report))
                        {
                            await SendStatusReport(report);
                            itemsSent++;
                        }
                    }

                    if (itemsSent > 0)
                    {
                        _serviceLog.Information($"Sent {itemsSent} queued status reports.");
                    }
                }
            }
            catch (Exception exp)
            {
                _serviceLog.Error(exp, "Failed to send queued status reports.");
            }
        }

        /// <summary>
        /// Delete a given managed certificate
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task DeleteManagedCertificate(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                var item = await _itemManager.GetById(id);
                if (item != null)
                {
                    await _itemManager.Delete(item);
                }
            }
        }

        /// <summary>
        /// Perform set of test challenges and configuration checks to determine if site appears
        /// valid for certificate requests
        /// </summary>
        /// <param name="managedCertificate"> managed site to check </param>
        /// <param name="isPreviewMode"> 
        /// If true, perform full set of checks (DNS etc), if false performs minimal/basic checks
        /// </param>
        /// <returns>  </returns>
        public async Task<List<StatusMessage>> TestChallenge(ILog log, ManagedCertificate managedCertificate,
            bool isPreviewMode, IProgress<RequestProgressState> progress = null)
        {
            var results = new List<StatusMessage>();

            if (managedCertificate.RequestConfig.AuthorityTokens?.Any() == true)
            {
                ReportProgress(progress,
                   new RequestProgressState(RequestState.Success, "All Tests Completed OK", managedCertificate,
                       isPreviewMode));
                return results;
            }

            var serverProvider = GetTargetServerProvider(managedCertificate);

            if (managedCertificate.RequestConfig.PerformAutoConfig && managedCertificate.GetChallengeConfig(null).ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
            {
                var serverCheck = await serverProvider.RunConfigurationDiagnostics(managedCertificate.ServerSiteId);
                results.AddRange(serverCheck.ConvertAll(x => new StatusMessage { IsOK = !x.HasError, HasWarning = x.HasWarning, Message = x.Description }));
            }

            var httpChallengeServerActive = false;
            if (managedCertificate.GetChallengeConfig(null).ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
            {
                if (CoreAppSettings.Current.EnableHttpChallengeServer)
                {
                    _httpChallengeServerAvailable = await StartHttpChallengeServer();

                    if (_httpChallengeServerAvailable)
                    {
                        results.Add(new StatusMessage { IsOK = true, Message = "Http Challenge Server process available." });

                        httpChallengeServerActive = true;
                    }
                    else
                    {
                        results.Add(new StatusMessage { IsOK = true, HasWarning = true, Message = "Built-in Http Challenge Server process unavailable or could not start. Challenge responses will fall back to the default web server process (if available)." });
                    }
                }
            }

            results.AddRange(
            await _challengeResponseService.TestChallengeResponse(
                    log,
                    serverProvider,
                    managedCertificate,
                    isPreviewMode,
                    CoreAppSettings.Current.EnableDNSValidationChecks,
                    performCleanupOnly: false,
                    _credentialsManager,
                    progress
                )
             );

            if (progress != null)
            {
                if (results.Any(r => r.IsOK == false))
                {
                    ReportProgress(progress,
                        new RequestProgressState(RequestState.Error, "One or more tests failed", managedCertificate,
                            isPreviewMode));
                }
                else
                {
                    ReportProgress(progress,
                        new RequestProgressState(RequestState.Success, "All Tests Completed OK", managedCertificate,
                            isPreviewMode));
                }
            }

            if (httpChallengeServerActive)
            {
                await StopHttpChallengeServer();
            }

            return results;
        }

        /// <summary>
        /// Perform a forced cleanup of challenges where possible. This is particularly useful for cleanup of stray DNS challenge TXT records.
        /// </summary>
        /// <param name="log">Log to use</param>
        /// <param name="managedCertificate"> managed site to perform cleanup for </param>
        /// <param name="progress">Progress tracker</param>
        /// <returns>  </returns>
        public async Task<List<StatusMessage>> PerformChallengeCleanup(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null)
        {
            var results = new List<StatusMessage>();

            var serverProvider = GetTargetServerProvider(managedCertificate);

            results.AddRange(
               await _challengeResponseService.TestChallengeResponse(
                   log,
                   serverProvider,
                   managedCertificate,
                   isPreviewMode: false,
                   enableDnsChecks: false,
                   performCleanupOnly: true,
                   credentialsManager: _credentialsManager,
                   progress
               )
           );

            if (progress != null)
            {
                if (results.Any(r => r.IsOK == false))
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Error, "One or more challenge cleanup operations failed", managedCertificate, isPreviewMode: false));
                }
                else
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Success, "Challenge cleanup operations completed", managedCertificate, isPreviewMode: false));
                }
            }

            return results;
        }

        private async Task<bool> IsManagedCertificateRunning(string id)
        {
            var managedCertificate = await _itemManager.GetById(id);
            if (managedCertificate != null)
            {
                var serverProvider = GetTargetServerProvider(managedCertificate);
                try
                {
                    return await serverProvider.IsSiteRunning(managedCertificate.GroupId);
                }
                catch
                {
                    // by default we assume the site is running
                    return true;
                }
            }
            else
            {
                //site not identified, assume it is running
                return true;
            }
        }

        public async Task<List<ActionStep>> GeneratePreview(ManagedCertificate item)
        {
            var serverProvider = GetTargetServerProvider(item);
            return await new PreviewManager().GeneratePreview(item, serverProvider, this, _credentialsManager);
        }

        public async Task<List<DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId)
        {

            var dnsHelper = new Core.Management.Challenges.DnsChallengeHelper(_credentialsManager);

            var result = await dnsHelper.GetDnsProvider(providerTypeId, credentialsId, null, _credentialsManager, _serviceLog);

            if (result.Provider != null)
            {
                return await result.Provider.GetZones();
            }
            else
            {
                return new List<DnsZone>();
            }
        }

        public async Task<LogItem[]> GetItemLog(string id, int limit)
        {
            var logPath = ManagedCertificateLog.GetLogPath(id);

            if (!string.IsNullOrEmpty(logPath) && System.IO.File.Exists(logPath))
            {
                try
                {
                    // TODO: use reverse stream reader for large files

                    var log = System.IO.File.ReadAllLines(logPath)
                        .Reverse()
                        .Take(limit)
                        .ToArray();
                    var parsed = LogParser.Parse(log);
                    return parsed;
                }
                catch (Exception exp)
                {
                    return new LogItem[] { new LogItem { LogLevel = "ERR", EventDate = DateTime.Now, Message = $"Failed to read log: {exp}" } };
                }
            }
            else
            {
                return await Task.FromResult(Array.Empty<LogItem>());
            }
        }

        /// <summary>
        /// Perform any one-time migrations of legacy managed certificate settings and deployment tasks etc
        /// </summary>
        /// <returns></returns>
        private async Task PerformManagedCertificateMigrations()
        {

            IEnumerable<ManagedCertificate> list = await GetManagedCertificates(ManagedCertificateFilter.ALL);

            list = list.Where(i => !string.IsNullOrEmpty(i.RequestConfig.WebhookUrl) || !string.IsNullOrEmpty(i.RequestConfig.PreRequestPowerShellScript) || !string.IsNullOrEmpty(i.RequestConfig.PostRequestPowerShellScript)
            || i.PostRequestTasks?.Any(t => t.TaskTypeId == StandardTaskTypes.POWERSHELL && t.Parameters?.Any(p => p.Key == "url") == true) == true);

            foreach (var i in list)
            {
                var result = MigrateDeploymentTasks(i);
                if (result.Item2 == true)
                {
                    // save change
                    await UpdateManagedCertificate(result.Item1);
                }
            }
        }

        /// <summary>
        /// If required, migrate legacy setting for this managed certicate related to pre/post deployment tasks
        /// </summary>
        /// <param name="managedCert">The source managed certificate to be migrated</param>
        /// <returns>The updated managed certificate to be stored</returns>
        private ManagedCertificate MigrateManagedCertificateSettings(ManagedCertificate managedCert)
        {
            if (
                !string.IsNullOrEmpty(managedCert.RequestConfig.WebhookUrl)
                || !string.IsNullOrEmpty(managedCert.RequestConfig.PreRequestPowerShellScript)
                || !string.IsNullOrEmpty(managedCert.RequestConfig.PostRequestPowerShellScript)
                || managedCert.PostRequestTasks?.Any(t => t.TaskTypeId == StandardTaskTypes.POWERSHELL && t.Parameters?.Any(p => p.Key == "url") == true) == true)
            {
                var result = MigrateDeploymentTasks(managedCert);
                if (result.Item2 == true)
                {
                    return result.Item1;
                }
                else
                {
                    return managedCert;
                }
            }
            else
            {
                return managedCert;
            }
        }

        /* http challenge response controls */

        /// <summary>
        /// process information for temporary http challenge response service
        /// </summary>
        private ProcessStartInfo _httpChallengeProcessInfo;
        private Process _httpChallengeProcess;
        private string _httpChallengeControlKey = Guid.NewGuid().ToString();
        private string _httpChallengeCheckKey = "configcheck";
        private System.Net.Http.HttpClient _httpChallengeServerClient = new System.Net.Http.HttpClient();
        private int _httpChallengePort = 80;

        /// <summary>
        /// Check if our temporary http challenge response service is running locally
        /// </summary>
        /// <returns></returns>
        private async Task<bool> IsHttpChallengeProcessStarted(bool allowRetry = false)
        {
            if (_httpChallengeServerClient != null)
            {
                var testUrl = $"http://127.0.0.1:{_httpChallengePort}/.well-known/acme-challenge/{_httpChallengeCheckKey}";

                try
                {
                    var attempts = 3;
                    while (attempts > 0)
                    {
                        var response = await _httpChallengeServerClient.GetAsync(testUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var status = await _httpChallengeServerClient.GetStringAsync(testUrl);

                            if (status == "OK")
                            {
                                return true;
                            }
                        }

                        attempts--;
                        await Task.Delay(1000);
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Start our temporary http challenge response server process
        /// </summary>
        /// <returns></returns>
        private async Task<bool> StartHttpChallengeServer()
        {
            if (!await IsHttpChallengeProcessStarted(true))
            {
                _tc?.TrackEvent("ChallengeResponse_HttpChallengeServer_Start");

                var cliPath = System.IO.Path.Combine(AppContext.BaseDirectory, "certify.exe");
                _httpChallengeProcessInfo = new ProcessStartInfo(cliPath, $"httpchallenge keys={_httpChallengeControlKey},{_httpChallengeCheckKey}")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,

                    WorkingDirectory = AppContext.BaseDirectory
                };

                try
                {
                    _httpChallengeProcess = new Process { StartInfo = _httpChallengeProcessInfo };
                    _httpChallengeProcess.Start();
                    await Task.Delay(1000);
                }
                catch (Exception)
                {
                    // failed to start process
                    _httpChallengeProcess = null;
                    return false;
                }

                if (_httpChallengeServerClient == null)
                {
                    _httpChallengeServerClient = new System.Net.Http.HttpClient();
                    _httpChallengeServerClient.DefaultRequestHeaders.Add("User-Agent", Util.GetUserAgent() + " CertifyManager");
                }

                return await IsHttpChallengeProcessStarted(true);
            }
            else
            {
                await StopHttpChallengeServer();
                return false;
            }
        }

        /// <summary>
        /// Stop our temporary http challenge response service
        /// </summary>
        /// <returns></returns>
        private async Task<bool> StopHttpChallengeServer()
        {
            if (_httpChallengeServerClient != null)
            {
                try
                {
                    var response = await _httpChallengeServerClient.GetAsync($"http://127.0.0.1:{_httpChallengePort}/.well-known/acme-challenge/{_httpChallengeControlKey}");
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    else
                    {
                        try
                        {
                            if (_httpChallengeProcess != null && !_httpChallengeProcess.HasExited)
                            {
                                _httpChallengeProcess.CloseMainWindow();
                            }
                        }
                        catch { }
                    }
                }
                catch
                {
                    return true;
                }
            }

            return true;
        }
    }
}
