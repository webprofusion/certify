using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        /// <summary>
        /// Tracks each request run's stages and messages, see <see cref="RequestRunTracker"/>
        /// </summary>
        private readonly RequestRunTracker _runTracker = new RequestRunTracker();

        /// <summary>
        /// How long the same deferral of the same item is not reported again. A scheduled pass runs every few minutes
        /// and would otherwise report an item waiting for its maintenance window on every pass.
        /// </summary>
        internal static readonly TimeSpan DeferralReportInterval = TimeSpan.FromHours(24);

        private readonly ConcurrentDictionary<string, (string Reason, DateTimeOffset Reported)> _reportedDeferrals = new ConcurrentDictionary<string, (string, DateTimeOffset)>();

        /// <summary>
        /// Activity raised before there is a management hub connection to send it through (e.g. a problem found during
        /// startup), sent once there is one
        /// </summary>
        private readonly ConcurrentQueue<(string CommandType, object Message)> _pendingHubActivity = new ConcurrentQueue<(string, object)>();

        private const int MaxPendingHubActivity = 50;

        /// <summary>
        /// Status items describing the hub connection itself. The hub records connections and disconnections as it
        /// sees them, so these are not reported as instance problems as well.
        /// </summary>
        private static readonly HashSet<string> _hubConnectionStatusKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SystemStatusKeys.SERVICE_CORE_HUB_CONNECTION,
            SystemStatusKeys.SERVICE_CORE_HUB_JOINING_KEY,
            SystemStatusKeys.SERVICE_CORE_HUB_JOINING_AUTH
        };

        /// <summary>
        /// Current renewal pass, if one is running
        /// </summary>
        private RenewalPassActivity _currentRenewalPass;

        /// <summary>
        /// Move the item's current request run to the given stage
        /// </summary>
        private void EnterRequestStage(ManagedCertificate managedCertificate, RequestStage stage)
        {
            _runTracker.SetStage(managedCertificate?.Id, stage);
        }

        /// <summary>
        /// Send an activity event to the management hub's activity history. Held while disconnected and sent on
        /// reconnect, so a problem which caused (or occurred during) a disconnection is not lost.
        /// </summary>
        private void ReportActivityToMgmtHub(ActivityEvent activityEvent)
        {
            if (activityEvent == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(activityEvent.Id))
            {
                activityEvent.Id = RequestRunTracker.NewRunId();
            }

            if (activityEvent.Timestamp == default)
            {
                activityEvent.Timestamp = DateTimeOffset.UtcNow;
            }

            QueueActivityNotification(ManagementHubCommands.NotificationActivityEvent, activityEvent);
        }

        private void QueueActivityNotification(string commandType, object message)
        {
            try
            {
                var client = _managementServerClient;

                if (client != null)
                {
                    client.QueueNotificationToManagementHub(commandType, message);
                }
                else
                {
                    _pendingHubActivity.Enqueue((commandType, message));

                    while (_pendingHubActivity.Count > MaxPendingHubActivity)
                    {
                        _pendingHubActivity.TryDequeue(out _);
                    }
                }
            }
            catch (Exception ex)
            {
                _serviceLog?.Warning("Failed to send activity to the management hub: {msg}", ex.Message);
            }
        }

        /// <summary>
        /// Send activity held from before there was a management hub client
        /// </summary>
        private void FlushPendingHubActivity()
        {
            var client = _managementServerClient;

            if (client == null)
            {
                return;
            }

            while (_pendingHubActivity.TryDequeue(out var pending))
            {
                try
                {
                    client.QueueNotificationToManagementHub(pending.CommandType, pending.Message);
                }
                catch (Exception ex)
                {
                    _serviceLog?.Warning("Failed to send held activity to the management hub: {msg}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Report a finished request run to the hub's request history, with an activity event describing its outcome
        /// </summary>
        private void ReportRequestRunCompleted(RequestRun run, ManagedCertificate managedCertificate, CertificateRequestResult result, bool hadCertificateBefore)
        {
            if (run == null)
            {
                return;
            }

            _currentRenewalPass?.RecordRun(run);

            // a preview did no work, so there is nothing to record
            if (run.IsPreview)
            {
                return;
            }

            run.CertificateIssued = result?.PrimaryRequest?.Status == RequestState.Success && !run.IsRedeploy
                || (managedCertificate.IsSubscription && run.Outcome == RequestState.Success && result?.IsSubscriptionUpdateDeferred != true);
            run.CertificateExpiry = managedCertificate.DateExpiry;
            run.FailureCount = managedCertificate.RenewalFailureCount;
            run.ItemTitle = managedCertificate.Name;

            // a subscription whose source had nothing new did no work worth a history entry
            if (managedCertificate.IsSubscription && !run.IsRedeploy && result?.IsSubscriptionUpdateDeferred == true && run.Outcome != RequestState.Error)
            {
                return;
            }

            QueueActivityNotification(ManagementHubCommands.NotificationRequestRun, run);

            ReportActivityToMgmtHub(BuildRequestCompletedEvent(run, managedCertificate, hadCertificateBefore));
        }

        /// <summary>
        /// Describe a finished request run as an activity event
        /// </summary>
        internal static ActivityEvent BuildRequestCompletedEvent(RequestRun run, ManagedCertificate managedCertificate, bool hadCertificateBefore)
        {
            var title = run.ItemTitle ?? managedCertificate?.Name ?? "Certificate";
            var stageName = RequestStageInfo.GetDisplayName(run.StoppedAtStage).ToLower(CultureInfo.InvariantCulture);

            string eventTitle;

            switch (run.Outcome)
            {
                case RequestState.Success:
                    eventTitle = run.IsRedeploy
                        ? $"Deployed {title} again"
                        : hadCertificateBefore ? $"Renewed {title}" : $"Certificate issued for {title}";
                    break;

                case RequestState.Paused:
                    eventTitle = $"{title} is waiting for you";
                    break;

                case RequestState.Error:
                    if (run.CertificateIssued)
                    {
                        eventTitle = $"{title} was renewed, but deployment failed";
                    }
                    else
                    {
                        eventTitle = $"{title} failed at {stageName}";

                        if (run.FailureCount > 1)
                        {
                            eventTitle += $" ({run.FailureCount} failures in a row)";
                        }
                    }

                    break;

                default:
                    eventTitle = $"{title} request did not run";
                    break;
            }

            var data = new Dictionary<string, string>
            {
                { ActivityDataKeys.Trigger, run.Trigger.ToString() },
                { ActivityDataKeys.FailureCount, run.FailureCount.ToString(CultureInfo.InvariantCulture) },
                { ActivityDataKeys.CertificateIssued, run.CertificateIssued ? "true" : "false" }
            };

            if (run.DurationSeconds.HasValue)
            {
                data[ActivityDataKeys.DurationSeconds] = Math.Round(run.DurationSeconds.Value).ToString(CultureInfo.InvariantCulture);
            }

            if (run.CertificateExpiry.HasValue)
            {
                data[ActivityDataKeys.DateExpiry] = run.CertificateExpiry.Value.ToString("o", CultureInfo.InvariantCulture);
            }

            return new ActivityEvent
            {
                Id = RequestRunTracker.NewRunId(),
                Timestamp = run.Completed ?? DateTimeOffset.UtcNow,
                ManagedItemId = run.ManagedItemId,
                ItemTitle = title,
                RunId = run.RunId,
                BatchId = run.BatchId,
                Category = ActivityCategory.Request,
                EventType = ActivityEventTypes.RequestCompleted,
                Status = run.Outcome,
                Stage = run.StoppedAtStage,
                Title = eventTitle,
                Detail = run.Outcome == RequestState.Success && run.CertificateExpiry.HasValue && !run.IsRedeploy
                    ? $"Valid until {run.CertificateExpiry.Value.UtcDateTime:yyyy-MM-dd}"
                    : run.Message,
                Data = data
            };
        }

        /// <summary>
        /// Report a renewal which was due but deferred or skipped (e.g. outside the maintenance window). The same
        /// deferral is reported at most once per <see cref="DeferralReportInterval"/>.
        /// </summary>
        private void ReportRenewalDeferred(ManagedCertificate item, string reason)
        {
            if (item?.Id == null)
            {
                return;
            }

            _currentRenewalPass?.RecordDeferred();

            var now = DateTimeOffset.UtcNow;

            if (_reportedDeferrals.TryGetValue(item.Id, out var reported)
                && reported.Reason == reason
                && now - reported.Reported < DeferralReportInterval)
            {
                return;
            }

            _reportedDeferrals[item.Id] = (reason, now);

            ReportActivityToMgmtHub(new ActivityEvent
            {
                ManagedItemId = item.Id,
                ItemTitle = item.Name,
                BatchId = _currentRenewalPass?.BatchId,
                Category = ActivityCategory.Certificate,
                EventType = ActivityEventTypes.RenewalDeferred,
                Status = RequestState.Skipped,
                Title = $"Renewal of {item.Name} deferred",
                Detail = reason,
                Data = new Dictionary<string, string> { { ActivityDataKeys.Reason, reason ?? string.Empty } }
            });
        }

        /// <summary>
        /// Report a change to one of this instance's status items between healthy and a problem (error or warning)
        /// </summary>
        private void ReportSystemStatusChange(string key, string title, string description, bool wasProblem, bool hasError, bool hasWarning)
        {
            var isProblem = hasError || hasWarning;

            if (wasProblem == isProblem || _hubConnectionStatusKeys.Contains(key))
            {
                return;
            }

            ReportActivityToMgmtHub(new ActivityEvent
            {
                Category = ActivityCategory.Instance,
                EventType = isProblem ? ActivityEventTypes.InstanceProblemRaised : ActivityEventTypes.InstanceProblemCleared,
                Status = hasError ? RequestState.Error : hasWarning ? RequestState.Warning : RequestState.Success,
                Key = key,
                Title = isProblem ? $"Problem: {title}" : $"Resolved: {title}",
                Detail = description
            });
        }

        /// <summary>
        /// Report a certificate whose renewal was brought forward by a status check (OCSP revocation or a CA
        /// suggested renewal window)
        /// </summary>
        private void ReportRenewalBroughtForward(ManagedCertificate item, string reason, bool isRevocation)
        {
            ReportActivityToMgmtHub(new ActivityEvent
            {
                ManagedItemId = item.Id,
                ItemTitle = item.Name,
                Category = ActivityCategory.Certificate,
                EventType = isRevocation ? ActivityEventTypes.CertificateRevoked : ActivityEventTypes.RenewalBroughtForward,
                Status = isRevocation ? RequestState.Warning : RequestState.NotRunning,
                Title = isRevocation
                    ? $"{item.Name} is no longer valid: renewal brought forward"
                    : $"Renewal of {item.Name} brought forward",
                Detail = reason,
                Data = new Dictionary<string, string> { { ActivityDataKeys.Reason, reason ?? string.Empty } }
            });
        }

        /// <summary>
        /// Begin tracking a renewal pass, so its runs are grouped and its totals reported when it finishes
        /// </summary>
        private RenewalPassActivity BeginRenewalPass(RequestTrigger trigger)
        {
            var pass = new RenewalPassActivity(RequestRunTracker.NewRunId(), trigger);
            _currentRenewalPass = pass;
            return pass;
        }

        /// <summary>
        /// Finish tracking a renewal pass, reporting its totals if it attempted anything
        /// </summary>
        private void EndRenewalPass(RenewalPassActivity pass)
        {
            if (pass == null)
            {
                return;
            }

            if (_currentRenewalPass == pass)
            {
                _currentRenewalPass = null;
            }

            if (pass.Attempted == 0)
            {
                // passes run every few minutes and most have nothing to do; deferrals are reported individually
                return;
            }

            var parts = new List<string>();

            if (pass.Succeeded > 0)
            {
                parts.Add($"{pass.Succeeded} renewed");
            }

            if (pass.Failed > 0)
            {
                parts.Add($"{pass.Failed} failed");
            }

            if (pass.Paused > 0)
            {
                parts.Add($"{pass.Paused} waiting for you");
            }

            if (pass.Deferred > 0)
            {
                parts.Add($"{pass.Deferred} deferred");
            }

            var passName = pass.Trigger == RequestTrigger.Schedule ? "Scheduled renewal" : "Renewal";

            ReportActivityToMgmtHub(new ActivityEvent
            {
                BatchId = pass.BatchId,
                Category = ActivityCategory.Request,
                EventType = ActivityEventTypes.RenewalPassCompleted,
                Status = pass.Failed > 0 ? RequestState.Error : pass.Paused > 0 ? RequestState.Paused : RequestState.Success,
                Title = $"{passName} finished: {(parts.Any() ? string.Join(", ", parts) : $"{pass.Attempted} attempted")}",
                Data = new Dictionary<string, string>
                {
                    { ActivityDataKeys.Trigger, pass.Trigger.ToString() },
                    { ActivityDataKeys.Attempted, pass.Attempted.ToString(CultureInfo.InvariantCulture) },
                    { ActivityDataKeys.Succeeded, pass.Succeeded.ToString(CultureInfo.InvariantCulture) },
                    { ActivityDataKeys.Failed, pass.Failed.ToString(CultureInfo.InvariantCulture) },
                    { ActivityDataKeys.Paused, pass.Paused.ToString(CultureInfo.InvariantCulture) },
                    { ActivityDataKeys.Deferred, pass.Deferred.ToString(CultureInfo.InvariantCulture) },
                    { ActivityDataKeys.DurationSeconds, Math.Round((DateTimeOffset.UtcNow - pass.Started).TotalSeconds).ToString(CultureInfo.InvariantCulture) }
                }
            });
        }

        /// <summary>
        /// Running totals for a renewal pass
        /// </summary>
        internal class RenewalPassActivity
        {
            private int _attempted, _succeeded, _failed, _paused, _deferred;

            public RenewalPassActivity(string batchId, RequestTrigger trigger)
            {
                BatchId = batchId;
                Trigger = trigger;
                Started = DateTimeOffset.UtcNow;
            }

            public string BatchId { get; }
            public RequestTrigger Trigger { get; }
            public DateTimeOffset Started { get; }

            public int Attempted => Volatile.Read(ref _attempted);
            public int Succeeded => Volatile.Read(ref _succeeded);
            public int Failed => Volatile.Read(ref _failed);
            public int Paused => Volatile.Read(ref _paused);
            public int Deferred => Volatile.Read(ref _deferred);

            public void RecordDeferred() => Interlocked.Increment(ref _deferred);

            public void RecordRun(RequestRun run)
            {
                if (run.BatchId != BatchId)
                {
                    return;
                }

                Interlocked.Increment(ref _attempted);

                switch (run.Outcome)
                {
                    case RequestState.Success:
                        Interlocked.Increment(ref _succeeded);
                        break;
                    case RequestState.Paused:
                        Interlocked.Increment(ref _paused);
                        break;
                    case RequestState.Error:
                    case RequestState.Warning:
                        Interlocked.Increment(ref _failed);
                        break;
                }
            }
        }
    }
}
