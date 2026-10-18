using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Certify.Server.Hub.Api.Services.Acme
{
    /// <summary>
    /// Queue of ACME order tasks, run concurrently up to a limit so one slow upstream request does not hold up
    /// every other order. A finalize task waits for its order's processing task to complete before it runs.
    /// </summary>
    public class AcmeOrderTaskQueue
    {
        /// <summary>
        /// Default number of order tasks run at once.
        /// </summary>
        public const int DefaultMaxConcurrentTasks = 4;

        private readonly ILogger _logger;
        private readonly Channel<AcmeOrderTask> _channel;
        private readonly SemaphoreSlim _slots;

        /// <summary>
        /// Tasks queued or running per order id, so the stale order sweep leaves orders with work outstanding.
        /// </summary>
        private readonly ConcurrentDictionary<string, int> _pendingTaskCounts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Completes when the order's processing task has run, keyed by order id.
        /// </summary>
        private readonly ConcurrentDictionary<string, Task> _processing = new(StringComparer.OrdinalIgnoreCase);

        public AcmeOrderTaskQueue(ILogger logger, int capacity = 100, int maxConcurrentTasks = DefaultMaxConcurrentTasks)
        {
            _logger = logger;
            _slots = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
            _channel = Channel.CreateBounded<AcmeOrderTask>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        }

        /// <summary>
        /// Queues a task, returns false if the queue is full or closed.
        /// </summary>
        public bool TryEnqueue(AcmeOrderTask task)
        {
            _pendingTaskCounts.AddOrUpdate(task.OrderId, 1, (_, count) => count + 1);

            if (_channel.Writer.TryWrite(task))
            {
                return true;
            }

            CompletePendingTask(task.OrderId);
            return false;
        }

        /// <summary>
        /// True while a task for the order is queued or running.
        /// </summary>
        public bool HasPendingTasks(string orderId) => _pendingTaskCounts.ContainsKey(orderId);

        /// <summary>
        /// Closes the queue, <see cref="RunAsync"/> completes once the queued tasks have run.
        /// </summary>
        public void Complete() => _channel.Writer.TryComplete();

        /// <summary>
        /// Runs queued tasks with the handler until the queue is closed or cancelled, then waits for running tasks.
        /// </summary>
        public async Task RunAsync(Func<AcmeOrderTask, CancellationToken, Task> handler, CancellationToken cancellationToken)
        {
            var running = new List<Task>();

            try
            {
                await foreach (var task in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    await _slots.WaitAsync(cancellationToken);

                    // tasks are dispatched in queue order and an order's processing task is queued before its
                    // finalize task, so the processing task is registered (and holds a slot) before finalize looks
                    TaskCompletionSource? processed = null;
                    Task? processedFirst = null;

                    if (task.Type == AcmeTaskType.ProcessOrder)
                    {
                        processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        _processing[task.OrderId] = processed.Task;
                    }
                    else
                    {
                        _processing.TryGetValue(task.OrderId, out processedFirst);
                    }

                    running.RemoveAll(t => t.IsCompleted);
                    running.Add(RunTaskAsync(task, handler, processedFirst, processed, cancellationToken));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // expected on shutdown
            }

            await Task.WhenAll(running);
        }

        private async Task RunTaskAsync(
            AcmeOrderTask task,
            Func<AcmeOrderTask, CancellationToken, Task> handler,
            Task? processedFirst,
            TaskCompletionSource? processed,
            CancellationToken cancellationToken)
        {
            try
            {
                if (processedFirst != null)
                {
                    await processedFirst;
                }

                await handler(task, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ACME task {TaskType} for order {OrderId}", task.Type, task.OrderId);
            }
            finally
            {
                if (processed != null)
                {
                    _processing.TryRemove(KeyValuePair.Create(task.OrderId, processed.Task));
                    processed.TrySetResult();
                }

                CompletePendingTask(task.OrderId);
                _slots.Release();
            }
        }

        private void CompletePendingTask(string orderId)
        {
            while (_pendingTaskCounts.TryGetValue(orderId, out var count))
            {
                var updated = count <= 1
                    ? _pendingTaskCounts.TryRemove(KeyValuePair.Create(orderId, count))
                    : _pendingTaskCounts.TryUpdate(orderId, count - 1, count);

                if (updated)
                {
                    return;
                }
            }
        }
    }
}
