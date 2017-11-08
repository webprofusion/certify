using Certify.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/managedsites")]
    public class ManagedSitesController : ApiController
    {
        private Management.CertifyManager _certifyManager = new Certify.Management.CertifyManager();

        // Get List of Top N Managed Sites, filtered by title
        [HttpPost, Route("search")]
        public List<ManagedSite> Search(ManagedSiteFilter filter)
        {
            return _certifyManager.GetManagedSites();
        }

        [HttpGet]
        public ManagedSite GetById(string id)
        {
            return _certifyManager.GetManagedSite(id);
        }

        //add or update managed site
        [HttpPost]
        public ManagedSite Update(ManagedSite site)
        {
            return _certifyManager.UpdateManagedSite(site);
        }

        [HttpDelete]
        public bool Delete(string id)
        {
            _certifyManager.DeleteManagedSite(id);

            return true;
        }

        [HttpPost]
        public async Task<APIResult> TestChallengeResponse(ManagedSite site)
        {
            return await _certifyManager.TestChallenge(site, isPreviewMode: true);
        }

        /// <summary>
        /// Begin auto renew process and return list of included sites 
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public List<ManagedSite> BeginAutoRenewal()
        {
            // TODO: progress tracking events
            var list = _certifyManager.GetManagedSites(new ManagedSiteFilter { IncludeOnlyNextAutoRenew = true });

            _certifyManager.PerformRenewalAllManagedSites(true, null);

            return list;
        }

        [HttpGet]
        public void BeginCertificateRequest(string managedSiteId)
        {
            var managedSite = _certifyManager.GetManagedSite(managedSiteId);
            // TODO: progress tracking events
            _certifyManager.PerformCertificateRequest(managedSite, null);
        }

        [HttpGet]
        public string CheckCertificateRequest(string managedSiteId)
        {
            //TODO: check current status of request in progress
            return "Unknown";
        }

        [HttpGet]
        public async Task<APIResult> RevokeCertificate(string managedSiteId)
        {
            var managedSite = _certifyManager.GetManagedSite(managedSiteId);
            var result = await _certifyManager.RevokeCertificate(managedSite);
            return result;
        }
    }
}