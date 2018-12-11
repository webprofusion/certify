using Certify.Management;
using Certify.Models;
using System.Threading.Tasks;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/system")]
    public class SystemController : Controllers.ControllerBase
    {
        private ICertifyManager _certifyManager = null;

        public SystemController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("appversion")]
        public string GetAppVersion()
        {
            DebugLog();

            return Management.Util.GetAppVersion().ToString();
        }

        [HttpGet, Route("updatecheck")]
        public async Task<UpdateCheck> PerformUpdateCheck()
        {
            DebugLog();

            return await new Management.Util().CheckForUpdates();
        }

        [HttpGet, Route("maintenance")]
        public async Task<string> PerformMaintenanceTasks()
        {
            DebugLog();

            await _certifyManager.PerformCertificateCleanup();
            return "OK";
        }
    }
}
