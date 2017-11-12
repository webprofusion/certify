using Certify.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/system")]
    public class SystemController : Controllers.ControllerBase
    {
        private Management.ICertifyManager _certifyManager = null;

        public SystemController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("test")]
        public string TestStatusStream()
        {
            StatusHub.HubContext.Clients.All.SendMessage("hello", "from status");
            return "OK";
        }

        [HttpGet, Route("appversion")]
        public string GetAppVersion()
        {
            DebugLog();

            return new Management.Util().GetAppVersion().ToString();
        }

        [HttpGet, Route("updatecheck")]
        public async Task<UpdateCheck> PerformUpdateCheck()
        {
            DebugLog();

            return await new Management.Util().CheckForUpdates();
        }
    }
}