using Certify.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/managedsites")]
    public class ManagedSitesController : Controllers.ControllerBase
    {
        private Management.ICertifyManager _certifyManager = null;

        public ManagedSitesController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        // Get List of Top N Managed Sites, filtered by title
        [HttpPost, Route("search")]
        public async Task<List<ManagedSite>> Search(ManagedSiteFilter filter)
        {
            DebugLog();

            return await _certifyManager.GetManagedSites(filter);
        }

        [HttpGet, Route("{id}")]
        public async Task<ManagedSite> GetById(string id)
        {
            DebugLog(id);

            return await _certifyManager.GetManagedSite(id);
        }

        //add or update managed site
        [HttpPost, Route("update")]
        public async Task<ManagedSite> Update(ManagedSite site)
        {
            DebugLog();

            return await _certifyManager.UpdateManagedSite(site);
        }

        [HttpDelete, Route("delete/{managedSiteId}")]
        public async Task<bool> Delete(string managedSiteId)
        {
            DebugLog();

            await _certifyManager.DeleteManagedSite(managedSiteId);

            return true;
        }

        [HttpPost, Route("testconfig")]
        public async Task<StatusMessage> TestChallengeResponse(ManagedSite site)
        {
            DebugLog();

            return await _certifyManager.TestChallenge(site, isPreviewMode: true);
        }

        /// <summary>
        /// Begin auto renew process and return list of included sites 
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("autorenew")]
        public async Task<List<CertificateRequestResult>> BeginAutoRenewal()
        {
            DebugLog();

            return await _certifyManager.PerformRenewalAllManagedSites(true, null);
        }

        [HttpGet, Route("renewcert/{managedSiteId}")]
        public async Task<CertificateRequestResult> BeginCertificateRequest(string managedSiteId)
        {
            DebugLog();

            var managedSite = await _certifyManager.GetManagedSite(managedSiteId);

            RequestProgressState progressState = new RequestProgressState(RequestState.Running, "Starting..", managedSite);

            var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

            //begin monitoring progress
            _certifyManager.BeginTrackingProgress(progressState);

            //begin request
            var result = await _certifyManager.PerformCertificateRequest(managedSite, progressIndicator);
            return result;
        }

        [HttpGet, Route("requeststatus/{managedSiteId}")]
        public RequestProgressState CheckCertificateRequest(string managedSiteId)
        {
            DebugLog();

            //TODO: check current status of request in progress
            return _certifyManager.GetRequestProgressState(managedSiteId);
        }

        [HttpGet, Route("revoke/{managedSiteId}")]
        public async Task<StatusMessage> RevokeCertificate(string managedSiteId)
        {
            DebugLog();

            var managedSite = await _certifyManager.GetManagedSite(managedSiteId);
            var result = await _certifyManager.RevokeCertificate(managedSite);
            return result;
        }

        [HttpGet, Route("reapply/{managedSiteId}/{isPreviewOnly}")]
        public async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedSiteId, bool isPreviewOnly)
        {
            DebugLog();

            var managedSite = await _certifyManager.GetManagedSite(managedSiteId);

            /* RequestProgressState progressState = new RequestProgressState(RequestState.Running, "Starting..", managedSite);
             //begin monitoring progress
             _certifyManager.BeginTrackingProgress(progressState);*/

            var result = await _certifyManager.ReapplyCertificateBindings(managedSite, null, isPreviewOnly);
            return result;
        }
    }
}