using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using Certify.Config.Migration;
using Certify.Management;
using Certify.Models;

namespace Certify.Service.Controllers
{
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    [RoutePrefix("api/system")]
    public class SystemController : Controllers.ControllerBase
    {
        private ICertifyManager _certifyManager = null;

        public SystemController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [AllowAnonymous]
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

        [HttpGet, Route("diagnostics")]
        public async Task<List<Models.Config.ActionResult>> PerformServiceDiagnostics()
        {
            DebugLog();

            return await _certifyManager.PerformServiceDiagnostics();
        }

        [HttpPost, Route("migration/export")]
        public async Task<ImportExportPackage> PerformExport(ExportRequest exportRequest)
        {
            return await _certifyManager.PerformExport(exportRequest);
        }

        [HttpPost, Route("migration/import")]
        public async Task<List<ActionStep>> PerformImport(ImportRequest importRequest)
        {
            return await _certifyManager.PerformImport(importRequest);
        }
    }
}
