using Certify.Server.Hub.Api.Models.Acme;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The status a hub Managed ACME order shows its client, and that showing it leaves the order's internal status alone.
    /// </summary>
    [TestClass]
    public class AcmeOrderStatusTests
    {
        [TestMethod]
        [DataRow(OrderStatus.Ready, OrderStatus.Ready)]
        [DataRow(OrderStatus.ReadyForInternalFinalization, OrderStatus.Ready)]
        [DataRow(OrderStatus.InternalFinalizationInProgress, OrderStatus.Processing)]
        [DataRow(OrderStatus.Valid, OrderStatus.Valid)]
        [DataRow(OrderStatus.Invalid, OrderStatus.Invalid)]
        public void ClientStatusBeforeFinalize(OrderStatus internalStatus, OrderStatus expected)
        {
            var order = new AcmeOrder { Status = internalStatus };

            var clientOrder = order.ToClientOrder();

            Assert.AreEqual(expected, clientOrder.Status);
            Assert.AreEqual(internalStatus, order.Status, "Reading the order must not change its internal status");
        }

        [TestMethod]
        [DataRow(OrderStatus.Ready)]
        [DataRow(OrderStatus.ReadyForInternalFinalization)]
        public void ClientStatusIsProcessingOnceFinalizeRequested(OrderStatus internalStatus)
        {
            var order = new AcmeOrder { Status = internalStatus };

            Assert.IsTrue(order.TryMarkFinalizeRequested());

            Assert.AreEqual(OrderStatus.Processing, order.ToClientOrder().Status);
            Assert.AreEqual(internalStatus, order.Status, "The finalize task waits on the internal status, so it must be kept");
        }

        [TestMethod]
        public void FinalizeIsQueuedOnce()
        {
            var order = new AcmeOrder { Status = OrderStatus.ReadyForInternalFinalization };

            Assert.IsTrue(order.TryMarkFinalizeRequested());
            Assert.IsFalse(order.TryMarkFinalizeRequested());

            order.ClearFinalizeRequested();

            Assert.IsTrue(order.TryMarkFinalizeRequested(), "A finalize which could not be queued can be retried");
        }
    }
}
