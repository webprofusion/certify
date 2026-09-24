using System;
using System.Collections.Generic;
using Certify.Models.Reporting;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Broad grouping of activity events, used to filter the activity feed
    /// </summary>
    public enum ActivityCategory
    {
        /// <summary>
        /// Certificate request runs
        /// </summary>
        Request = 0,

        /// <summary>
        /// Things that happen to a certificate between request runs, such as a renewal being deferred or a revocation
        /// being found
        /// </summary>
        Certificate = 1,

        /// <summary>
        /// Managed instances connecting, disconnecting, changing version or reporting problems
        /// </summary>
        Instance = 2,

        /// <summary>
        /// The hub itself
        /// </summary>
        Hub = 3,

        /// <summary>
        /// Changes people made, such as editing or removing a managed certificate
        /// </summary>
        Change = 4
    }

    /// <summary>
    /// The event types recorded in the hub activity history
    /// </summary>
    public static class ActivityEventTypes
    {
        /// <summary>
        /// A certificate request run finished, with its outcome
        /// </summary>
        public const string RequestCompleted = "request.completed";

        /// <summary>
        /// A scheduled (or requested) renewal pass finished, with its totals
        /// </summary>
        public const string RenewalPassCompleted = "renewal.pass.completed";

        /// <summary>
        /// A renewal was due but deferred or skipped, e.g. outside the maintenance window or the target site is stopped
        /// </summary>
        public const string RenewalDeferred = "certificate.renewal.deferred";

        /// <summary>
        /// The certificate was found to be revoked (or no longer valid) by an OCSP check, so renewal was brought forward
        /// </summary>
        public const string CertificateRevoked = "certificate.revoked";

        /// <summary>
        /// The certificate authority suggested an earlier renewal (ARI), so renewal was brought forward
        /// </summary>
        public const string RenewalBroughtForward = "certificate.renewal.expedited";

        public const string InstanceJoined = "instance.joined";
        public const string InstanceRemoved = "instance.removed";
        public const string InstanceConnected = "instance.connected";
        public const string InstanceDisconnected = "instance.disconnected";

        /// <summary>
        /// A connected instance stopped sending its regular heartbeat
        /// </summary>
        public const string InstanceUnresponsive = "instance.unresponsive";

        /// <summary>
        /// A connected instance resumed sending its regular heartbeat
        /// </summary>
        public const string InstanceResponsive = "instance.responsive";

        public const string InstanceVersionChanged = "instance.version.changed";
        public const string InstanceLicenseChanged = "instance.license.changed";

        /// <summary>
        /// An instance reported a problem with itself (data store unavailable, hub connection failing etc)
        /// </summary>
        public const string InstanceProblemRaised = "instance.problem.raised";

        /// <summary>
        /// An instance reported a previously raised problem is resolved
        /// </summary>
        public const string InstanceProblemCleared = "instance.problem.cleared";

        public const string HubStarted = "hub.started";
        public const string HubStopped = "hub.stopped";

        /// <summary>
        /// The hub told the instances using a shared (subscription) certificate that a new version is available
        /// </summary>
        public const string SubscriptionPushed = "hub.subscription.pushed";

        public const string ItemAdded = "change.item.added";
        public const string ItemUpdated = "change.item.updated";
        public const string ItemRemoved = "change.item.removed";
        public const string ItemRequested = "change.item.requested";
        public const string ItemStatusReset = "change.item.reset";
        public const string ItemTaskExecuted = "change.item.task";
    }

    /// <summary>
    /// Something that happened on a managed instance or the hub, recorded in the hub activity history
    /// </summary>
    public class ActivityEvent
    {
        /// <summary>
        /// Unique id, which sorts in time order
        /// </summary>
        public string Id { get; set; } = string.Empty;

        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// The instance the event relates to. Set by the hub from the connection it arrived on.
        /// </summary>
        public string? InstanceId { get; set; }

        /// <summary>
        /// The managed certificate the event relates to, if any
        /// </summary>
        public string? ManagedItemId { get; set; }

        /// <summary>
        /// The managed certificate's name at the time of the event
        /// </summary>
        public string? ItemTitle { get; set; }

        /// <summary>
        /// The request run the event relates to, if any
        /// </summary>
        public string? RunId { get; set; }

        /// <summary>
        /// The renewal pass the event relates to, if any, so the events of one pass can be grouped
        /// </summary>
        public string? BatchId { get; set; }

        public ActivityCategory Category { get; set; }

        /// <summary>
        /// One of <see cref="ActivityEventTypes"/>
        /// </summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// Outcome or severity of the event: Success, Error, Warning, Paused (waiting on a person), Skipped or
        /// NotRunning for purely informational events
        /// </summary>
        public RequestState Status { get; set; }

        /// <summary>
        /// For a request run, the stage it finished (or failed) in
        /// </summary>
        public RequestStage? Stage { get; set; }

        /// <summary>
        /// One readable sentence describing the event
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Further detail, such as the failure message
        /// </summary>
        public string? Detail { get; set; }

        /// <summary>
        /// Who caused the event, for changes made by people
        /// </summary>
        public string? Actor { get; set; }

        /// <summary>
        /// Distinguishes events of the same type for the same subject, e.g. the status item key of an instance problem
        /// </summary>
        public string? Key { get; set; }

        /// <summary>
        /// Additional values specific to the event type (counts, dates, versions etc)
        /// </summary>
        public Dictionary<string, string>? Data { get; set; }

        /// <summary>
        /// True for events which are problems a person should look at
        /// </summary>
        public bool IsProblem => Status == RequestState.Error || Status == RequestState.Warning || Status == RequestState.Paused;
    }

    /// <summary>
    /// Keys used in <see cref="ActivityEvent.Data"/>
    /// </summary>
    public static class ActivityDataKeys
    {
        public const string DateExpiry = "dateExpiry";
        public const string DurationSeconds = "durationSeconds";
        public const string FailureCount = "failureCount";
        public const string CertificateIssued = "certificateIssued";
        public const string Trigger = "trigger";
        public const string Attempted = "attempted";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
        public const string Paused = "paused";
        public const string Deferred = "deferred";
        public const string PreviousValue = "previous";
        public const string CurrentValue = "current";
        public const string Reason = "reason";
    }

    /// <summary>
    /// Criteria for querying the hub activity history. Results are newest first.
    /// </summary>
    public class ActivityQuery
    {
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }

        public string? InstanceId { get; set; }
        public string? ManagedItemId { get; set; }
        public string? RunId { get; set; }
        public string? BatchId { get; set; }

        /// <summary>
        /// Limit results to these categories, or all categories when empty
        /// </summary>
        public List<ActivityCategory>? Categories { get; set; }

        /// <summary>
        /// Limit results to problems (errors, warnings and requests waiting on a person)
        /// </summary>
        public bool ProblemsOnly { get; set; }

        /// <summary>
        /// Text to match against the event title, detail and item title
        /// </summary>
        public string? Keyword { get; set; }

        /// <summary>
        /// Limit item events to items matching these tag scopes ("category" or "category=value"). Instance events are
        /// limited to instances whose own tags match, and hub events are not shown when tag scopes are set.
        /// </summary>
        public List<string>? TagScopes { get; set; }

        public bool RequireAllTags { get; set; }

        public int PageIndex { get; set; }

        public int PageSize { get; set; } = 50;
    }

    public class ActivityQueryResult
    {
        public ICollection<ActivityEvent> Results { get; set; } = new List<ActivityEvent>();
        public long TotalResults { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }

    /// <summary>
    /// Criteria for daily totals of request run outcomes
    /// </summary>
    public class ActivityTotalsQuery
    {
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }

        /// <summary>
        /// The caller's offset from UTC in minutes, so days are counted in the caller's local time
        /// </summary>
        public int UtcOffsetMinutes { get; set; }

        public string? InstanceId { get; set; }
        public List<string>? TagScopes { get; set; }
        public bool RequireAllTags { get; set; }
    }

    /// <summary>
    /// Request run outcomes for one day
    /// </summary>
    public class ActivityDailyTotal
    {
        /// <summary>
        /// The day, as yyyy-MM-dd in the caller's local time
        /// </summary>
        public string Date { get; set; } = string.Empty;

        public int Renewed { get; set; }
        public int Failed { get; set; }
        public int Paused { get; set; }
        public int Deferred { get; set; }
    }

    /// <summary>
    /// One attempt at a certificate request for a managed item, from queued to finished
    /// </summary>
    public class RequestRun
    {
        public string RunId { get; set; } = string.Empty;
        public string? InstanceId { get; set; }
        public string? ManagedItemId { get; set; }
        public string? ItemTitle { get; set; }

        public RequestTrigger Trigger { get; set; }
        public string? TriggerReason { get; set; }

        /// <summary>
        /// For a request a person started, who started it where known
        /// </summary>
        public string? TriggeredBy { get; set; }

        public string? BatchId { get; set; }

        public DateTimeOffset? Queued { get; set; }
        public DateTimeOffset Started { get; set; }
        public DateTimeOffset? Completed { get; set; }

        /// <summary>
        /// Outcome of the run: Success, Error, Paused (waiting on a person), Warning or Skipped. Running while in progress.
        /// </summary>
        public RequestState Outcome { get; set; }

        /// <summary>
        /// The stage the run failed or paused in, if it did not succeed
        /// </summary>
        public RequestStage? StoppedAtStage { get; set; }

        /// <summary>
        /// True if a new certificate was obtained, even if a later stage (e.g. a deployment task) failed
        /// </summary>
        public bool CertificateIssued { get; set; }

        /// <summary>
        /// Final message of the run
        /// </summary>
        public string? Message { get; set; }

        public bool IsPreview { get; set; }

        /// <summary>
        /// True if the run deployed the certificate the item already held rather than requesting a new one
        /// </summary>
        public bool IsRedeploy { get; set; }

        /// <summary>
        /// Consecutive failures for the item after this run
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Expiry of the item's certificate after this run
        /// </summary>
        public DateTimeOffset? CertificateExpiry { get; set; }

        public List<RequestStageStatus> Stages { get; set; } = new List<RequestStageStatus>();

        public List<RequestRunMessage> Messages { get; set; } = new List<RequestRunMessage>();

        public List<RequestUserAction>? UserActions { get; set; }

        /// <summary>
        /// Duration of the run in seconds, from starting (not queuing) to finishing
        /// </summary>
        public double? DurationSeconds => Completed.HasValue ? (Completed.Value - Started).TotalSeconds : (double?)null;
    }

    /// <summary>
    /// One progress message of a request run
    /// </summary>
    public class RequestRunMessage
    {
        public DateTimeOffset Timestamp { get; set; }
        public RequestState State { get; set; }
        public RequestStage? Stage { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Criteria for querying request run history. Results are newest first and do not include each run's messages.
    /// </summary>
    public class RequestRunQuery
    {
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }
        public string? InstanceId { get; set; }
        public string? ManagedItemId { get; set; }
        public string? BatchId { get; set; }

        /// <summary>
        /// Limit results to these outcomes, or all when empty
        /// </summary>
        public List<RequestState>? Outcomes { get; set; }

        /// <summary>
        /// Text to match against the item title and final message
        /// </summary>
        public string? Keyword { get; set; }

        public List<string>? TagScopes { get; set; }
        public bool RequireAllTags { get; set; }

        public int PageIndex { get; set; }
        public int PageSize { get; set; } = 50;
    }

    public class RequestRunQueryResult
    {
        public ICollection<RequestRun> Results { get; set; } = new List<RequestRun>();
        public long TotalResults { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }

    /// <summary>
    /// Common filter for hub overview queries: an optional instance and optional item tag scopes
    /// </summary>
    public class HubViewFilter
    {
        public string? InstanceId { get; set; }

        /// <summary>
        /// Limit to items matching these tag scopes ("category" or "category=value")
        /// </summary>
        public List<string>? TagScopes { get; set; }

        public bool RequireAllTags { get; set; }
    }

    /// <summary>
    /// The kinds of thing shown as needing attention
    /// </summary>
    public static class AttentionKinds
    {
        /// <summary>
        /// A request is paused waiting on a person, e.g. to create a DNS record
        /// </summary>
        public const string RequestWaiting = "request.waiting";

        /// <summary>
        /// Renewal has failed repeatedly, or has failed and the certificate expires soon
        /// </summary>
        public const string CertificateFailing = "certificate.failing";

        /// <summary>
        /// The certificate was issued but deployment (binding or a deployment task) failed
        /// </summary>
        public const string DeploymentFailing = "certificate.deployment";

        /// <summary>
        /// The certificate expires soon and no renewal will happen in time, or its renewal is due and has not completed
        /// </summary>
        public const string CertificateExpiring = "certificate.expiring";

        /// <summary>
        /// The certificate has been revoked and not yet replaced
        /// </summary>
        public const string CertificateRevoked = "certificate.revoked";

        public const string InstanceDisconnected = "instance.disconnected";
        public const string InstanceUnresponsive = "instance.unresponsive";
        public const string InstanceLicense = "instance.license";
        public const string InstanceProblem = "instance.problem";
    }

    /// <summary>
    /// Something a person should look at: one entry per certificate or instance, with the latest reason
    /// </summary>
    public class AttentionItem
    {
        /// <summary>
        /// Stable key for the thing needing attention, so a client can track it between refreshes
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// One of <see cref="AttentionKinds"/>
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>
        /// Error, Warning or Paused (waiting on a person)
        /// </summary>
        public RequestState Severity { get; set; }

        public string Title { get; set; } = string.Empty;
        public string? Detail { get; set; }

        public string? InstanceId { get; set; }
        public string? InstanceTitle { get; set; }
        public string? ManagedItemId { get; set; }
        public string? ItemTitle { get; set; }

        /// <summary>
        /// When the condition began, where known
        /// </summary>
        public DateTimeOffset? Since { get; set; }

        public DateTimeOffset? DateExpiry { get; set; }
        public int? FailureCount { get; set; }

        /// <summary>
        /// For a request waiting on a person, what they need to do
        /// </summary>
        public List<RequestUserAction>? UserActions { get; set; }
    }

    /// <summary>
    /// Criteria for the upcoming renewal plan
    /// </summary>
    public class UpcomingRenewalsQuery : HubViewFilter
    {
        /// <summary>
        /// Number of days ahead to include, including today
        /// </summary>
        public int Days { get; set; } = 14;

        /// <summary>
        /// The caller's offset from UTC in minutes, so days are counted in the caller's local time
        /// </summary>
        public int UtcOffsetMinutes { get; set; }
    }

    public class UpcomingRenewals
    {
        public List<UpcomingRenewalDay> Days { get; set; } = new List<UpcomingRenewalDay>();

        /// <summary>
        /// Certificates which expire within the period before their next planned renewal attempt
        /// </summary>
        public List<UpcomingRenewalItem> ExpiringBeforeRenewal { get; set; } = new List<UpcomingRenewalItem>();

        /// <summary>
        /// Renewals already due (planned before today) which have not yet been attempted, e.g. held for a
        /// maintenance window or after repeated failures
        /// </summary>
        public int Overdue { get; set; }
    }

    public class UpcomingRenewalDay
    {
        /// <summary>
        /// The day, as yyyy-MM-dd in the caller's local time
        /// </summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>
        /// Renewal attempts planned for the day
        /// </summary>
        public int Planned { get; set; }

        /// <summary>
        /// Of the planned attempts, those held for a maintenance window which opens that day
        /// </summary>
        public int InMaintenanceWindow { get; set; }

        /// <summary>
        /// Certificates expiring that day before a renewal is planned
        /// </summary>
        public int ExpiringBeforeRenewal { get; set; }

        /// <summary>
        /// The items planned for the day (limited to a sample)
        /// </summary>
        public List<UpcomingRenewalItem> Items { get; set; } = new List<UpcomingRenewalItem>();
    }

    public class UpcomingRenewalItem
    {
        public string? InstanceId { get; set; }
        public string? InstanceTitle { get; set; }
        public string? ManagedItemId { get; set; }
        public string? Title { get; set; }
        public DateTimeOffset? DateNextAttempt { get; set; }
        public DateTimeOffset? DateExpiry { get; set; }
        public string? Reason { get; set; }
        public bool IsDeferredByMaintenanceWindow { get; set; }
        public bool IsOnHold { get; set; }
    }

    /// <summary>
    /// The certificate status counts as they stood about a day ago, for showing how the current counts have changed
    /// </summary>
    public class StatusSummaryBaseline
    {
        /// <summary>
        /// False when no baseline applies, e.g. the hub has not been recording for a day yet, or the caller's view is
        /// filtered (tags or scope restrictions) so whole-instance counts would not compare
        /// </summary>
        public bool IsAvailable { get; set; }

        /// <summary>
        /// When the baseline counts were taken
        /// </summary>
        public DateTimeOffset? Taken { get; set; }

        public StatusSummary? Summary { get; set; }
    }

    /// <summary>
    /// Criteria for managed instance connection history
    /// </summary>
    public class InstanceConnectionQuery
    {
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }
        public string? InstanceId { get; set; }
    }

    /// <summary>
    /// When a managed instance was connected, unresponsive or disconnected over a period
    /// </summary>
    public class InstanceConnectionHistory
    {
        public string InstanceId { get; set; } = string.Empty;
        public string? Title { get; set; }

        /// <summary>
        /// Current connection status (see <see cref="ConnectionStatus"/>)
        /// </summary>
        public string? CurrentStatus { get; set; }

        /// <summary>
        /// When the current status began, where known
        /// </summary>
        public DateTimeOffset? CurrentStatusSince { get; set; }

        public List<InstanceConnectionSegment> Segments { get; set; } = new List<InstanceConnectionSegment>();
    }

    public class InstanceConnectionSegment
    {
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }

        /// <summary>
        /// See <see cref="ConnectionStatus"/>: connected, away (connected but not reporting) or disconnected. Null
        /// where nothing is known (e.g. before the hub started recording).
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Reason for a disconnection, where known
        /// </summary>
        public string? Detail { get; set; }
    }
}
