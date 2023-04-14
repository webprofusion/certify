using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config.Migration;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [EnableCors()]
    [Route("api/system")]
    public class SystemController : ControllerBase
    {
        private ICertifyManager _certifyManager = null;

        public SystemController(ICertifyManager manager)
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

        [HttpGet, Route("datastores/providers")]
        public async Task<List<ProviderDefinition>> GetDataStoreProviders()
        {
            return await _certifyManager.GetDataStoreProviders();
        }

        [HttpGet, Route("datastores/")]
        public async Task<List<DataStoreConnection>> GetDataStores()
        {
            return await _certifyManager.GetDataStores();
        }

        [HttpPost, Route("datastores/copy/{sourceId}/{destId}")]
        public async Task<List<ActionStep>> CopyDataStoreToTarget(string sourceId, string destId)
        {
            return await _certifyManager.CopyDateStoreToTarget(sourceId, destId);
        }

        [HttpPost, Route("datastores/setdefault/{dataStoreId}")]
        public async Task<List<ActionStep>> SetDefaultDataStore(string dataStoreId)
        {
            return await _certifyManager.SetDefaultDataStore(dataStoreId);
        }

        [HttpPost, Route("datastores/update")]
        public async Task<List<ActionStep>> UpdateDataStore(DataStoreConnection dataStore)
        {
            return await _certifyManager.UpdateDataStoreConnection(dataStore);
        }

        [HttpPost, Route("datastores/test")]
        public async Task<List<ActionStep>> TestDataStore(DataStoreConnection dataStore)
        {
            return await _certifyManager.TestDataStoreConnection(dataStore);
        }

        [HttpPost, Route("datastores/delete")]
        public async Task<List<ActionStep>> RemoveDataStore(string dataStoreId) => await _certifyManager.RemoveDataStoreConnection(dataStoreId);
    }
}
