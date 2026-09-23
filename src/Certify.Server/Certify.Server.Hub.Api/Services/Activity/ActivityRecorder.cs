using System.Collections.Concurrent;
using System.Globalization;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.SignalR;
using Certify.Server.Hub.Api.SignalR.ManagementHub;

namespace Certify.Server.Hub.Api.Services.Activity
{
    /// <summary>
    /// Records activity in the hub activity history and sends each new event to the UI clients which may see it.
    ///
    /// Events come from managed instances (request runs, deferred renewals, problems they find with themselves) and
    /// from what the hub itself sees (instances connecting and disconnecting, changes people make through the hub).
    /// </summary>
    public class ActivityRecorder
    {
        /// <summary>
        /// How long a person asking for a request is remembered, to credit them with the run which follows
        /// </summary>
        internal static readonly TimeSpan RequestedByExpiry = TimeSpan.FromMinutes(10);

        private readonly ActivityStore _store;
        private readonly UserInterfaceStatusBroadcaster? _broadcaster;
        private readonly IInstanceManagementStateProvider _stateProvider;
        private readonly ICertifyInternalApiClient? _client;
        private readonly ILogger<ActivityRecorder>? _logger;

        private readonly ConcurrentDictionary<string, (string Actor, DateTimeOffset Noted)> _requestedBy = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _instanceTitles = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _principalNames = new(StringComparer.OrdinalIgnoreCase);
        private DateTimeOffset _principalNamesLoaded = DateTimeOffset.MinValue;

        /// <summary>
        /// Constructor
        /// </summary>
        public ActivityRecorder(
            ActivityStore store,
            IInstanceManagementStateProvider stateProvider,
            UserInterfaceStatusBroadcaster? broadcaster = null,
            ICertifyInternalApiClient? client = null,
            ILogger<ActivityRecorder>? logger = null)
        {
            _store = store;
            _stateProvider = stateProvider;
            _broadcaster = broadcaster;
            _client = client;
            _logger = logger;
        }

        /// <summary>
        /// The store holding the activity history
        /// </summary>
        public ActivityStore Store => _store;

        /// <summary>
        /// Record an event and send it to the UI clients which may see it. An event already recorded (e.g. sent again by
        /// an instance after reconnecting) is ignored.
        /// </summary>
        public async Task RecordAsync(ActivityEvent activityEvent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(activityEvent.Id))
                {
                    activityEvent.Id = NewEventId();
                }

                if (activityEvent.Timestamp == default)
                {
                    activityEvent.Timestamp = DateTimeOffset.UtcNow;
                }

                if (string.IsNullOrWhiteSpace(activityEvent.ItemTitle) && !string.IsNullOrWhiteSpace(activityEvent.ManagedItemId))
                {
                    activityEvent.ItemTitle = GetCachedItem(activityEvent.InstanceId, activityEvent.ManagedItemId)?.Name;
                }

                var isNew = await _store.AddEventAsync(activityEvent);

                if (isNew && _broadcaster != null)
                {
                    await _broadcaster.SendActivityEvent(activityEvent);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to record activity event {eventType}.", activityEvent.EventType);
            }
        }

        /// <summary>
        /// Record an event reported by a managed instance. The instance is the one the notification arrived from,
        /// whatever the event claims.
        /// </summary>
        public Task RecordFromInstanceAsync(string instanceId, ActivityEvent activityEvent)
        {
            activityEvent.InstanceId = instanceId;

            // actors are only ever the hub's own principals, credited by the hub
            activityEvent.Actor = null;

            // an event from an instance cannot claim to be a change made through the hub or about the hub itself
            if (activityEvent.Category == ActivityCategory.Change || activityEvent.Category == ActivityCategory.Hub)
            {
                activityEvent.Category = ActivityCategory.Instance;
            }

            return RecordAsync(activityEvent);
        }

        /// <summary>
        /// Record a finished request run reported by a managed instance
        /// </summary>
        public async Task RecordRunFromInstanceAsync(string instanceId, RequestRun run)
        {
            try
            {
                run.InstanceId = instanceId;

                if (string.IsNullOrWhiteSpace(run.RunId))
                {
                    return;
                }

                if (run.Trigger == RequestTrigger.User && string.IsNullOrWhiteSpace(run.TriggeredBy) && !string.IsNullOrWhiteSpace(run.ManagedItemId))
                {
                    run.TriggeredBy = TakeRequestedBy(instanceId, run.ManagedItemId);
                }
                else
                {
                    run.TriggeredBy = null;
                }

                await _store.UpsertRunAsync(run);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to record request run {runId}.", run.RunId);
            }
        }

