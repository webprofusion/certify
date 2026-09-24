using System.Globalization;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.SignalR.ManagementHub;

namespace Certify.Server.Hub.Api.Services.Activity
{
    /// <summary>
    /// Answers the hub overview and history queries: the activity feed, request run history, daily totals, what needs
    /// attention, upcoming renewals and instance connection history. Everything is limited to what the caller may see:
    /// managed item activity to the items they may list (tag scopes and domain restrictions), instance and hub
    /// activity to those who may list managed instances.
    /// </summary>
    public class HubActivityService
    {
        /// <summary>
        /// A connected instance which has not sent a heartbeat for this long is treated as not responding. Instances
        /// send a heartbeat every 30 seconds.
        /// </summary>
        internal static readonly TimeSpan UnresponsiveAfter = TimeSpan.FromMinutes(3);

        /// <summary>
        /// How far ahead an expiring certificate needs attention if nothing will renew it in time, or if a single failed
        /// renewal leaves little time to retry
        /// </summary>
        internal static readonly TimeSpan ExpiryAttentionWindow = TimeSpan.FromDays(14);

        /// <summary>
        /// A certificate expiring this soon needs attention once its renewal is due, even while an attempt is still planned
        /// </summary>
        internal static readonly TimeSpan ExpiryImminent = TimeSpan.FromDays(3);

        /// <summary>
        /// The certificate lifetime the expiry windows are sized for. A shorter lived certificate is always within days of
        /// expiry, so its windows shrink in proportion to its lifetime
        /// </summary>
        internal static readonly TimeSpan TypicalLifetime = TimeSpan.FromDays(90);

        private static readonly string[] _connectionEventTypes =
        [
            ActivityEventTypes.InstanceConnected,
            ActivityEventTypes.InstanceDisconnected,
            ActivityEventTypes.InstanceUnresponsive,
            ActivityEventTypes.InstanceResponsive
        ];

        private static readonly string[] _hubLifecycleEventTypes =
        [
            ActivityEventTypes.HubStarted,
            ActivityEventTypes.HubStopped
        ];

        private readonly ActivityStore _store;
        private readonly IInstanceManagementStateProvider _stateProvider;
        private readonly ICertifyInternalApiClient _client;
        private readonly ILogger<HubActivityService>? _logger;

        /// <summary>
        /// Constructor
        /// </summary>
        public HubActivityService(ActivityStore store, IInstanceManagementStateProvider stateProvider, ICertifyInternalApiClient client, ILogger<HubActivityService>? logger = null)
        {
            _store = store;
            _stateProvider = stateProvider;
            _client = client;
            _logger = logger;
        }

        /// <summary>
        /// What a caller may see, and the filter they have applied
        /// </summary>
        internal sealed class ViewScope
        {
            public ManagedItemVisibility Visibility { get; init; } = ManagedItemVisibility.None;
            public bool CanListInstances { get; init; }
            public bool IsTagFiltered { get; init; }

            /// <summary>
            /// The currently known items the caller may see (and which match their tag filter), keyed by item id
            /// </summary>
            public Dictionary<string, (string InstanceId, ManagedCertificate Item)> VisibleItems { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// The instances whose own tags match the tag filter, when one is applied
            /// </summary>
            public HashSet<string> MatchingInstanceIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// True when the visible items have to be checked for each record, rather than every item record being visible
            /// </summary>
            public bool IsItemRestricted => !Visibility.IsUnrestricted || IsTagFiltered;

            /// <summary>
            /// Whether the caller may see a stored record
            /// </summary>
            public bool Permits(ActivityRecordScope record)
            {
                if (!string.IsNullOrEmpty(record.ManagedItemId))
                {
                    // an item no longer known (e.g. since removed) can only be shown to someone who may see every item
                    return IsItemRestricted ? VisibleItems.ContainsKey(record.ManagedItemId) : Visibility.HasAction;
                }

                // instance activity follows the instance's own tags, while hub activity has none, so a tag filter leaves it out
                return CanListInstances && (!IsTagFiltered || (record.InstanceId != null && MatchingInstanceIds.Contains(record.InstanceId)));
            }

            /// <summary>
            /// The filter to apply to stored records, or null when every record is visible
            /// </summary>
            public Func<ActivityRecordScope, bool>? RecordFilter => !IsItemRestricted && CanListInstances ? null : Permits;
        }

        internal async Task<ViewScope> ResolveScopeAsync(AuthContext? authContext, List<string>? tagScopes, bool requireAllTags, string? instanceId = null)
        {
            var visibility = await ManagedItemVisibility.Resolve(_client, authContext);

            var canListInstances = await PrincipalAccess.IsAuthorized(_client, authContext,
                new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList));

            var scopes = TagScopeFilter.ParseAll(tagScopes);
            var visibleItems = new Dictionary<string, (string, ManagedCertificate)>(StringComparer.OrdinalIgnoreCase);

