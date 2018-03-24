using Certify.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/managedcertificates")]
    public class ManagedCertificatesController : Controllers.ControllerBase
    {
        private Management.ICertifyManager _certifyManager = null;

        public ManagedCertificatesController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        // Get List of Top N Managed Certificates, filtered by title
        [HttpPost, Route("search")]
        public async Task<List<ManagedCertificate>> Search(ManagedCertificateFilter filter)
        {
            DebugLog();

            return await _certifyManager.GetManagedCertificates(filter);
        }

        [HttpGet, Route("{id}")]
        public async Task<ManagedCertificate> GetById(string id)
        {
            DebugLog(id);

            return await _certifyManager.GetManagedCertificate(id);
        }

        //add or update managed site
        [HttpPost, Route("update")]
        public async Task<ManagedCertificate> Update(ManagedCertificate site)
        {
            DebugLog();

            return await _certifyManager.UpdateManagedCertificate(site);
        }

        [HttpDelete, Route("delete/{managedItemId}")]
        public async Task<bool> Delete(string managedItemId)
        {
            DebugLog();

            await _certifyManager.DeleteManagedCertificate(managedItemId);

            return true;
        }

        [HttpPost, Route("testconfig")]
        public async Task<StatusMessage> TestChallengeResponse(ManagedCertificate site)
        {
            DebugLog();

            var log = ManagedCertificateLog.GetLogger(site.Id);
            return await _certifyManager.TestChallenge(log, site, isPreviewMode: true);
        }

        /// <summary>
        /// Begin auto renew process and return list of included sites 
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("autorenew")]
        public async Task<List<CertificateRequestResult>> BeginAutoRenewal()
        {
            DebugLog();

            return await _certifyManager.PerformRenewalAllManagedCertificates(true, null);
        }

        [HttpGet, Route("renewcert/{managedItemId}")]
        public async Task<CertificateRequestResult> BeginCertificateRequest(string managedItemId)
        {
            DebugLog();

            var managedCertificate = await _certifyManager.GetManagedCertificate(managedItemId);

            RequestProgressState progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCertificate);

            var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

            //begin monitoring progress
            _certifyManager.BeginTrackingProgress(progressState);

            //begin request
            var result = await _certifyManager.PerformCertificateRequest(managedCertificate, progressIndicator);
            return result;
        }

        [HttpGet, Route("requeststatus/{managedItemId}")]
        public RequestProgressState CheckCertificateRequest(string managedItemId)
        {
            DebugLog();

            //TODO: check current status of request in progress
            return _certifyManager.GetRequestProgressState(managedItemId);
        }

        [HttpGet, Route("revoke/{managedItemId}")]
        public async Task<StatusMessage> RevokeCertificate(string managedItemId)
        {
            DebugLog();

            var managedCertificate = await _certifyManager.GetManagedCertificate(managedItemId);
            var result = await _certifyManager.RevokeCertificate(managedCertificate);
            return result;
        }

        [HttpGet, Route("reapply/{managedItemId}/{isPreviewOnly}")]
        public async Task<CertificateRequestResult> ReapplyCertificateBindings(string managedItemId, bool isPreviewOnly)
        {
            DebugLog();

            var managedCertificate = await _certifyManager.GetManagedCertificate(managedItemId);

            /* RequestProgressState progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCertificate);
             //begin monitoring progress
             _certifyManager.BeginTrackingProgress(progressState);*/

            var result = await _certifyManager.ReapplyCertificateBindings(managedCertificate, null, isPreviewOnly);
            return result;
        }
    }
}