        /// <summary>
        /// Remember who asked for an item's request, to credit them with the run which follows
        /// </summary>
        public void NoteRequestedBy(string instanceId, string managedItemId, string? actor)
        {
            if (!string.IsNullOrWhiteSpace(actor))
            {
                _requestedBy[$"{instanceId}|{managedItemId}"] = (actor, DateTimeOffset.UtcNow);
            }
        }

        private string? TakeRequestedBy(string instanceId, string managedItemId)
        {
            if (_requestedBy.TryRemove($"{instanceId}|{managedItemId}", out var noted) && DateTimeOffset.UtcNow - noted.Noted < RequestedByExpiry)
            {
                return noted.Actor;
            }

            return null;
        }

        /// <summary>
        /// Remember an instance's display title, for describing its events
        /// </summary>
        public void NoteInstanceTitle(string? instanceId, string? title)
        {
            if (!string.IsNullOrWhiteSpace(instanceId) && !string.IsNullOrWhiteSpace(title))
            {
                _instanceTitles[instanceId] = title;
            }
        }

        /// <summary>
        /// An instance connected to the hub
        /// </summary>
        public async Task InstanceConnectedAsync(string instanceId)
        {
            var title = await GetInstanceTitleAsync(instanceId);

            await RecordAsync(new ActivityEvent
            {
                InstanceId = instanceId,
                Category = ActivityCategory.Instance,
                EventType = ActivityEventTypes.InstanceConnected,
                Status = RequestState.Success,
                Title = $"{title} connected"
            });
        }

        /// <summary>
        /// An instance disconnected from the hub
        /// </summary>
        public async Task InstanceDisconnectedAsync(string instanceId, string? reason)
        {
            var title = await GetInstanceTitleAsync(instanceId);

            await RecordAsync(new ActivityEvent
            {
                InstanceId = instanceId,
                Category = ActivityCategory.Instance,
                EventType = ActivityEventTypes.InstanceDisconnected,
                Status = string.IsNullOrWhiteSpace(reason) ? RequestState.Warning : RequestState.Error,
                Title = $"{title} disconnected",
                Detail = string.IsNullOrWhiteSpace(reason) ? "The instance closed its connection" : $"Connection lost: {reason}"
            });
        }

        /// <summary>
        /// A connected instance stopped (or resumed) sending its regular heartbeat
        /// </summary>
        public async Task InstanceResponsivenessChangedAsync(string instanceId, bool isResponsive, DateTimeOffset? lastSeen)
        {
            var title = await GetInstanceTitleAsync(instanceId);

            await RecordAsync(new ActivityEvent
            {
                InstanceId = instanceId,
                Category = ActivityCategory.Instance,
                EventType = isResponsive ? ActivityEventTypes.InstanceResponsive : ActivityEventTypes.InstanceUnresponsive,
                Status = isResponsive ? RequestState.Success : RequestState.Warning,
                Title = isResponsive ? $"{title} is responding again" : $"{title} has stopped responding",
                Detail = isResponsive || lastSeen == null ? null : $"Last heartbeat {lastSeen.Value.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC"
            });
        }

