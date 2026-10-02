using System;
using System.IO;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Providers;
using Certify.Server.Hub.Api.Services.Acme;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Migration of the hub Managed ACME service's legacy file based state into its configuration store.
    /// </summary>
    [TestClass]
    public class AcmeServerStateMigrationTests
    {
        private const string ConsumedEabKeysFile = "consumed-eab-keys.json";
        private const string EabKeyId = "eab-key-id";

        private string _configPath;
        private string _statePath;

        [TestInitialize]
        public void Setup()
        {
            _configPath = $"acme-tests-{Guid.NewGuid():N}";
            _statePath = EnvironmentUtil.EnsuredAppDataPath(_configPath);

            File.WriteAllText(Path.Join(_statePath, ConsumedEabKeysFile), $"{{\"{EabKeyId}\":\"01/09/2026 10:00:00\"}}");
        }

        [TestCleanup]
        public void Cleanup() => Directory.Delete(_statePath, recursive: true);

        /// <summary>
        /// An EAB key may only register one ACME account, so a key consumed before migration has to stay consumed.
        /// </summary>
        [TestMethod]
        [Description("EAB keys consumed in the legacy state are still consumed after migration")]
        public async Task MigrateSavedState_ConsumedEabKeys_AreStillConsumed()
        {
            var config = new AcmeServerConfig(new FakeConfigurationStore(), _configPath);

            await config.MigrateSavedState();

            Assert.IsTrue(await config.IsEabKeyConsumed(EabKeyId), "the consumed EAB key cannot register another account");
            Assert.IsFalse(File.Exists(Path.Join(_statePath, ConsumedEabKeysFile)), "the legacy file is removed once migrated");
        }

        [TestMethod]
        [Description("A legacy state file is kept when its entries cannot be stored, so migration is retried")]
        public async Task MigrateSavedState_StoreFails_KeepsTheLegacyFile()
        {
            var store = new Mock<IConfigurationStore>();
            store.Setup(s => s.Add(It.IsAny<string>(), It.IsAny<TypedConfigurationItem<string>>()))
                .ThrowsAsync(new IOException("store unavailable"));

            var config = new AcmeServerConfig(store.Object, _configPath);

            await Assert.ThrowsExactlyAsync<IOException>(config.MigrateSavedState);

            Assert.IsTrue(File.Exists(Path.Join(_statePath, ConsumedEabKeysFile)));
        }
    }
}
