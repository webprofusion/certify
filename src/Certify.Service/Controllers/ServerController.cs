using Certify.Models;
using System.Collections.Generic;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/server")]
    public class ServerController : ApiController
    {
        private Management.CertifyManager _certifyManager = new Certify.Management.CertifyManager();

        [HttpGet, Route("isavailable/{serverType}")]
        public bool IsServerAvailable(StandardServerTypes serverType)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return _certifyManager.IsIISAvailable;
            }
            else
            {
                return false;
            }
        }

        [HttpGet, Route("sitelist/{serverType}")]
        public List<SiteBindingItem> GetServerSiteList(StandardServerTypes serverType)
        {
            return _certifyManager.GetPrimaryWebSites(Management.CoreAppSettings.Current.IgnoreStoppedSites);
        }

        [HttpGet, Route("sitedomains/{serverType}/{serverSiteId}")]
        public List<DomainOption> GetServerSiteDomainOptions(StandardServerTypes serverType, string serverSiteId)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return _certifyManager.GetDomainOptionsFromSite(serverSiteId);
            }
            else
            {
                return new List<DomainOption>();
            }
        }

        [HttpGet, Route("version/{serverType}")]
        public System.Version GetServerVersion(StandardServerTypes serverType)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return _certifyManager.IISVersion;
            }
            else
            {
                return null;
            }
        }
    }
}