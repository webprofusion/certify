using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// A subscription on the hub to a certificate the hub manages itself. The hub's own instance is not joined over the
    /// hub API, so it has no credentials to download with and the certificate is exported in-process instead, allowed
    /// only when the hub settings permit the hub to subscribe to its own certificates
    /// </summary>
    [TestClass]
    public class HubSelfSubscriptionFetchTests
    {
        private const string HubInstanceId = "hub-instance";
        private const string SourceId = "source-cert";
        private const string Thumbprint = "ABCDEF0123";

        private string _certPath;

        [TestInitialize]
        public void Setup()
        {
            _certPath = Path.GetTempFileName();
            File.WriteAllBytes(_certPath, Encoding.UTF8.GetBytes("pfx-bytes"));
        }

        [TestCleanup]
        public void Cleanup() => File.Delete(_certPath);

        private static void SetPrivateField(CertifyManager manager, string fieldName, object value)
        {
            var field = typeof(CertifyManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{fieldName} should be available for testing");
            field.SetValue(manager, value);
        }

        private CertifyManager CreateHubManager(bool allowSelfSubscription)
        {
            var source = new ManagedCertificate
            {
                Id = SourceId,
                Name = "Source",
                CertificatePath = _certPath,
                CertificateThumbprintHash = Thumbprint
            };

            var itemStore = new Mock<IManagedItemStore>();
            itemStore.Setup(s => s.GetById(SourceId)).ReturnsAsync(source);

            var manager = new CertifyManager();
            manager.EnableManagementHubBackend(isDirectHubBackend: true);

            SetPrivateField(manager, "_itemManager", itemStore.Object);
            SetPrivateField(manager, "_serverConfig", new Certify.Shared.ServiceConfig { HubAssignedInstanceId = HubInstanceId });
            SetPrivateField(manager, "_cachedHubSettings", new HubSettings
            {
                Subscriptions = new SubscriptionSettings { AllowHubSelfSubscription = allowSelfSubscription }
            });

            return manager;
        }

        private static ManagedCertificate CreateSubscription(string sourceId = SourceId, string lastSourceVersion = null)
        {
            return new ManagedCertificate
            {
                Id = "subscriber-item",
                Name = "Subscriber Item",
                ItemType = ManagedCertificateType.SSL_ExternalSubscription,
                ExternalSource = new ExternalCertificateSubscription
                {
                    SourceType = ExternalCertificateSourceTypes.ManagementHub,
                    RetrievalMode = ExternalCertificateRetrievalModes.Auto,
                    ExternalReference = $"{HubInstanceId}/{sourceId}",
                    LastSourceVersion = lastSourceVersion
                }
            };
        }

        private sealed class FetchOutcome
        {
            public bool IsSuccess { get; init; }
            public bool HasUpdate { get; init; }
            public string SourceVersion { get; init; }
            public string Message { get; init; }
            public int PayloadLength { get; init; }
        }

        private static async Task<FetchOutcome> Fetch(CertifyManager manager, ManagedCertificate item)
        {
            var method = typeof(CertifyManager).GetMethod("FetchExternalCertificateAsset", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "FetchExternalCertificateAsset should be available for testing");

            var task = (Task)method.Invoke(manager, new object[] { item, item.ExternalSource, CancellationToken.None, false });
            await task;

            var result = task.GetType().GetProperty("Result").GetValue(task);
            var resultType = result.GetType();
            var payload = (byte[])resultType.GetProperty("CertificateData").GetValue(result);

            return new FetchOutcome
            {
                IsSuccess = (bool)resultType.GetProperty("IsSuccess").GetValue(result),
                HasUpdate = (bool)resultType.GetProperty("HasUpdate").GetValue(result),
                SourceVersion = (string)resultType.GetProperty("SourceVersion").GetValue(result),
                Message = (string)resultType.GetProperty("Message").GetValue(result),
                PayloadLength = payload?.Length ?? 0
            };
        }

        [TestMethod, Description("The hub fetches its own certificate in-process, versioned by thumbprint as the download endpoint does")]
        public async Task HubOwnCertificateIsExportedInProcess()
        {
            var result = await Fetch(CreateHubManager(allowSelfSubscription: true), CreateSubscription());

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsTrue(result.HasUpdate);
            Assert.AreEqual("pfx-bytes".Length, result.PayloadLength);
            Assert.AreEqual(Thumbprint.ToLowerInvariant(), result.SourceVersion);
        }

        [TestMethod, Description("A hub certificate already held at the same version is not an update")]
        public async Task HubOwnCertificateAlreadyHeldIsNotAnUpdate()
        {
            var result = await Fetch(CreateHubManager(allowSelfSubscription: true), CreateSubscription(lastSourceVersion: Thumbprint.ToLowerInvariant()));

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsFalse(result.HasUpdate);
        }

        [TestMethod, Description("The hub does not fetch its own certificates unless the hub settings allow it")]
        public async Task HubOwnCertificateIsRefusedWhenNotAllowed()
        {
            var result = await Fetch(CreateHubManager(allowSelfSubscription: false), CreateSubscription());

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "not enabled");
        }

        [TestMethod, Description("A subscription cannot use itself as its source")]
        public async Task SubscriptionToItselfIsRefused()
        {
            var result = await Fetch(CreateHubManager(allowSelfSubscription: true), CreateSubscription(sourceId: "subscriber-item"));

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(0, result.PayloadLength);
        }
    }
}
