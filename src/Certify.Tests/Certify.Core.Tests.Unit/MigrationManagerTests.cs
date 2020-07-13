using Certify.Config.Migration;
using Certify.Core.Management;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class MigrationManagerTests
    {

        [TestMethod, Description("Ensure managed cert and bundle exported")]
        public async Task TestPerformExport()
        {
            // setup
            var migrationManager = new MigrationManager(new ItemManager(), new CredentialsManager(), new ServerProviderMock());

            // export
            var export = await migrationManager.PerformExport( new ManagedCertificateFilter { }, new ExportSettings { EncryptionSecret = "secret" }, isPreview: false);

            // assert
            Assert.AreEqual(1, export.FormatVersion);

            Assert.IsNotNull(export.Description);
            Assert.IsNotNull(export.SourceName);
            Assert.IsNotNull(export.Content);

            Assert.IsNotNull(export.Content.ManagedCertificates);

            Assert.IsTrue(export.Content.CertificateFiles.Count > 0);

        }

        [TestMethod, Description("Ensure bundled certs can be decrypted")]
        public async Task TestDecryptExportedCerts()
        {
            // setup
            var migrationManager = new MigrationManager(new ItemManager(), new CredentialsManager(), new ServerProviderMock());

            // export
            var export = await migrationManager.PerformExport(new ManagedCertificateFilter { }, new ExportSettings { EncryptionSecret = "secret" }, isPreview: false);

            var import = await migrationManager.PerformImport(export, new ImportSettings { EncryptionSecret = "secret" }, isPreviewMode: true);

            // assert
            Assert.IsTrue(export.Content.CertificateFiles.Count > 0);

            Assert.IsTrue(import.FirstOrDefault(s => s.Key == "CertFiles").Substeps.Count > 0);

        }
        public string GetMarkdownPreviewFromSteps(List<ActionStep> steps)
        {
            return "";
        }

    }
}
