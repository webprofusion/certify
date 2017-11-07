using Certify.Models;
using System.Collections.Generic;
using System.Web.Http;

namespace Certify.Service
{
    public class ManagedSitesController : ApiController
    {
        Management.CertifyManager _certifyManager = new Certify.Management.CertifyManager();

        // Get List of Top N Managed Sites, filtered by title
        public List<ManagedSite> Get()
        {
            return _certifyManager.GetManagedSites();
        }
        //add or update managed site

        public ManagedSite Update(ManagedSite site)
        {
            var certifyManager = new Certify.Management.CertifyManager();
            //certifyManager.UpdateManagedSite(site);

            return site;
        }


        //delete managed site

        //get web server site list

        //test managed site configuration

        //perform certificate request (stream responses or poll?). How to get progress updates.

        //get all requests in progress
    }

}