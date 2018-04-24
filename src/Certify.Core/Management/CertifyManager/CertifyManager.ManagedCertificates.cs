using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        public event Action<ManagedCertificate> OnManagedCertificateUpdated;

        public async Task<ManagedCertificate> GetManagedCertificate(string id)
        {
            return await _siteManager.GetManagedCertificate(id);
        }

        public async Task<List<ManagedCertificate>> GetManagedCertificates(ManagedCertificateFilter filter = null)
        {
            return await this._siteManager.GetManagedCertificates(filter, true);
        }

        public async Task<ManagedCertificate> UpdateManagedCertificate(ManagedCertificate site)
        {
            site = await _siteManager.UpdatedManagedCertificate(site);

            // report request state to status hub clients
            OnManagedCertificateUpdated?.Invoke(site);
            return site;
        }

        private async Task UpdateManagedCertificateStatus(ManagedCertificate managedCertificate, RequestState status,
            string msg = null)
        {
            managedCertificate.DateLastRenewalAttempt = DateTime.UtcNow;

            if (status == RequestState.Success)
            {
                managedCertificate.RenewalFailureCount = 0;
                managedCertificate.LastRenewalStatus = RequestState.Success;
            }
            else
            {
                managedCertificate.RenewalFailureMessage = msg;
                managedCertificate.RenewalFailureCount++;
                managedCertificate.LastRenewalStatus = RequestState.Error;
            }

            managedCertificate = await _siteManager.UpdatedManagedCertificate(managedCertificate);

            // report request state to status hub clients
            OnManagedCertificateUpdated?.Invoke(managedCertificate);

            //if reporting enabled, send report
            if (managedCertificate.RequestConfig?.EnableFailureNotifications == true)
            {
                await ReportManagedCertificateStatus(managedCertificate);
            }

            if (_tc != null) _tc.TrackEvent("UpdateManagedCertificatesStatus_" + status.ToString());
        }

        private async Task ReportManagedCertificateStatus(ManagedCertificate managedCertificate)
        {
            if (this._pluginManager != null && this._pluginManager.DashboardClient != null)
            {
                var report = new Models.Shared.RenewalStatusReport
                {
                    InstanceId = CoreAppSettings.Current.InstanceId,
                    MachineName = Environment.MachineName,
                    PrimaryContactEmail = GetPrimaryContactEmail(),
                    ManagedSite = managedCertificate,
                    AppVersion = new Management.Util().GetAppVersion().ToString()
                };
                try
                {
                    await this._pluginManager.DashboardClient.ReportRenewalStatusAsync(report);
                }
                catch (Exception)
                {
                    // failed to report status
                    LogMessage(managedCertificate.Id, "Failed to send renewal status report.",
                        LogItemType.GeneralWarning);
                }
            }
        }

        public async Task DeleteManagedCertificate(string id)
        {
            var site = await _siteManager.GetManagedCertificate(id);
            if (site != null)
            {
                await this._siteManager.DeleteManagedCertificate(site);
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
        /// <returns></returns>
        public async Task<List<StatusMessage>> TestChallenge(ILog log, ManagedCertificate managedCertificate,
            bool isPreviewMode, IProgress<RequestProgressState> progress = null)
        {
            var results = await _challengeDiagnostics.TestChallengeResponse(log, _serverProvider, managedCertificate,
                isPreviewMode, CoreAppSettings.Current.EnableDNSValidationChecks, progress);

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

            return results;
        }

        private async Task<bool> IsManagedCertificateRunning(string id, ICertifiedServer iis = null)
        {
            var managedCertificate = await _siteManager.GetManagedCertificate(id);
            if (managedCertificate != null)
            {
                if (iis == null) iis = _serverProvider;
                return await iis.IsSiteRunning(managedCertificate.GroupId);
            }
            else
            {
                //site not identified, assume it is running
                return true;
            }
        }

        public async Task<List<ActionStep>> GeneratePreview(ManagedCertificate item)
        {
            return await new PreviewManager().GeneratePreview(item, _serverProvider, this);
        }
    }
}
