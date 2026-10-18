using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.Services.Acme;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// How hub Managed ACME order tasks are run, and when an order is old enough to be swept.
    /// </summary>
    [TestClass]
    public class AcmeOrderTaskQueueTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [TestMethod]
        [Description("A slow order does not hold up the orders queued after it")]
        public async Task OrdersAreProcessedConcurrently()
        {
            var queue = new AcmeOrderTaskQueue(NullLogger.Instance, maxConcurrentTasks: 2);
            var handler = new BlockingHandler();
            using var cts = new CancellationTokenSource();
            var run = queue.RunAsync(handler.Handle, cts.Token);

            Assert.IsTrue(queue.TryEnqueue(ProcessTask("slow")));
            Assert.IsTrue(queue.TryEnqueue(ProcessTask("next")));

            await handler.WaitForStarted("slow");
            await handler.WaitForStarted("next");

            handler.Release("slow");
            handler.Release("next");
            queue.Complete();
            await run.WaitAsync(Timeout);
        }

        [TestMethod]
        [Description("An order's finalize task runs only once its processing task has completed")]
        public async Task FinalizeRunsAfterItsOrderIsProcessed()
        {
            var queue = new AcmeOrderTaskQueue(NullLogger.Instance, maxConcurrentTasks: 4);
            var handler = new BlockingHandler();
            using var cts = new CancellationTokenSource();
            var run = queue.RunAsync(handler.Handle, cts.Token);

            queue.TryEnqueue(ProcessTask("order"));
            queue.TryEnqueue(FinalizeTask("order"));
            queue.TryEnqueue(ProcessTask("other"));

            await handler.WaitForStarted("order");
            await handler.WaitForStarted("other");

            Assert.IsFalse(handler.HasStarted("order", AcmeTaskType.FinalizeOrder), "finalize waits for processing");

            handler.Release("order");

            await handler.WaitForStarted("order", AcmeTaskType.FinalizeOrder);

            handler.Release("other");
            queue.Complete();
            await run.WaitAsync(Timeout);

            CollectionAssert.AreEqual(
                new[] { AcmeTaskType.ProcessOrder, AcmeTaskType.FinalizeOrder },
                handler.Completed.Where(t => t.OrderId == "order").Select(t => t.Type).ToArray());
        }

        [TestMethod]
        [Description("An order has pending work from being queued until its tasks have run")]
        public async Task PendingTasksAreTrackedUntilRun()
        {
            var queue = new AcmeOrderTaskQueue(NullLogger.Instance);
            var handler = new BlockingHandler();

            queue.TryEnqueue(ProcessTask("order"));
            Assert.IsTrue(queue.HasPendingTasks("order"), "a queued task is pending before it runs");

            using var cts = new CancellationTokenSource();
            var run = queue.RunAsync(handler.Handle, cts.Token);

            await handler.WaitForStarted("order");
            Assert.IsTrue(queue.HasPendingTasks("order"), "a running task is pending");

            handler.Release("order");
            queue.Complete();
            await run.WaitAsync(Timeout);

            Assert.IsFalse(queue.HasPendingTasks("order"));
        }

        [TestMethod]
        [Description("A task which throws does not stop the queue")]
        public async Task FailedTaskDoesNotStopTheQueue()
        {
            var queue = new AcmeOrderTaskQueue(NullLogger.Instance, maxConcurrentTasks: 1);
            var completed = new ConcurrentBag<string>();
            using var cts = new CancellationTokenSource();

            var run = queue.RunAsync((task, _) =>
            {
                if (task.OrderId == "fails")
                {
                    throw new InvalidOperationException("upstream failed");
                }

                completed.Add(task.OrderId);
                return Task.CompletedTask;
            }, cts.Token);

            queue.TryEnqueue(ProcessTask("fails"));
            queue.TryEnqueue(ProcessTask("next"));
            queue.Complete();
            await run.WaitAsync(Timeout);

            CollectionAssert.AreEqual(new[] { "next" }, completed.ToArray());
            Assert.IsFalse(queue.HasPendingTasks("fails"));
        }

        [TestMethod]
        [Description("An order expires a full order lifetime after its last status change, not after it was created")]
        public async Task OrderExpiryIsMeasuredFromItsLastChange()
        {
            var config = new AcmeServerConfig(new FakeConfigurationStore(), "acme-tests");
            var orderId = Guid.NewGuid().ToString("N");
            var order = new AcmeOrder { Id = orderId, Status = OrderStatus.Ready, CreatedAt = DateTime.UtcNow.AddHours(-1) };

            try
            {
                await config.StoreAcmeOrder(orderId, order);
                Assert.IsTrue(order.Expires > DateTime.UtcNow.Add(AcmeBackgroundTaskService.OrderMaxAge).AddMinutes(-1));
                Assert.IsFalse(IsStale(config, orderId), "an order created long ago but just changed is not stale");

                order.Expires = DateTime.UtcNow.AddSeconds(-1);
                Assert.IsTrue(IsStale(config, orderId), "an order unchanged for its lifetime is stale");

                order.Status = OrderStatus.Valid;
                await config.StoreAcmeOrder(orderId, order);
                Assert.IsFalse(IsStale(config, orderId), "a newly valid order stays downloadable");
            }
            finally
            {
                await config.RemoveAcmeOrder(orderId);
            }
        }

        private static bool IsStale(AcmeServerConfig config, string orderId) => config.GetStaleAcmeOrders().Any(o => o.Id == orderId);

        private static AcmeOrderTask ProcessTask(string orderId) => new() { Type = AcmeTaskType.ProcessOrder, OrderId = orderId };

        private static AcmeOrderTask FinalizeTask(string orderId) => new() { Type = AcmeTaskType.FinalizeOrder, OrderId = orderId };

        /// <summary>
        /// Holds each task open until the test releases its order, recording which tasks started and completed.
        /// </summary>
        private sealed class BlockingHandler
        {
            private readonly ConcurrentDictionary<(string, AcmeTaskType), TaskCompletionSource> _started = new();
            private readonly ConcurrentDictionary<string, TaskCompletionSource> _released = new();
            private readonly object _completedLock = new();

            public List<AcmeOrderTask> Completed { get; } = [];

            public async Task Handle(AcmeOrderTask task, CancellationToken cancellationToken)
            {
                Started(task.OrderId, task.Type).TrySetResult();

                if (task.Type == AcmeTaskType.ProcessOrder)
                {
                    await Released(task.OrderId).Task.WaitAsync(cancellationToken);
                }

                lock (_completedLock)
                {
                    Completed.Add(task);
                }
            }

            public Task WaitForStarted(string orderId, AcmeTaskType type = AcmeTaskType.ProcessOrder)
                => Started(orderId, type).Task.WaitAsync(Timeout);

            public bool HasStarted(string orderId, AcmeTaskType type) => Started(orderId, type).Task.IsCompleted;

            public void Release(string orderId) => Released(orderId).TrySetResult();

            private TaskCompletionSource Started(string orderId, AcmeTaskType type)
                => _started.GetOrAdd((orderId, type), _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            private TaskCompletionSource Released(string orderId)
                => _released.GetOrAdd(orderId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }
}
