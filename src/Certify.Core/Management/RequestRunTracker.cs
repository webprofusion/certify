using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Certify.Models;
using Certify.Models.Hub;

namespace Certify.Management
{
    /// <summary>
    /// Tracks each certificate request run from queued to finished: which stage it is in, how long each stage took and
    /// every progress message it produced. Progress messages are stamped with the run's details as they are reported,
    /// so a client can show a run's stages and history rather than only its latest message, and the finished run is
    /// returned as a <see cref="RequestRun"/> for the hub's request history.
    ///
    /// Runs are keyed by managed item, as only one request runs for an item at a time.
    /// </summary>
    public class RequestRunTracker
    {
        /// <summary>
        /// The most messages kept for a run. The first messages (what the run set out to do) and the latest are the
        /// useful ones, so the oldest after the first few are dropped once a run exceeds this.
        /// </summary>
        internal const int MaxMessagesPerRun = 200;

        private const int KeepFirstMessages = 20;

        /// <summary>
        /// A queued run which has not started after this long is assumed abandoned (e.g. its renewal pass timed out)
        /// </summary>
        internal static readonly TimeSpan QueuedRunExpiry = TimeSpan.FromHours(6);

        private readonly ConcurrentDictionary<string, RequestRunContext> _runs = new ConcurrentDictionary<string, RequestRunContext>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<DateTimeOffset> _now;

        public RequestRunTracker(Func<DateTimeOffset> clock = null)
        {
            _now = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Create a new run id. Ids sort in time order.
        /// </summary>
        public static string NewRunId() => $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}".Substring(0, 30);

        /// <summary>
        /// Record that a request for the item has been queued (e.g. by a renewal pass), so the run which follows
        /// carries the pass, what started it and why. Ignored if a run for the item is already in progress.
        /// </summary>
        public void Queue(string itemId, string itemTitle, RequestTrigger trigger, string reason, string batchId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }

            RemoveExpiredQueuedRuns();

            var now = _now();

            _runs.AddOrUpdate(itemId,
                _ => NewContext(itemId, itemTitle, trigger, reason, batchId, queued: now),
                (_, existing) => existing.IsStarted ? existing : NewContext(itemId, itemTitle, trigger, reason, batchId, queued: now));
        }

        /// <summary>
        /// Start the run for an item: the one queued for it if there is one, otherwise a new run
        /// </summary>
        public RequestRunContext Begin(ManagedCertificate item, RequestTrigger trigger, string reason, bool isPreview, bool isRedeploy)
        {
            var now = _now();

            var context = _runs.AddOrUpdate(item.Id,
                _ => NewContext(item.Id, item.Name, trigger, reason, batchId: null, queued: null),
                (_, existing) => existing.IsStarted ? NewContext(item.Id, item.Name, trigger, reason, batchId: null, queued: null) : existing);

            lock (context.Sync)
            {
                context.IsStarted = true;
                context.Started = now;
                context.IsPreview = isPreview;
                context.IsRedeploy = isRedeploy;
                context.ItemTitle = item.Name;

                if (!string.IsNullOrWhiteSpace(reason))
                {
                    context.TriggerReason = reason;
                }

                // a person asking for the request is what started it, whatever queued it
                if (trigger == RequestTrigger.User)
                {
                    context.Trigger = trigger;
                }

                if (context.CurrentStage == RequestStage.Queued)
                {
                    CloseCurrentStage(context, RequestState.Success, now);
                    context.CurrentStage = null;
                }
            }

            return context;
        }

        /// <summary>
        /// Discard a queued run which will not start (e.g. the request was skipped). A run which has started is kept.
        /// </summary>
        public void DiscardQueued(string itemId)
        {
            if (itemId != null && _runs.TryGetValue(itemId, out var context) && !context.IsStarted)
            {
                _runs.TryRemove(itemId, out _);
            }
        }

        /// <summary>
        /// Move the item's run to the given stage, completing the stage it was in
        /// </summary>
        public void SetStage(string itemId, RequestStage stage)
        {
            if (itemId == null || !_runs.TryGetValue(itemId, out var context))
            {
                return;
            }

            lock (context.Sync)
            {
                EnterStage(context, stage, _now());
            }
        }

        /// <summary>
        /// Get the item's current run, if one is queued or in progress
        /// </summary>
        public RequestRunContext GetRun(string itemId)
        {
            return itemId != null && _runs.TryGetValue(itemId, out var context) ? context : null;
        }

