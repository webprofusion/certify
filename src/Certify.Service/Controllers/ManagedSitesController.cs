using Certify.Models;
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
        public async Task<APIResult> TestChallengeResponse(ManagedSite site)
        {
            DebugLog();

            return await _certifyManager.TestChallenge(site, isPreviewMode: true);
        }

        /// <summary>
        /// Begin auto renew process and return list of included sites 
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("autorenew")]
        public async Task<List<ManagedSite>> BeginAutoRenewal()
        {
            DebugLog();

            // TODO: progress tracking events, background queue processing
            var list = await _certifyManager.GetManagedSites(new ManagedSiteFilter { IncludeOnlyNextAutoRenew = true });

            //we do not await here, instead the task currently continues after the request
            _certifyManager.PerformRenewalAllManagedSites(true, null);

            return list;
        }

        [HttpGet, Route("renewcert/{managedSiteId}")]
        public async Task<bool> BeginCertificateRequest(string managedSiteId)
        {
            DebugLog();

            var managedSite = await _certifyManager.GetManagedSite(managedSiteId);
            // TODO: progress tracking events, background queue
            _certifyManager.PerformCertificateRequest(managedSite, null);
            return true;
        }

        [HttpGet, Route("requeststatus/{managedSiteId}")]
        public string CheckCertificateRequest(string managedSiteId)
        {
            DebugLog();

            //TODO: check current status of request in progress
            return "Unknown";
        }

        [HttpGet, Route("revoke/{managedSiteId}")]
        public async Task<APIResult> RevokeCertificate(string managedSiteId)
        {
            DebugLog();

            var managedSite = await _certifyManager.GetManagedSite(managedSiteId);
            var result = await _certifyManager.RevokeCertificate(managedSite);
            return result;
        }
    }
}