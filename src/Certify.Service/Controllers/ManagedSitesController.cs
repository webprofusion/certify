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

            await _certifyManager.LoadSettingsAsync(skipIfLoaded: true);
            return _certifyManager.GetManagedSites(filter);
        }

        [HttpGet, Route("{id}")]
        public async Task<ManagedSite> GetById(string id)
        {
            DebugLog(id);

            await _certifyManager.LoadSettingsAsync(skipIfLoaded: true);
            return _certifyManager.GetManagedSite(id);
        }

        //add or update managed site
        [HttpPost]
        public ManagedSite Update(ManagedSite site)
        {
            DebugLog();

            return _certifyManager.UpdateManagedSite(site);
        }

        [HttpDelete]
        public bool Delete(string id)
        {
            DebugLog();

            _certifyManager.DeleteManagedSite(id);

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
        public List<ManagedSite> BeginAutoRenewal()
        {
            DebugLog();

            // TODO: progress tracking events, background queue processing
            var list = _certifyManager.GetManagedSites(new ManagedSiteFilter { IncludeOnlyNextAutoRenew = true });

            _certifyManager.PerformRenewalAllManagedSites(true, null);

            return list;
        }

        [HttpGet, Route("renewcert/{managedSiteId}")]
        public bool BeginCertificateRequest(string managedSiteId)
        {
            DebugLog();

            var managedSite = _certifyManager.GetManagedSite(managedSiteId);
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

            var managedSite = _certifyManager.GetManagedSite(managedSiteId);
            var result = await _certifyManager.RevokeCertificate(managedSite);
            return result;
        }
    }
}