        /// <summary>
        /// An instance reported its details: record it joining, or any change of version or licence since last time
        /// </summary>
        /// <param name="stored">the instance's details as last stored by the hub, or null if not yet known</param>
        /// <param name="current">the details just reported</param>
        public async Task InstanceInfoReceivedAsync(ManagedInstanceInfo? stored, ManagedInstanceInfo current)
        {
            var instanceId = current.InstanceId;

            NoteInstanceTitle(instanceId, current.DisplayTitle);

            var title = current.DisplayTitle ?? instanceId;

            if (stored == null || stored.IsPendingConnection)
            {
                await RecordAsync(new ActivityEvent
                {
                    InstanceId = instanceId,
                    Category = ActivityCategory.Instance,
                    EventType = ActivityEventTypes.InstanceJoined,
                    Status = RequestState.Success,
                    Title = $"{title} joined the hub",
                    Detail = string.Join(" ", new[] { current.ClientName, current.ClientVersion, current.OS }.Where(p => !string.IsNullOrWhiteSpace(p)))
                });

                return;
            }

            if (!string.IsNullOrWhiteSpace(stored.ClientVersion) && !string.IsNullOrWhiteSpace(current.ClientVersion)
                && !string.Equals(stored.ClientVersion, current.ClientVersion, StringComparison.OrdinalIgnoreCase))
            {
                await RecordAsync(new ActivityEvent
                {
                    InstanceId = instanceId,
                    Category = ActivityCategory.Instance,
                    EventType = ActivityEventTypes.InstanceVersionChanged,
                    Status = RequestState.NotRunning,
                    Title = $"{title} updated from {stored.ClientVersion} to {current.ClientVersion}",
                    Data = new Dictionary<string, string>
                    {
                        { ActivityDataKeys.PreviousValue, stored.ClientVersion! },
                        { ActivityDataKeys.CurrentValue, current.ClientVersion! }
                    }
                });
            }

            var previousLicense = DescribeLicense(stored.License);
            var currentLicense = DescribeLicense(current.License);

            if (stored.License != null && current.License != null && previousLicense.IsValid != currentLicense.IsValid)
            {
                await RecordAsync(new ActivityEvent
                {
                    InstanceId = instanceId,
                    Category = ActivityCategory.Instance,
                    EventType = ActivityEventTypes.InstanceLicenseChanged,
                    Status = currentLicense.IsValid ? RequestState.Success : RequestState.Warning,
                    Title = currentLicense.IsValid ? $"{title} licence is now valid" : $"{title} licence is no longer valid",
                    Detail = currentLicense.Description,
                    Data = new Dictionary<string, string>
                    {
                        { ActivityDataKeys.PreviousValue, previousLicense.Description },
                        { ActivityDataKeys.CurrentValue, currentLicense.Description }
                    }
                });
            }
        }

        /// <summary>
        /// An instance was removed from the hub
        /// </summary>
        public async Task InstanceRemovedAsync(string instanceId, AuthContext? authContext)
        {
            var title = await GetInstanceTitleAsync(instanceId);

            await RecordAsync(new ActivityEvent
            {
                InstanceId = instanceId,
                Category = ActivityCategory.Instance,
                EventType = ActivityEventTypes.InstanceRemoved,
                Status = RequestState.NotRunning,
                Title = $"{title} was removed from the hub",
                Actor = await ResolveActorAsync(authContext)
            });
        }

        /// <summary>
        /// The hub started or is stopping
        /// </summary>
        public Task HubLifecycleAsync(bool isStarting, string? version)
        {
            return RecordAsync(new ActivityEvent
            {
                InstanceId = _stateProvider.GetManagementHubInstanceId(),
                Category = ActivityCategory.Hub,
                EventType = isStarting ? ActivityEventTypes.HubStarted : ActivityEventTypes.HubStopped,
                Status = RequestState.NotRunning,
                Title = isStarting ? "Management hub started" : "Management hub stopped",
                Detail = string.IsNullOrWhiteSpace(version) ? null : $"Version {version}"
            });
        }

        /// <summary>
        /// The hub told the instances using a shared (subscription) certificate that a new version is available
        /// </summary>
        public Task SubscriptionPushedAsync(string instanceId, ManagedCertificate sourceItem, int subscriberCount)
        {
            return RecordAsync(new ActivityEvent
            {
                InstanceId = instanceId,
                ManagedItemId = sourceItem.Id,
                ItemTitle = sourceItem.Name,
                Category = ActivityCategory.Certificate,
                EventType = ActivityEventTypes.SubscriptionPushed,
                Status = RequestState.NotRunning,
                Title = $"Sent the new {sourceItem.Name} certificate to {subscriberCount} subscribing {(subscriberCount == 1 ? "certificate" : "certificates")}"
            });
        }

