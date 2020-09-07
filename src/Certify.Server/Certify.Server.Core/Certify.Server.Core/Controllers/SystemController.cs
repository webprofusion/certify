using Certify.Management;
using Certify.Models;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Certify.Service
{
    [ApiController]
    [EnableCors()]
    [Route("api/system")]
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
