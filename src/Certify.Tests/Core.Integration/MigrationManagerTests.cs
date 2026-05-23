using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Core.Management;
using Certify.Datastore.SQLite;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class MigrationManagerTests
    {

        private IManagedItemStore GetManagedItemStore()
        {

            var itemManager = new SQLiteManagedItemStore();
            itemManager.Init(string.Empty, null, performBackup: false);
            return itemManager;
        }

        private ICredentialsManager GetCredentialsStore()
        {
            var itemManager = new SQLiteCredentialStore();
            itemManager.Init(string.Empty, null, instanceId: null);
            return itemManager;
        }

        [TestMethod, Description("Ensure managed cert and bundle exported")]
        public async Task TestPerformExport()
        {
            // setup
            var migrationManager = new MigrationManager(GetManagedItemStore(), GetCredentialsStore(), new List<Models.Providers.ITargetWebServer> { new ServerProviderMock() });

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
            var migrationManager = new MigrationManager(GetManagedItemStore(), GetCredentialsStore(), new List<Models.Providers.ITargetWebServer> { new ServerProviderMock() });

            // encrypt

            var sourceString = "The /cat/ sat on the {mat}. The /cat/ sat on the {mat}. The /cat/ sat on the {mat} 12345.";

            var encrypted = migrationManager.EncryptBytes(System.Text.Encoding.ASCII.GetBytes(sourceString), "secretstringthing", "salty123");

            var decrypted = migrationManager.DecryptBytes(encrypted, "secretstringthing", "salty123");

            var decryptedString = System.Text.Encoding.ASCII.GetString(decrypted).TrimEnd('\0');

            // assert
            Assert.AreEqual(sourceString, decryptedString, "Source and decrypted values should be the same.");

        }

        [TestMethod, Description("Ensure basic encrypt/decrypt with short secret")]
        public void TestDecryptEncryptShortSecret()
        {
            var secretPwd = "test";
            var salt = "8add3854-7819-4220-99a8-29bcdc32e9be";
            // setup
            var migrationManager = new MigrationManager(GetManagedItemStore(), GetCredentialsStore(), new List<Models.Providers.ITargetWebServer> { new ServerProviderMock() });

            // encrypt

            var sourceString = "The /cat/ sat on the {mat}. The /cat/ sat on the {mat}. The /cat/ sat on the {mat} 12345.";

            var encrypted = migrationManager.EncryptBytes(System.Text.Encoding.ASCII.GetBytes(sourceString), secretPwd, salt);

            var decrypted = migrationManager.DecryptBytes(encrypted, secretPwd, salt);

            var decryptedString = System.Text.Encoding.ASCII.GetString(decrypted).TrimEnd('\0');

            // assert
            Assert.AreEqual(sourceString, decryptedString, "Source and decrypted values should be the same.");

        }

        [TestMethod, Description("Ensure bundled certs can be decrypted")]
        public async Task TestDecryptExportedCerts()
        {
            // setup
            var migrationManager = new MigrationManager(GetManagedItemStore(), GetCredentialsStore(), new List<Models.Providers.ITargetWebServer> { new ServerProviderMock() });

            // export
            var export = await migrationManager.PerformExport(new ManagedCertificateFilter { }, new ExportSettings { EncryptionSecret = "secret" }, isPreview: false);

            var import = await migrationManager.PerformImport(export, new ImportSettings { EncryptionSecret = "secret" }, isPreviewMode: true);

            // assert
            Assert.IsNotNull(export);
            Assert.IsNotNull(import);
            //Assert.IsTrue(import.FirstOrDefault(s => s.Key == "CertFiles").Substeps.Count > 0);

        }

        [TestMethod, Description("Ensure export only includes referenced credentials and records decrypt failures")]
        public async Task TestPerformExportOnlyIncludesReferencedCredentials()
        {
            var managedCert = CreateManagedCertificate(
                id: "managed-1",
                primaryDomain: "example.com",
                certificatePasswordCredentialId: "cred-cert",
                postRequestCredentialId: "cred-task");

            var itemStore = new TestManagedItemStore(managedCert);
            var credentialsStore = new TestCredentialsManager(
            [
                new StoredCredential { StorageKey = "cred-cert", Title = "Cert Password" },
                new StoredCredential { StorageKey = "cred-task", Title = "DNS Task" },
                new StoredCredential { StorageKey = "cred-unused", Title = "Unused" }
            ]);

            credentialsStore.UnlockedSecrets["cred-cert"] = "pfx-secret";
            credentialsStore.UnlockFailures.Add("cred-task");

            var migrationManager = new MigrationManager(itemStore, credentialsStore, new List<ITargetWebServer>());

            var export = await migrationManager.PerformExport(new ManagedCertificateFilter(), new ExportSettings
            {
                EncryptionSecret = "secret",
                ExportAllStoredCredentials = false,
                ExportCustomCertificateAuthorities = false
            }, isPreview: false);

            Assert.IsNotNull(export.Content?.StoredCredentials);
            Assert.AreEqual(2, export.Content.StoredCredentials.Count, "Only referenced credentials should be exported.");
            Assert.IsTrue(export.Content.StoredCredentials.Any(c => c.StorageKey == "cred-cert"));
            Assert.IsTrue(export.Content.StoredCredentials.Any(c => c.StorageKey == "cred-task"));
            Assert.IsFalse(export.Content.StoredCredentials.Any(c => c.StorageKey == "cred-unused"));

            var exportedPasswordCredential = export.Content.StoredCredentials.Single(c => c.StorageKey == "cred-cert");
            var decryptedSecret = Encoding.UTF8.GetString(
                migrationManager.DecryptBytes(Convert.FromBase64String(exportedPasswordCredential.Secret), "secret", export.EncryptionSalt))
                .TrimEnd('\0');

            Assert.AreEqual("pfx-secret", decryptedSecret, "Referenced credential secrets should be re-encrypted into the export package.");

            var exportedTaskCredential = export.Content.StoredCredentials.Single(c => c.StorageKey == "cred-task");
            Assert.AreEqual(string.Empty, exportedTaskCredential.Secret, "Failed credential decrypts should export a blank secret.");
            Assert.IsTrue(export.Errors.Any(e => e.Contains("DNS Task") && e.Contains("could not be decrypted")), "Decrypt failures should be recorded in export errors.");
        }

        [TestMethod, Description("Ensure import stops when the provided password cannot decrypt secrets")]
        public async Task TestPerformImportReturnsErrorForWrongPassword()
        {
            var migrationManager = new MigrationManager(new TestManagedItemStore(), new TestCredentialsManager(), new List<ITargetWebServer>());
            var package = CreatePackage(migrationManager, new ImportExportContent(), encryptionSecret: "correct-secret");

            var steps = await migrationManager.PerformImport(package, new ImportSettings { EncryptionSecret = "wrong-secret" }, isPreviewMode: true);

            Assert.AreEqual("Version", steps[0].Key);
            Assert.AreEqual("Decrypt", steps[1].Key);
            Assert.IsTrue(steps[1].HasError, "Wrong passwords should fail the decryption validation step.");
            Assert.AreEqual(2, steps.Count, "Import should stop immediately after decryption validation fails.");
        }

        [TestMethod, Description("Ensure import warns and switches to Auto deployment when the target site cannot be matched")]
        public async Task TestPerformImportPreviewSwitchesSingleSiteToAutoWhenSiteMissing()
        {
            var managedCert = CreateManagedCertificate(id: "managed-2", primaryDomain: "example.com");
            managedCert.ServerSiteId = "site-1";
            managedCert.RequestConfig.DeploymentSiteOption = DeploymentOption.SingleSite;

            var migrationManager = new MigrationManager(
                new TestManagedItemStore(),
                new TestCredentialsManager(),
                new List<ITargetWebServer> { new TestTargetWebServer() });

            var package = CreatePackage(migrationManager, new ImportExportContent
            {
                ManagedCertificates = new List<ManagedCertificate> { managedCert }
            });

            var steps = await migrationManager.PerformImport(package, new ImportSettings { EncryptionSecret = "secret" }, isPreviewMode: true);
            var managedCertStep = steps.Single(s => s.Key == "ManagedCerts");
            var certSubstep = managedCertStep.Substeps.Single();

            Assert.IsTrue(certSubstep.HasWarning, "Missing target sites should generate a warning during import preview.");
            StringAssert.Contains(certSubstep.Description, "Deployment switched to Auto mode");
            Assert.AreEqual(DeploymentOption.Auto, managedCert.RequestConfig.DeploymentSiteOption, "Single-site imports should switch to Auto when the target site no longer exists.");
        }

        [TestMethod, Description("Ensure import warns when an item already exists and overwrite is disabled")]
        public async Task TestPerformImportSkipsExistingManagedCertificateWhenOverwriteDisabled()
        {
            var existing = CreateManagedCertificate(id: "managed-3", primaryDomain: "existing.example.com");
            var incoming = CreateManagedCertificate(id: "managed-3", primaryDomain: "incoming.example.com");

            var itemStore = new TestManagedItemStore(existing);
            var migrationManager = new MigrationManager(itemStore, new TestCredentialsManager(), new List<ITargetWebServer>());

            var package = CreatePackage(migrationManager, new ImportExportContent
            {
                ManagedCertificates = new List<ManagedCertificate> { incoming }
            });

            var steps = await migrationManager.PerformImport(package, new ImportSettings { EncryptionSecret = "secret", OverwriteExisting = false }, isPreviewMode: true);
            var managedCertStep = steps.Single(s => s.Key == "ManagedCerts");
            var certSubstep = managedCertStep.Substeps.Single();

            Assert.IsTrue(certSubstep.HasWarning, "Existing managed certificates should be warned about when overwrite is disabled.");
            StringAssert.Contains(certSubstep.Description, "already exists");
            Assert.AreEqual(0, itemStore.UpdateCalls, "Preview import should not update existing managed certificates.");
        }

        public string GetMarkdownPreviewFromSteps(List<ActionStep> steps)
        {
            return "";
        }

        private static ManagedCertificate CreateManagedCertificate(string id, string primaryDomain, string certificatePasswordCredentialId = null, string postRequestCredentialId = null)
        {
            var managedCert = new ManagedCertificate
            {
                Id = id,
                Name = id,
                ServerSiteId = "site-1",
                CertificatePasswordCredentialId = certificatePasswordCredentialId,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = primaryDomain,
                    DeploymentSiteOption = DeploymentOption.Auto
                }
            };

            if (postRequestCredentialId != null)
            {
                managedCert.PostRequestTasks =
                [
                    new DeploymentTaskConfig { ChallengeCredentialKey = postRequestCredentialId }
                ];
            }

            return managedCert;
        }

        private static ImportExportPackage CreatePackage(MigrationManager migrationManager, ImportExportContent content, string encryptionSecret = "secret")
        {
            var salt = Guid.NewGuid().ToString();

            return new ImportExportPackage
            {
                SystemVersion = new SerializableVersion(Certify.Management.Util.GetAppVersion()),
                EncryptionSalt = salt,
                EncryptionValidation = new EncryptedContent
                {
                    Content = migrationManager.EncryptBytes(Encoding.UTF8.GetBytes("Secret"), encryptionSecret, salt),
                    Scheme = "default"
                },
                Content = content
            };
        }

        private sealed class TestManagedItemStore : IManagedItemStore
        {
            private readonly Dictionary<string, ManagedCertificate> _items = new();

            public int UpdateCalls { get; private set; }

            public TestManagedItemStore(params ManagedCertificate[] items)
            {
                foreach (var item in items)
                {
                    _items[item.Id] = item;
                }
            }

            public bool Init(string connectionString, ILog log, string instanceId = null) => true;

            public Task DeleteAll()
            {
                _items.Clear();
                return Task.CompletedTask;
            }

            public Task StoreAll(IEnumerable<ManagedCertificate> list)
            {
                foreach (var item in list)
                {
                    _items[item.Id] = item;
                }

                return Task.CompletedTask;
            }

            public Task Delete(ManagedCertificate site)
            {
                _items.Remove(site.Id);
                return Task.CompletedTask;
            }

            public Task DeleteByName(string nameStartsWith)
            {
                foreach (var key in _items.Values.Where(i => i.Name?.StartsWith(nameStartsWith, StringComparison.OrdinalIgnoreCase) == true).Select(i => i.Id).ToList())
                {
                    _items.Remove(key);
                }

                return Task.CompletedTask;
            }

            public Task<ManagedCertificate> GetById(string siteId)
            {
                _items.TryGetValue(siteId, out var item);
                return Task.FromResult(item);
            }

            public Task<List<ManagedCertificate>> Find(ManagedCertificateFilter filter) => Task.FromResult(_items.Values.ToList());

            public Task<long> CountAll(ManagedCertificateFilter filter) => Task.FromResult((long)_items.Count);

            public Task<ManagedCertificate> Update(ManagedCertificate managedCertificate)
            {
                UpdateCalls++;
                _items[managedCertificate.Id] = managedCertificate;
                return Task.FromResult(managedCertificate);
            }

            public Task PerformMaintenance() => Task.CompletedTask;

            public Task<bool> IsInitialised() => Task.FromResult(true);
        }

        private sealed class TestCredentialsManager : ICredentialsManager
        {
            private readonly Dictionary<string, StoredCredential> _credentials = new();

            public Dictionary<string, string> UnlockedSecrets { get; } = new();
            public HashSet<string> UnlockFailures { get; } = new();

            public TestCredentialsManager(params StoredCredential[] credentials)
            {
                foreach (var credential in credentials)
                {
                    _credentials[credential.StorageKey] = credential;
                }
            }

            public bool Init(string connectionString, ILog log, string instanceId = null) => true;

            public Task<bool> IsInitialised() => Task.FromResult(true);

            public Task<ActionResult> Delete(IManagedItemStore itemStore, string storageKey) => Task.FromResult<ActionResult>(null);

            public Task<List<StoredCredential>> GetCredentials(string type = null, string storageKey = null)
            {
                var results = _credentials.Values.ToList();

                if (storageKey != null)
                {
                    results = results.Where(c => c.StorageKey == storageKey).ToList();
                }

                return Task.FromResult(results);
            }

            public Task<StoredCredential> GetCredential(string storageKey)
            {
                _credentials.TryGetValue(storageKey, out var credential);
                return Task.FromResult(credential);
            }

            public Task<string> GetUnlockedCredential(string storageKey)
            {
                if (UnlockFailures.Contains(storageKey))
                {
                    throw new InvalidOperationException("Credential cannot be unlocked.");
                }

                return Task.FromResult(UnlockedSecrets.TryGetValue(storageKey, out var secret) ? secret : null);
            }

            public Task<Dictionary<string, string>> GetUnlockedCredentialsDictionary(string storageKey)
            {
                return Task.FromResult(UnlockedSecrets.TryGetValue(storageKey, out var secret)
                    ? new Dictionary<string, string> { ["password"] = secret }
                    : null);
            }

            public Task<StoredCredential> Update(StoredCredential credentialInfo)
            {
                _credentials[credentialInfo.StorageKey] = credentialInfo;
                return Task.FromResult(credentialInfo);
            }
        }

        private sealed class TestTargetWebServer : ITargetWebServer
        {
            public List<BindingInfo> Bindings { get; } = new();

            public void Dispose()
            {
            }

            public Task<List<BindingInfo>> GetSiteBindingList(bool ignoreStoppedSites, string siteId = null)
            {
                var bindings = Bindings;
                if (siteId != null)
                {
                    bindings = bindings.Where(b => b.SiteId == siteId).ToList();
                }

                return Task.FromResult(bindings);
            }

            public Task<List<SiteInfo>> GetPrimarySites(bool ignoreStoppedSites) => Task.FromResult(new List<SiteInfo>());

            public Task<SiteInfo> GetSiteById(string siteId) => Task.FromResult<SiteInfo>(null);

            public Task RemoveHttpsBinding(string siteId, string sni) => Task.CompletedTask;

            public Task<Version> GetServerVersion() => Task.FromResult(new Version(1, 0));

            public Task<bool> IsAvailable() => Task.FromResult(true);

            public Task<bool> IsSiteRunning(string id) => Task.FromResult(true);

            public IBindingDeploymentTarget GetDeploymentTarget() => null;

            public Task<List<ActionStep>> RunConfigurationDiagnostics(string siteId) => Task.FromResult(new List<ActionStep>());

            public void Init(ILog log, string configRoot = null)
            {
            }

            public ServerTypeInfo GetServerTypeInfo() => new() { ServerType = StandardServerTypes.Other, Title = "Test Server" };
        }
    }
}