        /// <summary>
        /// Record a progress message against the item's run and stamp the message with the run's details. A message
        /// for an item with no run (e.g. a configuration test) is left as it is.
        /// </summary>
        public void Apply(RequestProgressState state)
        {
            var itemId = state?.ManagedCertificate?.Id;

            if (string.IsNullOrEmpty(itemId) || !_runs.TryGetValue(itemId, out var context))
            {
                return;
            }

            lock (context.Sync)
            {
                if (state.CurrentState == RequestState.Queued && !context.IsStarted)
                {
                    // the queued message which created the run
                }
                else if (state.Stage.HasValue && state.Stage != context.CurrentStage)
                {
                    EnterStage(context, state.Stage.Value, _now());
                }

                var current = CurrentStageStatus(context);

                if (current != null)
                {
                    switch (state.CurrentState)
                    {
                        case RequestState.Error:
                        case RequestState.Paused:
                        case RequestState.Warning:
                            current.Status = state.CurrentState;
                            current.Message = state.Message;
                            break;
                    }
                }

                if (state.UserActions?.Any() == true)
                {
                    context.UserActions = state.UserActions;
                }

                if (!string.IsNullOrWhiteSpace(state.Message))
                {
                    AddMessage(context, new RequestRunMessage
                    {
                        Timestamp = state.MessageCreated,
                        State = state.CurrentState,
                        Stage = context.CurrentStage,
                        Message = state.Message
                    });
                }

                Stamp(state, context);
            }
        }

        /// <summary>
        /// Finish the item's run with its outcome, returning the finished run. Returns null if the item has no run.
        /// </summary>
        public RequestRun Complete(string itemId, RequestState outcome, string message)
        {
            if (itemId == null || !_runs.TryRemove(itemId, out var context))
            {
                return null;
            }

            lock (context.Sync)
            {
                var now = _now();

                CloseCurrentStage(context, outcome == RequestState.Success ? RequestState.Success : outcome, now, message);

                // the stage a run stopped at is the first which did not succeed: a request which fails validation can
                // still go on to run deployment tasks configured to run on failure
                var stoppedAt = outcome == RequestState.Success
                    ? null
                    : context.Stages.FirstOrDefault(s => s.Status == RequestState.Error || s.Status == RequestState.Paused)?.Stage
                        ?? context.Stages.LastOrDefault()?.Stage;

                if (!string.IsNullOrWhiteSpace(message))
                {
                    AddMessage(context, new RequestRunMessage { Timestamp = now, State = outcome, Stage = context.CurrentStage, Message = message });
                }

                return new RequestRun
                {
                    RunId = context.RunId,
                    ManagedItemId = context.ItemId,
                    ItemTitle = context.ItemTitle,
                    Trigger = context.Trigger,
                    TriggerReason = context.TriggerReason,
                    BatchId = context.BatchId,
                    Queued = context.Queued,
                    Started = context.Started ?? context.Queued ?? now,
                    Completed = now,
                    Outcome = outcome,
                    StoppedAtStage = stoppedAt,
                    Message = message,
                    IsPreview = context.IsPreview,
                    IsRedeploy = context.IsRedeploy,
                    Stages = CopyStages(context.Stages),
                    Messages = context.Messages.ToList(),
                    UserActions = outcome == RequestState.Paused ? context.UserActions : null
                };
            }
        }

        /// <summary>
        /// Stamp a message reported after a run finished (its final message) with the finished run's details
        /// </summary>
        public static void StampFinal(RequestProgressState state, RequestRun run)
        {
            if (state == null || run == null)
            {
                return;
            }

            state.RunId = run.RunId;
            state.Trigger = run.Trigger;
            state.TriggerReason = run.TriggerReason;
            state.BatchId = run.BatchId;
            state.RunStarted = run.Queued ?? run.Started;
            state.Stage = run.StoppedAtStage ?? run.Stages.LastOrDefault()?.Stage;
            state.Stages = CopyStages(run.Stages);
            state.UserActions = run.UserActions;
            state.IsFinal = true;
        }

        private RequestRunContext NewContext(string itemId, string itemTitle, RequestTrigger trigger, string reason, string batchId, DateTimeOffset? queued)
        {
            var context = new RequestRunContext
            {
                RunId = NewRunId(),
                ItemId = itemId,
                ItemTitle = itemTitle,
                Trigger = trigger,
                TriggerReason = string.IsNullOrWhiteSpace(reason) ? null : reason,
                BatchId = batchId,
                Queued = queued
            };

            if (queued.HasValue)
            {
                EnterStage(context, RequestStage.Queued, queued.Value);
            }

            return context;
        }