            if (visibility.HasAction)
            {
                var needsTags = scopes.Count > 0 || visibility.RequiresTags;
                var tagsByItem = needsTags ? await GetItemTagsByItemIdAsync(TaggedItemTypes.ManagedCertificate) : new Dictionary<string, List<ItemTag>>();

                foreach (var instanceItems in _stateProvider.GetManagedInstanceItems().Values)
                {
                    if (!string.IsNullOrWhiteSpace(instanceId) && !string.Equals(instanceId, instanceItems.InstanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (var item in instanceItems.Items ?? [])
                    {
                        if (string.IsNullOrWhiteSpace(item.Id))
                        {
                            continue;
                        }

                        var tags = tagsByItem.TryGetValue(item.Id, out var itemTags) ? itemTags : [];

                        if (scopes.Count > 0 && !TagScopeFilter.Matches(tags, scopes, requireAllTags))
                        {
                            continue;
                        }

                        if (!visibility.Permits(tags, item.GetCertificateIdentifiers().Select(i => i.Value)))
                        {
                            continue;
                        }

                        visibleItems[item.Id] = (instanceItems.InstanceId, item);
                    }
                }
            }

            var matchingInstanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (scopes.Count > 0 && canListInstances)
            {
                // instance tags are keyed by the instance record id, not the instance id
                var instanceTags = await GetItemTagsByItemIdAsync(TaggedItemTypes.ManagedInstance);

                foreach (var instance in await GetKnownInstancesAsync())
                {
                    if (instanceTags.TryGetValue(instance.Id ?? "", out var tags) && TagScopeFilter.Matches(tags, scopes, requireAllTags))
                    {
                        matchingInstanceIds.Add(instance.InstanceId);
                    }
                }
            }

            return new ViewScope
            {
                Visibility = visibility,
                CanListInstances = canListInstances,
                IsTagFiltered = scopes.Count > 0,
                VisibleItems = visibleItems,
                MatchingInstanceIds = matchingInstanceIds
            };
        }

        private async Task<Dictionary<string, List<ItemTag>>> GetItemTagsByItemIdAsync(string itemType)
        {
            var result = new Dictionary<string, List<ItemTag>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var tags = await _client.GetAllHubItemTags(null, null, itemType, null, PrincipalAccess.SystemAuthContext);

                foreach (var tag in tags ?? [])
                {
                    if (!result.TryGetValue(tag.TaggedItemId, out var list))
                    {
                        list = [];
                        result[tag.TaggedItemId] = list;
                    }

                    list.Add(tag);
                }
            }
            catch (Exception ex)
            {
                // without tags a tag scoped caller sees no items, which is the safe outcome
                _logger?.LogWarning(ex, "Could not read item tags to scope activity.");
            }

            return result;
        }

        /// <summary>
        /// The activity feed
        /// </summary>
        public async Task<ActivityQueryResult> GetActivityAsync(ActivityQuery query, AuthContext? authContext)
        {
            query ??= new ActivityQuery();

            var scope = await ResolveScopeAsync(authContext, query.TagScopes, query.RequireAllTags, query.InstanceId);

            var (results, total) = await _store.QueryEventsAsync(query, scope.RecordFilter);

            return new ActivityQueryResult
            {
                Results = results,
                TotalResults = total,
                PageIndex = Math.Max(0, query.PageIndex),
                PageSize = query.PageSize
            };
        }

        /// <summary>
        /// Request run history (without each run's messages)
        /// </summary>
        public async Task<RequestRunQueryResult> GetRequestRunsAsync(RequestRunQuery query, AuthContext? authContext)
        {
            query ??= new RequestRunQuery();

            var scope = await ResolveScopeAsync(authContext, query.TagScopes, query.RequireAllTags, query.InstanceId);

            var (results, total) = await _store.QueryRunsAsync(query, scope.IsItemRestricted ? scope.Permits : null);

            return new RequestRunQueryResult
            {
                Results = results,
                TotalResults = total,
                PageIndex = Math.Max(0, query.PageIndex),
                PageSize = query.PageSize
            };
        }

        /// <summary>
        /// A single request run with its messages, if the caller may see it
        /// </summary>
        public async Task<RequestRun?> GetRequestRunAsync(string runId, AuthContext? authContext)
        {
            var run = await _store.GetRunAsync(runId);

            if (run == null)
            {
                return null;
            }

            var scope = await ResolveScopeAsync(authContext, null, false);

            return scope.Permits(new ActivityRecordScope(run.InstanceId, run.ManagedItemId, ActivityCategory.Request)) ? run : null;
        }

