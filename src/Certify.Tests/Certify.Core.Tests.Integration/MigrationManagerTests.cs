using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config.Migration;
using Certify.Core.Management;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class MigrationManagerTests
    {

        [TestMethod, Description("Ensure managed cert and bundle exported")]
        public async Task TestPerformExport()
        {
            // setup
            var migrationManager = new MigrationManager(new SQLiteManagedItemStore(), new SQLiteCredentialStore(), new List<Models.Providers.ITargetWebServer> { new ServerProviderMock() });

            // export
            var export = await migrationManager.PerformExport(new ManagedCertificateFilter { }, new ExportSettings { EncryptionSecret = "secret" }, isPreview: false);

            // assert
            Assert.AreEqual(1, export.FormatVersion);

            Assert.IsNotNull(export.Description);
            Assert.IsNotNull(export.SourceName);
            Assert.IsNotNull(export.Content);

            Assert.IsNotNull(export.Content.ManagedCertificates);

            Assert.AreNotEqual(0, export.Content.ManagedCertificates.Count);

        }

        [TestMethod, Description("Ensure basic encrypt/decrypt")]
        public void TestDecryptEncrypt()
        {
            // setup
            var migrationManager = new MigrationManager(new SQLiteManagedItemStore(), new SQLiteCredentialStore(), new List<Models.Providers.ITargetWebServer> { new ServerProviderMock() });

            // encrypt

            var sourceString = "The /cat/ sat on the {mat}. The /cat/ sat on the {mat}. The /cat/ sat on the {mat} 12345.";

            var encrypted = migrationManager.EncryptBytes(System.Text.Encoding.ASCII.GetBytes(sourceString), "secretstringthing", "salty123");

            var decrypted = migrationManager.DecryptBytes(encrypted, "secretstringthing", "salty123");

            var decryptedString = System.Text.Encoding.ASCII.GetString(decrypted).TrimEnd('\0');

            // assert
            Assert.AreEqual(sourceString, decryptedString, "Source and decrypted values should be the same.");

        }

        [TestMethod, Description("Ensure bundled certs can be decrypted")]
        public async Task TestDecryptExportedCerts()
        {
            // setup
            var migrationManager = new MigrationManager(new SQLiteManagedItemStore(), new SQLiteCredentialStore(), new List<Models.Providers.ITargetWebServer> { new ServerProviderMock() });

            // export
            var export = await migrationManager.PerformExport(new ManagedCertificateFilter { }, new ExportSettings { EncryptionSecret = "secret" }, isPreview: false);

            var import = await migrationManager.PerformImport(export, new ImportSettings { EncryptionSecret = "secret" }, isPreviewMode: true);

            // assert
            Assert.IsNotNull(export);
            Assert.IsNotNull(import);
            //Assert.IsTrue(import.FirstOrDefault(s => s.Key == "CertFiles").Substeps.Count > 0);

        }
        public string GetMarkdownPreviewFromSteps(List<ActionStep> steps)
        {
            return "";
        }
    }
}
