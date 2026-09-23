using System.Collections.Concurrent;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Providers;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;

namespace Certify.Server.Hub.Api.SignalR
{
    /// <summary>
    /// Sends status updates to connected UI clients, each only to the clients whose security principal may see it.
    ///
    /// Item level updates go to principals the managed item listing would show the item to, so a principal whose
    /// roles are limited by tag scopes or domain restrictions is not sent the progress or changes of items outside
    /// them. Hub level diagnostics go to administrators only.
    ///
    /// What each principal may see is cached briefly, as progress is reported many times per request. A change to
    /// a principal's role assignments therefore reaches an existing connection within <see cref="AccessCacheDuration"/>.
    /// </summary>
    public class UserInterfaceStatusBroadcaster
    {
        internal static readonly TimeSpan AccessCacheDuration = TimeSpan.FromSeconds(30);

        private readonly IHubContext<UserInterfaceStatusHub> _hubContext;
        private readonly ICertifyInternalApiClient _client;
        private readonly IInstanceManagementStateProvider _stateProvider;
        private readonly IMemoryCache _cache;
        private readonly ILogger<UserInterfaceStatusBroadcaster> _logger;

        private readonly ConcurrentDictionary<string, AuthContext> _connections = new();

        /// <summary>
        /// Constructor
        /// </summary>
        public UserInterfaceStatusBroadcaster(
            IHubContext<UserInterfaceStatusHub> hubContext,
            ICertifyInternalApiClient client,
            IInstanceManagementStateProvider stateProvider,
            IMemoryCache cache,
            ILogger<UserInterfaceStatusBroadcaster> logger)
        {
            _hubContext = hubContext;
            _client = client;
            _stateProvider = stateProvider;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Track a connected UI client and the caller it authenticated as
        /// </summary>
        public void AddConnection(string connectionId, AuthContext authContext) => _connections[connectionId] = authContext;

        /// <summary>
        /// Stop tracking a disconnected UI client
        /// </summary>
        public void RemoveConnection(string connectionId) => _connections.TryRemove(connectionId, out _);

        /// <summary>
        /// Send request progress for a managed item to the clients which may see the item
        /// </summary>
        public Task SendRequestProgress(RequestProgressState state)
        {
            // an error state carries the request result, which includes the whole managed item, and UI clients use
            // only the progress itself
            var progress = new RequestProgressState
            {
                CurrentState = state.CurrentState,
                Message = state.Message,
                ManagedCertificate = state.ManagedCertificate,
                IsPreviewMode = state.IsPreviewMode,
                IsSkipped = state.IsSkipped,
                MessageCreated = state.MessageCreated
            };

            return SendToItemViewers(
                state.ManagedCertificate?.InstanceId,
                state.ManagedCertificate?.Id,
                item: null,
                clients => clients.SendAsync(StatusHubMessages.SendProgressStateMsg, progress));
        }

        /// <summary>
        /// Notify the clients which may see a managed item that it has been updated
        /// </summary>
        public Task SendManagedItemUpdated(ManagedCertificate item)
            => SendManagedItemChange(ManagementHubCommands.NotificationUpdatedManagedItem, item.InstanceId, item.Id, "updated", item);

        /// <summary>
        /// Notify the clients which may see a managed item that it has been removed. Sent before the hub's cached
        /// copy of the item is removed, as that is what the item's identifiers are read from.
        /// </summary>
        public Task SendManagedItemRemoved(string instanceId, string managedItemId)
            => SendManagedItemChange(ManagementHubCommands.NotificationRemovedManagedItem, instanceId, managedItemId, "deleted", item: null);

        /// <summary>
        /// Send a service level diagnostic which requires operator action to connected administrators
        /// </summary>
        public async Task SendDiagnosticActionRequired(DiagnosticActionRequired diagnostic)
        {
            var connectionIds = new List<string>();

            foreach (var audience in GetAudiences())
            {
                var isAdministrator = await GetCachedAccess(
                    $"ui-status-admin:{audience.Key}",
                    () => PrincipalAccess.IsAdministrator(_client, audience.AuthContext),
                    fallback: false);

                if (isAdministrator)
                {
                    connectionIds.AddRange(audience.ConnectionIds);
                }
            }

            if (connectionIds.Count > 0)
            {
                await _hubContext.Clients.Clients(connectionIds).SendAsync(
                    StatusHubMessages.SendMsg,
                    StatusHubMessages.NotificationActionRequired,
                    System.Text.Json.JsonSerializer.Serialize(diagnostic));
            }
        }

        private Task SendManagedItemChange(string notificationType, string? instanceId, string? managedItemId, string action, ManagedCertificate? item)
        {
            var notification = System.Text.Json.JsonSerializer.Serialize(new ManagedItemChangeNotification
            {
                InstanceId = instanceId ?? string.Empty,
                ManagedItemId = managedItemId ?? string.Empty,
                Action = action
            });

            return SendToItemViewers(
                instanceId,
                managedItemId,
                item,
                clients => clients.SendAsync(StatusHubMessages.SendMsg, notificationType, notification));
        }

        /// <summary>
        /// Send to the connected clients which may see the given managed item
        /// </summary>
        /// <param name="instanceId">instance holding the item</param>
        /// <param name="managedItemId">the item</param>
        /// <param name="item">the item where the caller has it, otherwise the hub's cached copy is used</param>
        /// <param name="send">the send to perform</param>
        private async Task SendToItemViewers(string? instanceId, string? managedItemId, ManagedCertificate? item, Func<IClientProxy, Task> send)
        {
            var audiences = GetAudiences();

            if (audiences.Count == 0)
            {
                return;
            }

            // the item's tags and identifiers are only looked up if a restricted audience needs them
            ICollection<TagSummary>? tags = null;
            List<string>? identifiers = null;

            var connectionIds = new List<string>();

            foreach (var audience in audiences)
            {
                var visibility = await GetCachedAccess(
                    $"ui-status-visibility:{audience.Key}",
                    () => ManagedItemVisibility.Resolve(_client, audience.AuthContext),
                    fallback: ManagedItemVisibility.None);

                if (visibility.RequiresTags)
                {
                    tags ??= await GetItemTags(managedItemId);
                }

                if (visibility.RequiresIdentifiers)
                {
                    identifiers ??= (item ?? GetCachedManagedItem(instanceId, managedItemId))?
                        .GetCertificateIdentifiers()
                        .Select(i => i.Value)
                        .ToList() ?? [];
                }

                if (visibility.Permits(tags, identifiers))
                {
                    connectionIds.AddRange(audience.ConnectionIds);
                }
            }

            if (connectionIds.Count > 0)
            {
                await send(_hubContext.Clients.Clients(connectionIds));
            }
        }

        private async Task<ICollection<TagSummary>> GetItemTags(string? managedItemId)
        {
            if (string.IsNullOrWhiteSpace(managedItemId))
            {
                return [];
            }

            try
            {
                return await _client.GetHubItemTags(TaggedItemTypes.ManagedCertificate, managedItemId, PrincipalAccess.SystemAuthContext) ?? [];
            }
            catch (Exception ex)
            {
                // no tags means no tag scoped audience is sent the update, which is the safe outcome
                _logger.LogWarning(ex, "Could not read tags for managed item {managedItemId} to filter status updates.", managedItemId);
                return [];
            }
        }

        private ManagedCertificate? GetCachedManagedItem(string? instanceId, string? managedItemId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(managedItemId))
            {
                return null;
            }

            return _stateProvider.GetManagedInstanceItems().TryGetValue(instanceId, out var instanceItems)
                ? instanceItems.Items?.FirstOrDefault(i => string.Equals(i.Id, managedItemId, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        private async Task<T> GetCachedAccess<T>(string cacheKey, Func<Task<T>> resolve, T fallback)
        {
            if (_cache.TryGetValue(cacheKey, out T? cached) && cached != null)
            {
                return cached;
            }

            try
            {
                var result = await resolve();
                _cache.Set(cacheKey, result, AccessCacheDuration);
                return result;
            }
            catch (Exception ex)
            {
                // not cached, so the next update evaluates again
                _logger.LogWarning(ex, "Could not evaluate status update access for {cacheKey}.", cacheKey);
                return fallback;
            }
        }

        /// <summary>
        /// The connected clients grouped by the caller they authenticated as. A principal connected with an API
        /// access token scoped to specific role assignments is evaluated separately from the same principal
        /// connected without one.
        /// </summary>
        private List<(string Key, AuthContext AuthContext, List<string> ConnectionIds)> GetAudiences()
        {
            return _connections
                .ToArray()
                .GroupBy(c => GetAudienceKey(c.Value))
                .Select(g => (g.Key, g.First().Value, g.Select(c => c.Key).ToList()))
                .ToList();
        }

        private static string GetAudienceKey(AuthContext authContext)
        {
            var scope = authContext.ScopedAssignedRoles?.Count > 0
                ? string.Join(",", authContext.ScopedAssignedRoles.Select(r => r.ToLowerInvariant()).Order(StringComparer.Ordinal))
                : string.Empty;

            return $"{authContext.UserId}|{scope}";
        }
    }
}