        /// <summary>
        /// Daily totals of request outcomes and deferred renewals
        /// </summary>
        public async Task<ICollection<ActivityDailyTotal>> GetActivityTotalsAsync(ActivityTotalsQuery query, AuthContext? authContext)
        {
            query ??= new ActivityTotalsQuery();

            var offset = TimeSpan.FromMinutes(Math.Clamp(query.UtcOffsetMinutes, -14 * 60, 14 * 60));
            var now = DateTimeOffset.UtcNow;

            var toDay = (query.To ?? now).ToOffset(offset).Date;
            var fromDay = (query.From ?? now.AddDays(-29)).ToOffset(offset).Date;

            if (fromDay > toDay)
            {
                (fromDay, toDay) = (toDay, fromDay);
            }

            // at most a year of days
            if ((toDay - fromDay).TotalDays > 366)
            {
                fromDay = toDay.AddDays(-366);
            }

            var from = new DateTimeOffset(fromDay, offset);
            var to = new DateTimeOffset(toDay.AddDays(1), offset);

            var scope = await ResolveScopeAsync(authContext, query.TagScopes, query.RequireAllTags, query.InstanceId);

            var days = new SortedDictionary<DateTime, ActivityDailyTotal>();

            for (var day = fromDay; day <= toDay; day = day.AddDays(1))
            {
                days[day] = new ActivityDailyTotal { Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            }

            bool Include(ActivityRecordScope record) =>
                scope.Permits(record)
                && (string.IsNullOrWhiteSpace(query.InstanceId) || string.Equals(record.InstanceId, query.InstanceId, StringComparison.OrdinalIgnoreCase));

            foreach (var (completed, outcome, record) in await _store.GetRunOutcomesAsync(from, to))
            {
                if (!Include(record) || !days.TryGetValue(completed.ToOffset(offset).Date, out var total))
                {
                    continue;
                }

                switch (outcome)
                {
                    case RequestState.Success:
                        total.Renewed++;
                        break;
                    case RequestState.Paused:
                        total.Paused++;
                        break;
                    case RequestState.Error:
                    case RequestState.Warning:
                        total.Failed++;
                        break;
                }
            }

            foreach (var (timestamp, record) in await _store.GetEventTimesAsync(ActivityEventTypes.RenewalDeferred, from, to))
            {
                if (Include(record) && days.TryGetValue(timestamp.ToOffset(offset).Date, out var total))
                {
                    total.Deferred++;
                }
            }

            return days.Values.ToList();
        }

        /// <summary>
        /// Everything that needs a person's attention: one entry per certificate or instance
        /// </summary>
        public async Task<ICollection<AttentionItem>> GetAttentionItemsAsync(HubViewFilter filter, AuthContext? authContext)
        {
            filter ??= new HubViewFilter();

            var scope = await ResolveScopeAsync(authContext, filter.TagScopes, filter.RequireAllTags, filter.InstanceId);
            var now = DateTimeOffset.UtcNow;

            var knownInstances = await GetKnownInstancesAsync();
            var instanceTitles = knownInstances.ToDictionary(i => i.InstanceId, i => i.DisplayTitle ?? i.InstanceId, StringComparer.OrdinalIgnoreCase);

            var items = new List<AttentionItem>();

            // certificates
            var waitingItemIds = new List<string>();

            foreach (var (itemId, (instanceId, item)) in scope.VisibleItems)
            {
                var attention = GetItemAttention(item, now);

                if (attention == null)
                {
                    continue;
                }

                attention.InstanceId = instanceId;
                attention.InstanceTitle = instanceTitles.TryGetValue(instanceId, out var instanceTitle) ? instanceTitle : instanceId;
                attention.ManagedItemId = itemId;
                attention.ItemTitle = item.Name;
                attention.Key = $"{attention.Kind}:{instanceId}:{itemId}";

                if (attention.Kind == AttentionKinds.RequestWaiting)
                {
                    waitingItemIds.Add(itemId);
                }

                items.Add(attention);
            }

            // what a person must do for a waiting request is recorded with the run which paused
            foreach (var waiting in items.Where(i => i.Kind == AttentionKinds.RequestWaiting))
            {
                var runs = await _store.QueryRunsAsync(new RequestRunQuery
                {
                    ManagedItemId = waiting.ManagedItemId,
                    InstanceId = waiting.InstanceId,
                    Outcomes = [RequestState.Paused],
                    PageSize = 1
                });

                var latest = runs.Results.FirstOrDefault();

                if (latest != null)
                {
                    var run = await _store.GetRunAsync(latest.RunId);
                    waiting.UserActions = run?.UserActions;
                    waiting.Since ??= run?.Completed;
                }
            }

            // instances, where a tag filter keeps those whose own tags match it
            if (scope.CanListInstances)
            {
                var instances = scope.IsTagFiltered ? knownInstances.Where(i => scope.MatchingInstanceIds.Contains(i.InstanceId)).ToList() : knownInstances;

                items.AddRange(await GetInstanceAttentionAsync(instances, filter.InstanceId, now));
            }

            return items
                .OrderBy(i => SeverityRank(i.Severity))
                .ThenByDescending(i => i.Since ?? DateTimeOffset.MinValue)
                .ToList();
        }

        /// <summary>
        /// Whether a certificate needs attention, and why
        /// </summary>
        internal static AttentionItem? GetItemAttention(ManagedCertificate item, DateTimeOffset now)
        {
            var expiry = item.DateExpiry;
            var expiresIn = expiry.HasValue ? expiry.Value - now : (TimeSpan?)null;
            var expiryText = DescribeExpiry(expiry, now);

            var attentionWindow = ExpiryWindowFor(item, ExpiryAttentionWindow);
            var imminentWindow = ExpiryWindowFor(item, ExpiryImminent);

            if (item.Health == ManagedCertificateHealth.AwaitingUser || item.LastRenewalStatus == RequestState.Paused)
            {
                return new AttentionItem
                {
                    Kind = AttentionKinds.RequestWaiting,
                    Severity = RequestState.Paused,
                    Title = $"{item.Name} is waiting for you",
                    Detail = FirstLine(item.RenewalFailureMessage) ?? "The request is paused until you complete a manual step",
                    Since = item.DateLastRenewalAttempt,
                    DateExpiry = expiry
                };
            }

            // a revoked certificate is rejected by clients until it is replaced, however long it has left to run
            if (item.CertificateRevoked)
            {
                var failing = item.LastRenewalStatus == RequestState.Error;

                return new AttentionItem
                {
                    Kind = AttentionKinds.CertificateRevoked,
                    Severity = RequestState.Error,
                    Title = $"{item.Name} has been revoked",
                    Detail = failing
                        ? FirstLine(item.RenewalFailureMessage)
                        : !item.IncludeInAutoRenew ? "Automatic renewal is off, so it will not be replaced" : null,
                    DateExpiry = expiry,
                    FailureCount = failing ? item.RenewalFailureCount : null
                };
            }

            if (item.LastRenewalStatus == RequestState.Error)
            {
                var certificateIssued = item.LastPrimaryRequest?.Status == RequestState.Success;

                if (certificateIssued)
                {
                    return new AttentionItem
                    {
                        Kind = AttentionKinds.DeploymentFailing,
                        Severity = RequestState.Error,
                        Title = $"{item.Name} was renewed, but deployment failed",
                        Detail = FirstLine(item.RenewalFailureMessage),
                        Since = item.DateLastRenewalAttempt,
                        DateExpiry = expiry,
                        FailureCount = item.RenewalFailureCount
                    };
                }

                var expiringSoon = expiresIn.HasValue && expiresIn.Value < attentionWindow;

                if (item.RenewalFailureCount >= 2 || expiringSoon)
                {
                    var failures = item.RenewalFailureCount > 1 ? $"has failed its last {item.RenewalFailureCount} renewals" : "failed to renew";

                    return new AttentionItem
                    {
                        Kind = AttentionKinds.CertificateFailing,
                        Severity = RequestState.Error,
                        Title = $"{item.Name} {failures}",
                        Detail = string.Join(" · ", new[] { FirstLine(item.RenewalFailureMessage), expiryText }.Where(d => !string.IsNullOrWhiteSpace(d))),
                        Since = item.DateLastRenewalAttempt,
                        DateExpiry = expiry,
                        FailureCount = item.RenewalFailureCount
                    };
                }
            }

            if (expiresIn.HasValue && !item.IsExternallyManaged)
            {
                var plan = item.RenewalPlan;
                var plannedAttempt = plan?.DateNextRenewalAttempt ?? item.DateNextScheduledRenewalAttempt;

                var noRenewalInTime = !item.IncludeInAutoRenew
                    || plan?.IsRenewalOnHold == true
                    || plannedAttempt == null
                    || plannedAttempt > expiry;

                // the plan sets when renewal is due from the certificate's lifetime and the configured renewal threshold.
                // Until then nearing expiry is expected, so a certificate which will be renewed in time only needs
                // attention once renewal is due and still has not completed close to expiry
                var renewalDue = plan?.IsRenewalDue == true || plannedAttempt <= now;

                if (expiresIn.Value < TimeSpan.Zero
                    || (noRenewalInTime && expiresIn.Value < attentionWindow)
                    || (renewalDue && expiresIn.Value < imminentWindow))
                {
                    var why = !item.IncludeInAutoRenew
                        ? "Automatic renewal is off"
                        : plan?.IsRenewalOnHold == true
                            ? "Renewal is on hold after repeated failures"
                            : plannedAttempt > expiry
                                ? $"Next renewal is planned for {plannedAttempt.Value.UtcDateTime:yyyy-MM-dd}, after it expires"
                                : plannedAttempt == null
                                    ? null
                                    : plan?.IsDeferredByMaintenanceWindow == true
                                        ? "Renewal is due and waiting for its maintenance window"
                                        : "Renewal is due but has not completed";

                    return new AttentionItem
                    {
                        Kind = AttentionKinds.CertificateExpiring,
                        Severity = expiresIn.Value < imminentWindow ? RequestState.Error : RequestState.Warning,
                        Title = expiresIn.Value < TimeSpan.Zero
                            ? $"{item.Name} has expired"
                            : noRenewalInTime ? $"{item.Name} {expiryText} and no renewal is planned in time" : $"{item.Name} {expiryText}",
                        Detail = why,
                        DateExpiry = expiry
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// A window before a certificate's expiry, shortened in proportion when its lifetime is shorter than typical
        /// </summary>
        internal static TimeSpan ExpiryWindowFor(ManagedCertificate item, TimeSpan window)
        {
            var lifetime = item.DateExpiry - item.DateStart;

            return lifetime > TimeSpan.Zero && lifetime < TypicalLifetime
                ? window * (lifetime.Value / TypicalLifetime)
                : window;
        }

        private async Task<List<AttentionItem>> GetInstanceAttentionAsync(List<ManagedInstanceInfo> knownInstances, string? instanceId, DateTimeOffset now)
        {
            var items = new List<AttentionItem>();
            var hubInstanceId = _stateProvider.GetManagementHubInstanceId();

            var connected = _stateProvider.GetConnectedInstances()
                .Where(c => !string.IsNullOrWhiteSpace(c.InstanceId))
                .GroupBy(c => c.InstanceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.DateLastReported).First(), StringComparer.OrdinalIgnoreCase);

            var lastConnectionEvents = (await _store.GetLatestEventsByInstanceAsync(_connectionEventTypes))
                .Where(e => e.InstanceId != null)
                .ToDictionary(e => e.InstanceId!, StringComparer.OrdinalIgnoreCase);

            foreach (var instance in knownInstances)
            {
                if (string.IsNullOrWhiteSpace(instance.InstanceId)
                    || instance.IsPendingConnection
                    || string.Equals(instance.InstanceId, hubInstanceId, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(instanceId) && !string.Equals(instanceId, instance.InstanceId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var title = instance.DisplayTitle ?? instance.InstanceId;

                if (!connected.TryGetValue(instance.InstanceId, out var connection))
                {
                    lastConnectionEvents.TryGetValue(instance.InstanceId, out var lastEvent);

                    var since = lastEvent?.EventType == ActivityEventTypes.InstanceDisconnected ? lastEvent.Timestamp : instance.DateLastReported;

                    items.Add(new AttentionItem
                    {
                        Key = $"{AttentionKinds.InstanceDisconnected}:{instance.InstanceId}",
                        Kind = AttentionKinds.InstanceDisconnected,
                        Severity = RequestState.Error,
                        Title = $"{title} is disconnected",
                        Detail = lastEvent?.EventType == ActivityEventTypes.InstanceDisconnected
                            ? lastEvent.Detail
                            : "Its certificates are not being reported to the hub",
                        InstanceId = instance.InstanceId,
                        InstanceTitle = title,
                        Since = since
                    });
                }
                else if (now - connection.DateLastReported > UnresponsiveAfter)
                {
                    items.Add(new AttentionItem
                    {
                        Key = $"{AttentionKinds.InstanceUnresponsive}:{instance.InstanceId}",
                        Kind = AttentionKinds.InstanceUnresponsive,
                        Severity = RequestState.Warning,
                        Title = $"{title} has stopped responding",
                        Detail = "Connected, but not sending its regular heartbeat",
                        InstanceId = instance.InstanceId,
                        InstanceTitle = title,
                        Since = connection.DateLastReported
                    });
                }

                var license = connection?.License ?? instance.License;

                if (license?.DateExpiry != null)
                {
                    var licenseExpiry = new DateTimeOffset(DateTime.SpecifyKind(license.DateExpiry.Value, DateTimeKind.Utc));

                    if (licenseExpiry < now || (license.IsValid && licenseExpiry - now < ExpiryAttentionWindow))
                    {
                        items.Add(new AttentionItem
                        {
                            Key = $"{AttentionKinds.InstanceLicense}:{instance.InstanceId}",
                            Kind = AttentionKinds.InstanceLicense,
                            Severity = RequestState.Warning,
                            Title = licenseExpiry < now ? $"{title} licence has expired" : $"{title} licence {DescribeExpiry(licenseExpiry, now)}",
                            InstanceId = instance.InstanceId,
                            InstanceTitle = title,
                            DateExpiry = licenseExpiry
                        });
                    }
                }
            }

            // problems instances have reported with themselves and not yet reported resolved
            var problemEvents = await _store.GetEventsAsync(
                now.AddDays(-30), now.AddMinutes(1),
                [ActivityEventTypes.InstanceProblemRaised, ActivityEventTypes.InstanceProblemCleared],
                instanceId);

            var openProblems = problemEvents
                .GroupBy(e => $"{e.InstanceId}|{e.Key}")
                .Select(g => g.Last())
                .Where(e => e.EventType == ActivityEventTypes.InstanceProblemRaised && e.InstanceId != null);

            var knownIds = knownInstances.Select(i => i.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var problem in openProblems)
            {
                if (!knownIds.Contains(problem.InstanceId!))
                {
                    continue;
                }

                var instance = knownInstances.First(i => string.Equals(i.InstanceId, problem.InstanceId, StringComparison.OrdinalIgnoreCase));

                items.Add(new AttentionItem
                {
                    Key = $"{AttentionKinds.InstanceProblem}:{problem.InstanceId}:{problem.Key}",
                    Kind = AttentionKinds.InstanceProblem,
                    Severity = problem.Status == RequestState.Error ? RequestState.Error : RequestState.Warning,
                    Title = $"{instance.DisplayTitle ?? problem.InstanceId}: {problem.Title.Replace("Problem: ", "", StringComparison.Ordinal)}",
                    Detail = problem.Detail,
                    InstanceId = problem.InstanceId,
                    InstanceTitle = instance.DisplayTitle,
                    Since = problem.Timestamp
                });
            }

            return items;
        }

        /// <summary>
        /// Renewals planned over the coming days, and certificates which will expire before a renewal is planned
        /// </summary>
        public async Task<UpcomingRenewals> GetUpcomingRenewalsAsync(UpcomingRenewalsQuery query, AuthContext? authContext)
        {
            query ??= new UpcomingRenewalsQuery();

            var scope = await ResolveScopeAsync(authContext, query.TagScopes, query.RequireAllTags, query.InstanceId);

            var knownInstances = await GetKnownInstancesAsync();
            var instanceTitles = knownInstances.ToDictionary(i => i.InstanceId, i => i.DisplayTitle ?? i.InstanceId, StringComparer.OrdinalIgnoreCase);

            return BuildUpcomingRenewals(scope.VisibleItems.Values, instanceTitles, query, DateTimeOffset.UtcNow);
        }

        internal static UpcomingRenewals BuildUpcomingRenewals(
            IEnumerable<(string InstanceId, ManagedCertificate Item)> items,
            IReadOnlyDictionary<string, string> instanceTitles,
            UpcomingRenewalsQuery query,
            DateTimeOffset now)
        {
            const int maxItemsPerDay = 25;

            var offset = TimeSpan.FromMinutes(Math.Clamp(query.UtcOffsetMinutes, -14 * 60, 14 * 60));
            var dayCount = Math.Clamp(query.Days <= 0 ? 14 : query.Days, 1, 90);

            var today = now.ToOffset(offset).Date;
            var lastDay = today.AddDays(dayCount - 1);
            var periodEnd = new DateTimeOffset(lastDay.AddDays(1), offset);

            var result = new UpcomingRenewals();
            var days = new Dictionary<DateTime, UpcomingRenewalDay>();

            for (var day = today; day <= lastDay; day = day.AddDays(1))
            {
                var entry = new UpcomingRenewalDay { Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
                days[day] = entry;
                result.Days.Add(entry);
            }

            foreach (var (instanceId, item) in items)
            {
                if (item.IsExternallyManaged)
                {
                    continue;
                }

                var plan = item.RenewalPlan;
                var planned = plan?.DateNextRenewalAttempt ?? item.DateNextScheduledRenewalAttempt;

                // a renewal due now is attempted on the next pass, unless it is being held
                if (plan?.IsRenewalDue == true && !plan.IsDeferredByMaintenanceWindow && !plan.IsRenewalOnHold)
                {
                    planned = now;
                }

                var entry = new UpcomingRenewalItem
                {
                    InstanceId = instanceId,
                    InstanceTitle = instanceTitles.TryGetValue(instanceId, out var instanceTitle) ? instanceTitle : instanceId,
                    ManagedItemId = item.Id,
                    Title = item.Name,
                    DateNextAttempt = planned,
                    DateExpiry = item.DateExpiry,
                    Reason = plan?.Reason,
                    IsDeferredByMaintenanceWindow = plan?.IsDeferredByMaintenanceWindow == true
                        || plan?.Reason?.Contains("maintenance window", StringComparison.OrdinalIgnoreCase) == true,
                    IsOnHold = plan?.IsRenewalOnHold == true
                };

                if (item.IncludeInAutoRenew && planned.HasValue)
                {
                    var plannedDay = planned.Value.ToOffset(offset).Date;

                    if (plannedDay < today)
                    {
                        // due before today and still not attempted (on hold or waiting for a window)
                        result.Overdue++;
                    }
                    else if (days.TryGetValue(plannedDay, out var day))
                    {
                        day.Planned++;

                        if (entry.IsDeferredByMaintenanceWindow)
                        {
                            day.InMaintenanceWindow++;
                        }

                        if (day.Items.Count < maxItemsPerDay)
                        {
                            day.Items.Add(entry);
                        }
                    }
                }

                var expiresBeforeRenewal = item.DateExpiry.HasValue
                    && item.DateExpiry.Value < periodEnd
                    && (!item.IncludeInAutoRenew || planned == null || planned > item.DateExpiry || plan?.IsRenewalOnHold == true);

                if (expiresBeforeRenewal)
                {
                    result.ExpiringBeforeRenewal.Add(entry);

                    var expiryDay = item.DateExpiry!.Value.ToOffset(offset).Date;

                    if (days.TryGetValue(expiryDay < today ? today : expiryDay, out var day))
                    {
                        day.ExpiringBeforeRenewal++;
                    }
                }
            }

            result.ExpiringBeforeRenewal = result.ExpiringBeforeRenewal.OrderBy(e => e.DateExpiry).ToList();

            return result;
        }

        /// <summary>
        /// When each instance was connected, not responding or disconnected over a period
        /// </summary>
        public async Task<ICollection<InstanceConnectionHistory>> GetInstanceConnectionHistoryAsync(InstanceConnectionQuery query, AuthContext? authContext)
        {
            query ??= new InstanceConnectionQuery();

            var canListInstances = await PrincipalAccess.IsAuthorized(_client, authContext,
                new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList));

            if (!canListInstances)
            {
                return [];
            }

            var now = DateTimeOffset.UtcNow;
            var to = query.To.HasValue && query.To.Value < now ? query.To.Value : now;
            var from = query.From ?? to.AddHours(-24);

            if (from >= to)
            {
                from = to.AddHours(-24);
            }

            var knownInstances = (await GetKnownInstancesAsync())
                .Where(i => !i.IsPendingConnection && !string.IsNullOrWhiteSpace(i.InstanceId))
                .Where(i => string.IsNullOrWhiteSpace(query.InstanceId) || string.Equals(i.InstanceId, query.InstanceId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var events = await _store.GetEventsAsync(from, to, _connectionEventTypes.Concat(_hubLifecycleEventTypes), instanceId: null);
            var initialEvents = await _store.GetLatestEventsByInstanceAsync(_connectionEventTypes, before: from);
            var lastHubEventBefore = (await _store.GetLatestEventsByInstanceAsync(_hubLifecycleEventTypes, before: from)).OrderBy(e => e.Timestamp).LastOrDefault();

            var connected = _stateProvider.GetConnectedInstances()
                .Where(c => !string.IsNullOrWhiteSpace(c.InstanceId))
                .GroupBy(c => c.InstanceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.DateLastReported).First(), StringComparer.OrdinalIgnoreCase);

            var hubInstanceId = _stateProvider.GetManagementHubInstanceId();

            return knownInstances
                .Select(instance => string.Equals(instance.InstanceId, hubInstanceId, StringComparison.OrdinalIgnoreCase)
                    ? BuildUnrecordedHistory(instance, from, to)
                    : BuildConnectionHistory(
                        instance,
                        initialEvents.FirstOrDefault(e => string.Equals(e.InstanceId, instance.InstanceId, StringComparison.OrdinalIgnoreCase)),
                        lastHubEventBefore,
                        events,
                        connected.TryGetValue(instance.InstanceId, out var c) ? c : null,
                        from,
                        to))
                .OrderBy(h => h.Title)
                .ToList();
        }

        /// <summary>
        /// The hub's own instance runs within the hub rather than connecting to it, so it has no connection history
        /// </summary>
        internal static InstanceConnectionHistory BuildUnrecordedHistory(ManagedInstanceInfo instance, DateTimeOffset from, DateTimeOffset to)
        {
            return new InstanceConnectionHistory
            {
                InstanceId = instance.InstanceId,
                Title = instance.DisplayTitle ?? instance.InstanceId,
                Segments = [new InstanceConnectionSegment { Start = from, End = to }]
            };
        }

        internal static InstanceConnectionHistory BuildConnectionHistory(
            ManagedInstanceInfo instance,
            ActivityEvent? stateBefore,
            ActivityEvent? hubEventBefore,
            IEnumerable<ActivityEvent> events,
            ManagedInstanceInfo? currentConnection,
            DateTimeOffset from,
            DateTimeOffset to)
        {
            var history = new InstanceConnectionHistory
            {
                InstanceId = instance.InstanceId,
                Title = instance.DisplayTitle ?? instance.InstanceId
            };

            // the state at the start of the period is the last recorded before it, unless the hub has restarted since
            string? status = StatusFromEvent(stateBefore);
            var statusSince = stateBefore?.Timestamp;

            if (hubEventBefore != null && (stateBefore == null || hubEventBefore.Timestamp >= stateBefore.Timestamp))
            {
                status = hubEventBefore.EventType == ActivityEventTypes.HubStarted ? ConnectionStatus.Disconnected : null;
                statusSince = hubEventBefore.Timestamp;
            }

            var segmentStart = from;
            string? detail = stateBefore?.EventType == ActivityEventTypes.InstanceDisconnected ? stateBefore.Detail : null;

            void Transition(DateTimeOffset at, string? newStatus, string? newDetail)
            {
                if (at > segmentStart && (newStatus != status || at >= to))
                {
                    history.Segments.Add(new InstanceConnectionSegment { Start = segmentStart, End = at, Status = status, Detail = detail });
                    segmentStart = at;
                }

                if (newStatus != status)
                {
                    statusSince = at;
                }

                status = newStatus;
                detail = newDetail;
            }

            foreach (var e in events.OrderBy(e => e.Timestamp))
            {
                if (_hubLifecycleEventTypes.Contains(e.EventType))
                {
                    Transition(e.Timestamp, e.EventType == ActivityEventTypes.HubStarted ? ConnectionStatus.Disconnected : null, null);
                }
                else if (string.Equals(e.InstanceId, instance.InstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    Transition(e.Timestamp, StatusFromEvent(e), e.EventType == ActivityEventTypes.InstanceDisconnected ? e.Detail : null);
                }
            }

            // the current state is what the hub sees now, which is authoritative for the end of the period
            var currentStatus = currentConnection == null
                ? ConnectionStatus.Disconnected
                : to - currentConnection.DateLastReported > UnresponsiveAfter
                    ? ConnectionStatus.Away
                    : ConnectionStatus.Connected;

            if (currentStatus != status)
            {
                // no event recorded the change (e.g. the instance connected before history was being recorded)
                Transition(to, currentStatus, null);
            }

            if (to > segmentStart)
            {
                history.Segments.Add(new InstanceConnectionSegment { Start = segmentStart, End = to, Status = status, Detail = detail });
            }

            history.CurrentStatus = currentStatus;
            history.CurrentStatusSince = statusSince;

            return history;
        }

        private static string? StatusFromEvent(ActivityEvent? e)
        {
            return e?.EventType switch
            {
                ActivityEventTypes.InstanceConnected => ConnectionStatus.Connected,
                ActivityEventTypes.InstanceResponsive => ConnectionStatus.Connected,
                ActivityEventTypes.InstanceUnresponsive => ConnectionStatus.Away,
                ActivityEventTypes.InstanceDisconnected => ConnectionStatus.Disconnected,
                _ => null
            };
        }

        /// <summary>
        /// The certificate status counts as they stood about a day ago
        /// </summary>
        public async Task<StatusSummaryBaseline> GetStatusSummaryBaselineAsync(HubViewFilter filter, AuthContext? authContext)
        {
            filter ??= new HubViewFilter();

            var visibility = await ManagedItemVisibility.Resolve(_client, authContext);

            // whole-instance counts only compare with the current counts for a caller who sees every item
            if (!visibility.IsUnrestricted || filter.TagScopes?.Count > 0)
            {
                return new StatusSummaryBaseline { IsAvailable = false };
            }

            var at = DateTimeOffset.UtcNow.AddDays(-1);
            var snapshots = await _store.GetStatusSnapshotsAtAsync(at);

            var relevant = snapshots
                .Where(s => string.IsNullOrWhiteSpace(filter.InstanceId) || string.Equals(s.Key, filter.InstanceId, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Value)
                .ToList();

            if (relevant.Count == 0)
            {
                return new StatusSummaryBaseline { IsAvailable = false };
            }

            var total = new StatusSummary { InstanceId = filter.InstanceId ?? string.Empty };

            foreach (var s in relevant)
            {
                total.Total += s.Total;
                total.Healthy += s.Healthy;
                total.Error += s.Error;
                total.Warning += s.Warning;
                total.AwaitingUser += s.AwaitingUser;
                total.InvalidConfig += s.InvalidConfig;
                total.NoCertificate += s.NoCertificate;
                total.ExternallyManaged += s.ExternallyManaged;
                total.TotalDomains += s.TotalDomains;
            }

            return new StatusSummaryBaseline { IsAvailable = true, Taken = at, Summary = total };
        }

        /// <summary>
        /// Record each instance's current status counts, at most hourly, so the counts can later be compared with
        /// how they stood a day before
        /// </summary>
        public async Task RecordStatusSnapshotsAsync()
        {
            var now = DateTimeOffset.UtcNow;
            var latest = await _store.GetLatestSnapshotTimesAsync();

            foreach (var (instanceId, summary) in _stateProvider.GetManagedInstanceStatusSummaries())
            {
                if (latest.TryGetValue(instanceId, out var taken) && now - taken < TimeSpan.FromHours(1))
                {
                    continue;
                }

                await _store.AddStatusSnapshotAsync(instanceId, now, summary);
            }
        }

        /// <summary>
        /// Remove history older than the hub's retention period
        /// </summary>
        public async Task<int> PurgeExpiredHistoryAsync()
        {
            var retentionDays = ActivitySettings.DefaultRetentionDays;

            try
            {
                var settings = await _client.GetHubSettings(PrincipalAccess.SystemAuthContext);
                retentionDays = ActivitySettings.ResolveRetentionDays(settings?.Activity);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Could not read hub settings for the activity retention period, using the default.");
            }

            var removed = await _store.PurgeOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-retentionDays));

            if (removed > 0)
            {
                _logger?.LogInformation("Removed {count} activity records older than {days} days.", removed, retentionDays);
            }

            return removed;
        }

        private async Task<List<ManagedInstanceInfo>> GetKnownInstancesAsync()
        {
            try
            {
                return (await _client.GetHubManagedInstances(PrincipalAccess.SystemAuthContext))?.ToList() ?? [];
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not read managed instances.");
                return [];
            }
        }

        private static int SeverityRank(RequestState severity) => severity switch
        {
            RequestState.Error => 0,
            RequestState.Paused => 1,
            _ => 2
        };

        private static string? FirstLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

            return line?.Length > 240 ? line[..237] + "..." : line;
        }

        private static string DescribeExpiry(DateTimeOffset? expiry, DateTimeOffset now)
        {
            if (!expiry.HasValue)
            {
                return string.Empty;
            }

            var remaining = expiry.Value - now;

            if (remaining < TimeSpan.Zero)
            {
                return "has expired";
            }

            if (remaining.TotalHours < 1)
            {
                return "expires within the hour";
            }

            if (remaining.TotalDays < 1)
            {
                var hours = (int)Math.Floor(remaining.TotalHours);

                return hours == 1 ? "expires in 1 hour" : $"expires in {hours} hours";
            }

            var days = (int)Math.Floor(remaining.TotalDays);

            return days == 1 ? "expires in 1 day" : $"expires in {days} days";
        }
    }
}
