using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        public async Task<ManagedCertificate> GetManagedCertificate(string id) => await _itemManager.GetById(id);

        public async Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter = null)
        {
            var list = await _itemManager.GetAll(filter);


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
                                if (cp.IsEnabled)
                                {
                                    var certManager = p.GetProvider(pluginType, cp.Id);
                                    var certs = await certManager.GetManagedCertificates(filter);

                                    list.AddRange(certs);
                                }
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to create provider from plugin [Certificate Manager] ");
                        }
                    }
                }
            }

            return list;
        }

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

        private async Task UpdateManagedCertificateStatus(ManagedCertificate managedCertificate, RequestState status,
            string msg = null)
        {
            managedCertificate.DateLastRenewalAttempt = DateTime.UtcNow;

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
                managedCertificate.RenewalFailureMessage = msg;
                managedCertificate.RenewalFailureCount++;
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
                managedCertificate.RenewalFailureMessage = "Error: cannot store certificate status update to %ProgramData%\\Certify\\manageditems.db. Check there is enough disk space and permission for writes. If this problem persists, contact support. " + exp;
            }

            // report request state to status hub clients
            _statusReporting?.ReportManagedCertificateUpdated(managedCertificate);

            // if reporting api enabled, send report

            if (managedCertificate.RequestConfig?.EnableFailureNotifications == true)
            {
                await ReportManagedCertificateStatus(managedCertificate);
            }

            if (_tc != null)
            {
                _tc.TrackEvent("UpdateManagedCertificatesStatus_" + status.ToString());
            }
        }

        private async Task ReportManagedCertificateStatus(ManagedCertificate managedCertificate)
        {
            if (CoreAppSettings.Current.EnableStatusReporting)
            {
                if (_pluginManager != null && _pluginManager.DashboardClient != null)
                {
                    var reportedCert = Newtonsoft.Json.JsonConvert.DeserializeObject<ManagedCertificate>(Newtonsoft.Json.JsonConvert.SerializeObject(managedCertificate));

                    // remove anything we don't want to report to the dashboard

                    reportedCert.RequestConfig.CustomCSR = null;
                    reportedCert.RequestConfig.CustomPrivateKey = null;

                    var report = new Models.Shared.RenewalStatusReport
                    {
                        InstanceId = CoreAppSettings.Current.InstanceId,
                        MachineName = Environment.MachineName,
                        PrimaryContactEmail = (await GetAccountDetailsForManagedItem(managedCertificate))?.Email,
                        ManagedSite = reportedCert,
                        AppVersion = Util.GetAppVersion().ToString()
                    };
                    try
                    {
                        await _pluginManager.DashboardClient.ReportRenewalStatusAsync(report);
                    }
                    catch (Exception)
                    {
                        // failed to report status
                        LogMessage(managedCertificate.Id, "Failed to send renewal status report.",
                            LogItemType.GeneralWarning);
                    }
                }
            }
        }

        public async Task DeleteManagedCertificate(string id)
        {
            var site = await _itemManager.GetById(id);
            if (site != null)
            {
                await _itemManager.Delete(site);
            }
        }

        private ProcessStartInfo _httpChallengeProcessInfo;
        private Process _httpChallengeProcess;
        private string _httpChallengeControlKey = Guid.NewGuid().ToString();
        private string _httpChallengeCheckKey = "configcheck";
        private System.Net.Http.HttpClient _httpChallengeServerClient = new System.Net.Http.HttpClient();
        private int _httpChallengePort = 80;

        private async Task<bool> IsHttpChallengeProcessStarted()
        {
            if (_httpChallengeServerClient != null)
            {
                var testUrl = $"http://127.0.0.1:{_httpChallengePort}/.well-known/acme-challenge/{_httpChallengeCheckKey}";

                try
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

        private async Task<bool> StartHttpChallengeServer()
        {
            if (!await IsHttpChallengeProcessStarted())
            {
                var cliPath = $"{AppContext.BaseDirectory}certify.exe";
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

                return await IsHttpChallengeProcessStarted();
            }
            else
            {
                await StopHttpChallengeServer();
                return false;
            }
        }

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

            if (managedCertificate.RequestConfig.PerformAutoConfig && managedCertificate.GetChallengeConfig(null).ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
            {
                var serverCheck = await _serverProvider.RunConfigurationDiagnostics(managedCertificate.ServerSiteId);
                results.AddRange(serverCheck.ConvertAll(x => new StatusMessage { IsOK = !x.HasError, HasWarning = x.HasWarning, Message = x.Description }));
            }

            if (managedCertificate.GetChallengeConfig(null).ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
            {
                if (CoreAppSettings.Current.EnableHttpChallengeServer)
                {
                    _httpChallengeServerAvailable = await StartHttpChallengeServer();

                    if (_httpChallengeServerAvailable)
                    {
                        results.Add(new StatusMessage { IsOK = true, Message = "Http Challenge Server process available." });
                    }
                    else
                    {
                        results.Add(new StatusMessage { IsOK = true, HasWarning = true, Message = "Built-in Http Challenge Server process unavailable or could not start. Challenge responses will fall back to IIS." });
                    }
                }
            }

            results.AddRange(
            await _challengeDiagnostics.TestChallengeResponse(
                    log,
                    _serverProvider,
                    managedCertificate,
                    isPreviewMode,
                    CoreAppSettings.Current.EnableDNSValidationChecks,
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

            if (CoreAppSettings.Current.EnableHttpChallengeServer)
            {
                await StopHttpChallengeServer();
            }

            return results;
        }

        private async Task<bool> IsManagedCertificateRunning(string id, ICertifiedServer iis = null)
        {
            var managedCertificate = await _itemManager.GetById(id);
            if (managedCertificate != null)
            {
                if (iis == null)
                {
                    iis = _serverProvider;
                }

                try
                {
                    return await iis.IsSiteRunning(managedCertificate.GroupId);
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

        public async Task<List<ActionStep>> GeneratePreview(ManagedCertificate item) => await new PreviewManager().GeneratePreview(item, _serverProvider, this, _credentialsManager);

        public async Task<List<DnsZone>> GetDnsProviderZones(string providerTypeId, string credentialsId)
        {

            var dnsHelper = new Core.Management.Challenges.DnsChallengeHelper(_credentialsManager);
            var result = await dnsHelper.GetDnsProvider(providerTypeId, credentialsId, null, _credentialsManager);

            if (result.Provider != null)
            {
                return await result.Provider.GetZones();
            }
            else
            {
                return new List<DnsZone>();
            }
        }

        public async Task<string[]> GetItemLog(string id, int limit)
        {
            var logPath = ManagedCertificateLog.GetLogPath(id);

            if (System.IO.File.Exists(logPath))
            {
                try
                {
                    // TODO: use reverse stream reader for large files

                    var log = System.IO.File.ReadAllLines(logPath)
                        .Reverse()
                        .Take(limit)
                        .Reverse()
                        .ToArray();

                    return log;
                }
                catch (Exception exp)
                {
                    return new string[] { $"Failed to read log: {exp}" };
                }
            }
            else
            {
                return await Task.FromResult(new string[] { "" });
            }
        }

    }
}
