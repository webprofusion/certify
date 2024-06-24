using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.API.Management;
using Certify.Client;
using Certify.Core.Management;
using Certify.Models;
using Certify.Models.Config.Migration;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        /// <summary>
        /// Perform (or preview) an import of settings from another instance
        /// </summary>
        /// <param name="importRequest"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> PerformImport(ImportRequest importRequest)
        {
            var migrationManager = new MigrationManager(_itemManager, _credentialsManager, _serverProviders);

            var importResult = await migrationManager.PerformImport(importRequest.Package, importRequest.Settings, importRequest.IsPreviewMode);

            // store and apply certs if we have no errors

            var hasError = false;
            if (!importResult.Any(i => i.HasError))
            {
                if (importRequest.Settings.IncludeDeployment)
                {

                    var deploySteps = new List<ActionStep>();
                    foreach (var m in importRequest.Package.Content.ManagedCertificates)
                    {
                        var managedCert = await GetManagedCertificate(m.Id);

                        if (managedCert != null && !string.IsNullOrEmpty(managedCert.CertificatePath))
                        {
                            var deployResult = await DeployCertificate(managedCert, null, isPreviewOnly: importRequest.IsPreviewMode);

                            deploySteps.Add(new ActionStep { Category = "Deployment", HasError = !deployResult.IsSuccess, Key = managedCert.Id, Description = deployResult.Message });
                        }
                    }

                    importResult.Add(new ActionStep { Title = "Deployment" + (importRequest.IsPreviewMode ? " [Preview]" : ""), Substeps = deploySteps });
                }
            }
            else
            {
                hasError = true;
            }

            _tc?.TrackEvent("Import" + (importRequest.IsPreviewMode ? "_Preview" : ""), new Dictionary<string, string> {
                { "hasErrors", hasError.ToString() }
            });

            return importResult;
        }

        /// <summary>
        /// Perform (or preview) and export of settings from this instance
        /// </summary>
        /// <param name="exportRequest"></param>
        /// <returns></returns>
        public async Task<ImportExportPackage> PerformExport(ExportRequest exportRequest)
        {
            _tc?.TrackEvent("Export" + (exportRequest.IsPreviewMode ? "_Preview" : ""));

            var migrationManager = new MigrationManager(_itemManager, _credentialsManager, _serverProviders);
            return await migrationManager.PerformExport(exportRequest.Filter, exportRequest.Settings, exportRequest.IsPreviewMode);
        }
    }
}