        private void RemoveExpiredQueuedRuns()
        {
            var cutoff = _now() - QueuedRunExpiry;

            foreach (var run in _runs.Where(r => !r.Value.IsStarted && r.Value.Queued < cutoff).ToList())
            {
                _runs.TryRemove(run.Key, out _);
            }
        }

        private static RequestStageStatus CurrentStageStatus(RequestRunContext context)
        {
            return context.CurrentStage.HasValue
                ? context.Stages.LastOrDefault(s => s.Stage == context.CurrentStage)
                : null;
        }

        private static void EnterStage(RequestRunContext context, RequestStage stage, DateTimeOffset now)
        {
            if (context.CurrentStage == stage)
            {
                return;
            }

            CloseCurrentStage(context, RequestState.Success, now);

            // a stage entered again (e.g. deployment after a failed validation) continues its existing entry
            var existing = context.Stages.FirstOrDefault(s => s.Stage == stage);

            if (existing != null)
            {
                existing.Completed = null;

                if (existing.Status == RequestState.Success)
                {
                    existing.Status = RequestState.Running;
                }
            }
            else
            {
                context.Stages.Add(new RequestStageStatus { Stage = stage, Status = RequestState.Running, Started = now });
            }

            context.CurrentStage = stage;
        }

        private static void CloseCurrentStage(RequestRunContext context, RequestState outcome, DateTimeOffset now, string message = null)
        {
            var current = CurrentStageStatus(context);

            if (current == null || current.Completed.HasValue)
            {
                return;
            }

            current.Completed = now;

            if (outcome == RequestState.Success)
            {
                // moving on from a stage means it completed. A pause while waiting (e.g. for propagation) or a warning
                // (e.g. one deployment task failed) is kept, an error stays an error
                if (current.Status == null || current.Status == RequestState.Running || current.Status == RequestState.Paused)
                {
                    current.Status = RequestState.Success;
                }
            }
            else
            {
                // the run finished in this stage with this outcome, unless the stage already failed
                if (current.Status != RequestState.Error)
                {
                    current.Status = outcome;
                }

                if (!string.IsNullOrWhiteSpace(message) && outcome != RequestState.Success)
                {
                    current.Message ??= message;
                }
            }
        }

        private static void AddMessage(RequestRunContext context, RequestRunMessage message)
        {
            var last = context.Messages.LastOrDefault();

            // the same message is reported again by some paths (e.g. a final state reported twice), which adds nothing
            if (last != null && last.State == message.State && last.Message == message.Message)
            {
                return;
            }

            context.Messages.Add(message);

            if (context.Messages.Count > MaxMessagesPerRun)
            {
                context.Messages.RemoveAt(KeepFirstMessages);
            }
        }

        private static void Stamp(RequestProgressState state, RequestRunContext context)
        {
            state.RunId = context.RunId;
            state.Trigger = context.Trigger;
            state.TriggerReason = context.TriggerReason;
            state.BatchId = context.BatchId;
            state.RunStarted = context.Queued ?? context.Started;
            state.Stage = context.CurrentStage;
            state.Stages = CopyStages(context.Stages);

            if (state.UserActions == null && state.CurrentState == RequestState.Paused)
            {
                state.UserActions = context.UserActions;
            }
        }

        private static List<RequestStageStatus> CopyStages(IEnumerable<RequestStageStatus> stages)
        {
            return stages.Select(s => new RequestStageStatus
            {
                Stage = s.Stage,
                Status = s.Status,
                Message = s.Message,
                Started = s.Started,
                Completed = s.Completed
            }).ToList();
        }
    }

    /// <summary>
    /// The state of one request run while it is queued or in progress
    /// </summary>
    public class RequestRunContext
    {
        internal object Sync { get; } = new object();

        public string RunId { get; set; }
        public string ItemId { get; set; }
        public string ItemTitle { get; set; }
        public RequestTrigger Trigger { get; set; }
        public string TriggerReason { get; set; }
        public string BatchId { get; set; }
        public DateTimeOffset? Queued { get; set; }
        public DateTimeOffset? Started { get; set; }
        public bool IsStarted { get; set; }
        public bool IsPreview { get; set; }
        public bool IsRedeploy { get; set; }
        public RequestStage? CurrentStage { get; set; }
        public List<RequestStageStatus> Stages { get; } = new List<RequestStageStatus>();
        public List<RequestRunMessage> Messages { get; } = new List<RequestRunMessage>();
        public List<RequestUserAction> UserActions { get; set; }
    }
}
