using Certify.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/system")]
    public class SystemController : ApiController
    {
        [HttpGet, Route("appversion")]
        public string GetAppVersion()
        {
            return new Management.Util().GetAppVersion().ToString();
        }

        [HttpGet, Route("updatecheck")]
        public async Task<UpdateCheck> PerformUpdateCheck()
        {
            return await new Management.Util().CheckForUpdates();
        }
    }
}