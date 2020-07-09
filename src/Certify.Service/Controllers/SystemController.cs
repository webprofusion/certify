using Certify.Config.Migration;
using Certify.Management;
using Certify.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;

namespace Certify.Service
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
