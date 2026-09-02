using System;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how a certificate request behaves while the service is in degraded mode. The outcome of a request
    /// cannot be recorded without the data store, and a request whose outcome is not recorded is repeated on the next
    /// pass - for a certificate order, against the CA's duplicate certificate limits - so no request may start.
    /// The scheduled passes stand down on their own; this covers the requests still to come in a batch which was
    /// already running when the data store failed, and requests started by a user or the hub in the meantime
    /// </summary>
    [TestClass]
    public class DegradedModeRequestTests
    {
        private static ManagedCertificate CreateItem()
        {
            return new ManagedCertificate
            {
                Id = "degraded-item",
                Name = "Degraded Item",
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ACME,
                RequestConfig = new CertRequestConfig { PrimaryDomain = "test.example.com" }
            };
        }

        [TestMethod, Description("A certificate request is not started while the data store is unavailable")]
        public async Task RequestIsAbortedInDegradedMode()
        {
            var manager = new CertifyManager();
            var item = CreateItem();

            Assert.IsFalse(manager.IsInDegradedMode, "A new manager is not degraded");

            manager.HandleDataStoreFailure("Data store write test failed: disk full", "default", "sqlite");

            Assert.IsTrue(manager.IsInDegradedMode);

            var result = await manager.PerformCertificateRequest(null, item);

            Assert.IsTrue(result.Abort, "The request must not start while its outcome cannot be recorded");
            Assert.IsFalse(result.IsSuccess);
            Assert.AreSame(item, result.ManagedItem);
            StringAssert.Contains(result.Message, "data store", StringComparison.OrdinalIgnoreCase);
        }
    }
}