        /// <summary>
        /// A person changed a managed certificate (or asked for its request) through the hub
        /// </summary>
        public async Task ItemChangedAsync(string instanceId, string managedItemId, string? itemTitle, string eventType, AuthContext? authContext, string? detail = null)
        {
            var actor = await ResolveActorAsync(authContext);

            itemTitle ??= GetCachedItem(instanceId, managedItemId)?.Name ?? "a certificate";

            if (eventType == ActivityEventTypes.ItemRequested)
            {
                NoteRequestedBy(instanceId, managedItemId, actor);
            }

            var who = actor ?? "Someone";

            var title = eventType switch
            {
                ActivityEventTypes.ItemAdded => $"{who} added {itemTitle}",
                ActivityEventTypes.ItemUpdated => $"{who} edited {itemTitle}",
                ActivityEventTypes.ItemRemoved => $"{who} removed {itemTitle}",
                ActivityEventTypes.ItemRequested => $"{who} requested {itemTitle}",
                ActivityEventTypes.ItemStatusReset => $"{who} reset the status of {itemTitle}",
                ActivityEventTypes.ItemTaskExecuted => $"{who} ran a deployment task for {itemTitle}",
                _ => $"{who} changed {itemTitle}"
            };

            await RecordAsync(new ActivityEvent
            {
                InstanceId = instanceId,
                ManagedItemId = managedItemId,
                ItemTitle = itemTitle,
                Category = ActivityCategory.Change,
                EventType = eventType,
                Status = RequestState.NotRunning,
                Title = title,
                Detail = detail,
                Actor = actor
            });
        }

        /// <summary>
        /// The display name of the principal behind a request, where it can be found
        /// </summary>
        public async Task<string?> ResolveActorAsync(AuthContext? authContext)
        {
            var principalId = authContext?.UserId;

            if (string.IsNullOrWhiteSpace(principalId))
            {
                return null;
            }

            if (_principalNames.TryGetValue(principalId, out var name))
            {
                return name;
            }

            // principals are loaded all at once, at most every few minutes, rather than for every change recorded
            if (_client != null && DateTimeOffset.UtcNow - _principalNamesLoaded > TimeSpan.FromMinutes(5))
            {
                _principalNamesLoaded = DateTimeOffset.UtcNow;

                try
                {
                    var principals = await _client.GetSecurityPrincipals(PrincipalAccess.SystemAuthContext);

                    foreach (var p in principals ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(p.Id))
                        {
                            _principalNames[p.Id] = p.Title ?? p.Username ?? p.Id;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Could not load security principals to name activity actors.");
                }
            }

            return _principalNames.TryGetValue(principalId, out name) ? name : principalId;
        }

        /// <summary>
        /// The display title of an instance
        /// </summary>
        public async Task<string> GetInstanceTitleAsync(string instanceId)
        {
            if (_instanceTitles.TryGetValue(instanceId, out var title))
            {
                return title;
            }

            var connected = _stateProvider.GetConnectedInstances()
                .FirstOrDefault(i => string.Equals(i.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(connected?.Title))
            {
                NoteInstanceTitle(instanceId, connected.DisplayTitle);
                return connected.DisplayTitle ?? instanceId;
            }

            if (_client != null)
            {
                try
                {
                    var stored = await _client.GetHubManagedInstance(instanceId, PrincipalAccess.SystemAuthContext);

                    if (!string.IsNullOrWhiteSpace(stored?.DisplayTitle))
                    {
                        NoteInstanceTitle(instanceId, stored.DisplayTitle);
                        return stored.DisplayTitle;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Could not read instance {instanceId} to name its activity.", instanceId);
                }
            }

            return instanceId;
        }

        private ManagedCertificate? GetCachedItem(string? instanceId, string? managedItemId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(managedItemId))
            {
                return null;
            }

            return _stateProvider.GetManagedInstanceItems().TryGetValue(instanceId, out var instanceItems)
                ? instanceItems.Items?.FirstOrDefault(i => string.Equals(i.Id, managedItemId, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        private static (bool IsValid, string Description) DescribeLicense(Registration.Core.Models.Shared.LicenseCheckResult? license)
        {
            if (license == null)
            {
                return (false, "No licence");
            }

            var expiry = license.DateExpiry.HasValue ? $", expires {license.DateExpiry.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : "";

            return license.IsValid
                ? (true, $"Valid{expiry}")
                : (false, string.IsNullOrWhiteSpace(license.ValidationMessage) ? $"Not valid{expiry}" : license.ValidationMessage);
        }

        /// <summary>
        /// A new event id, which sorts in time order
        /// </summary>
        public static string NewEventId() => $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..30];
    }
}
