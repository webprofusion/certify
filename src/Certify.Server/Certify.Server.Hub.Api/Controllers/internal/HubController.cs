using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Provides managed certificate related operations
    /// </summary>
    [ApiController]
    [Route("internal/v1/[controller]")]
    public partial class HubController : ApiControllerBase
    {

        private readonly ILogger<CertificateController> _logger;

        private readonly ICertifyInternalApiClient _client;

        private IInstanceManagementStateProvider _mgmtStateProvider;
        private ManagementAPI _mgmtAPI;
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="mgmtStateProvider"></param>
        /// <param name="mgmtAPI"></param>
        public HubController(ILogger<CertificateController> logger, ICertifyInternalApiClient client, IInstanceManagementStateProvider mgmtStateProvider, ManagementAPI mgmtAPI)
        {
            _logger = logger;
            _client = client;
            _mgmtStateProvider = mgmtStateProvider;
            _mgmtAPI = mgmtAPI;
        }

        /// <summary>
        /// Get all managed certificates matching criteria
        /// </summary>
        /// <param name="instanceId">optionally restrict results to a single managed instance</param>
        /// <param name="keyword">optional keyword to match against the item name</param>
        /// <param name="health">optional health status to match</param>
        /// <param name="tagScopes">optional set of tag scopes to match, each expressed as "category" (any value in the category) or "category=value"</param>
        /// <param name="requireAllTags">if true an item must match every supplied tag scope, otherwise matching any one scope is enough</param>
        /// <param name="includeUntagged">if true items with no tags at all are also included when tag scopes are supplied</param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("items")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedCertificateSummaryResult))]
        public async Task<IActionResult> GetHubManagedItems(string? instanceId, string? keyword, string? health = null, [FromQuery] string[]? tagScopes = null, bool requireAllTags = false, bool includeUntagged = false, int? page = null, int? pageSize = null)
        {
            var list = await GetFilteredManagedItems(instanceId, keyword, health, tagScopes, requireAllTags, includeUntagged);

            var resolvedPageSize = pageSize ?? 100;
            var resolvedPageIndex = page > 0 ? (int)page : 0;

            return new OkObjectResult(new ManagedCertificateSummaryResult
            {
                TotalResults = list.Count,
                PageIndex = resolvedPageIndex,
                PageSize = resolvedPageSize,
                Results = list.OrderBy(l => l.Title).Skip(resolvedPageIndex * resolvedPageSize).Take(resolvedPageSize)
            });
        }

        /// <summary>
        /// Get a status summary for all managed certificates matching criteria.
        /// </summary>
        /// <remarks>
        /// This is computed from the same filtered set as the items endpoint, so summary counts and list
        /// contents are always consistent. When no filtering applies the pre-aggregated instance summaries are used instead.
        /// </remarks>
        /// <param name="instanceId">optionally restrict results to a single managed instance</param>
        /// <param name="keyword">optional keyword to match against the item name</param>
        /// <param name="tagScopes">optional set of tag scopes to match, each expressed as "category" (any value in the category) or "category=value"</param>
        /// <param name="requireAllTags">if true an item must match every supplied tag scope, otherwise matching any one scope is enough</param>
        /// <param name="includeUntagged">if true items with no tags at all are also included when tag scopes are supplied</param>
        /// <returns></returns>
        [HttpGet]
        [Route("items/summary")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StatusSummary))]
        public async Task<IActionResult> GetHubManagedItemsSummary(string? instanceId, string? keyword, [FromQuery] string[]? tagScopes = null, bool requireAllTags = false, bool includeUntagged = false)
        {
            var scopes = TagScopeFilter.ParseAll(tagScopes);
            var userTagScopes = await GetUserTagScopes();

            // when nothing needs per-item evaluation we can use the pre-aggregated summaries reported by each instance
            if (scopes.Count == 0 && string.IsNullOrWhiteSpace(keyword) && userTagScopes?.Any() != true)
            {
                var aggregate = string.IsNullOrEmpty(instanceId)
                    ? await _mgmtAPI.GetManagedCertificateSummary(CurrentAuthContext)
                    : await _mgmtAPI.GetManagedCertificateSummary(instanceId, CurrentAuthContext);

                return new OkObjectResult(aggregate ?? new StatusSummary { InstanceId = instanceId ?? string.Empty });
            }

            var list = await GetFilteredManagedItems(instanceId, keyword, null, tagScopes, requireAllTags, includeUntagged, userTagScopes);

            return new OkObjectResult(SummariseManagedItems(list, instanceId));
        }

        /// <summary>
        /// Build the set of managed certificate summaries matching the given criteria, including the tag scope
        /// restrictions which apply to the current user.
        /// </summary>
        private async Task<List<ManagedCertificateSummary>> GetFilteredManagedItems(string? instanceId, string? keyword, string? health, IEnumerable<string>? tagScopes, bool requireAllTags, bool includeUntagged, List<TagScope>? userTagScopes = null)
        {
            var scopes = TagScopeFilter.ParseAll(tagScopes);

            // if the user has scoped tags on their assigned roles they can only see items matching those tags
            userTagScopes ??= await GetUserTagScopes();

            var managedItems = _mgmtStateProvider.GetManagedInstanceItems();
            var instances = _mgmtStateProvider.GetConnectedInstances();

            // TODO: would fetching cached hub status summaries be faster
            var knownInstances = await _client.GetHubManagedInstances(CurrentAuthContext);

            var tagsByItemId = await GetItemTagsByItemId(TaggedItemTypes.ManagedCertificate);

            ManagedCertificateHealth? healthFilter = null;

            if (!string.IsNullOrEmpty(health) && Enum.TryParse(health, true, out ManagedCertificateHealth healthValue))
            {
                healthFilter = healthValue;
            }

            var list = new List<ManagedCertificateSummary>();

            foreach (var remote in managedItems.Values)
            {
                if (!string.IsNullOrEmpty(instanceId) && instanceId != remote.InstanceId)
                {
                    continue;
                }

                var instance = knownInstances.FirstOrDefault(k => k.InstanceId == remote.InstanceId)
                               ?? instances.FirstOrDefault(c => c.InstanceId == remote.InstanceId);

                foreach (var i in remote.Items)
                {
                    if (!string.IsNullOrWhiteSpace(keyword) && i.Name?.Contains(keyword, StringComparison.InvariantCultureIgnoreCase) != true)
                    {
                        continue;
                    }

                    if (healthFilter != null && i.Health != healthFilter)
                    {
                        continue;
                    }

                    var tags = tagsByItemId.TryGetValue(i.Id ?? "", out var itemTags) ? itemTags : new List<TagSummary>();

                    if (!TagScopeFilter.Matches(tags, scopes, requireAllTags, includeUntagged))
                    {
                        continue;
                    }

                    // a user restricted to tag scopes can never see untagged items
                    if (userTagScopes?.Any() == true && !TagScopeFilter.Matches(tags, userTagScopes, matchAll: false))
                    {
                        continue;
                    }

                    list.Add(new ManagedCertificateSummary
                    {
                        InstanceId = remote.InstanceId,
                        InstanceTitle = instance?.DisplayTitle,
                        Id = i.Id ?? "",
                        Title = i.Name ?? "",
                        OS = instance?.OS,
                        ClientDetails = i.SourceId != null ? i.SourceName : instance?.ClientName,
                        PrimaryIdentifier = i.GetCertificateIdentifiers().FirstOrDefault(p => p.Value == i.RequestConfig.PrimaryDomain) ?? i.GetCertificateIdentifiers().FirstOrDefault(),
                        Identifiers = i.GetCertificateIdentifiers(),
                        DateRenewed = i.DateRenewed,
                        DateExpiry = i.DateExpiry,
                        Comments = i.Comments ?? "",
                        Status = i.Health.ToString(),
                        DateRetrieved = i.DateRetrieved,
                        HasCertificate = !string.IsNullOrEmpty(i.CertificatePath),
                        IsExternallyManaged = i.IsExternallyManaged,
                        IsSubscription = i.IsSubscription,
                        Tags = tags
                    });
                }
            }

            return list;
        }

        /// <summary>
        /// Load the display tags for all items of the given type, keyed by item id.
        /// </summary>
        /// <remarks>
        /// TODO: we need to optimize this by only loading tags for items we know are in the result set, which
        /// requires a backend API to fetch tags for a given set of item ids.
        /// </remarks>
        private async Task<Dictionary<string, List<TagSummary>>> GetItemTagsByItemId(string itemTypeId)
        {
            var tagsByItemId = new Dictionary<string, List<TagSummary>>();

            try
            {
                // load tag categories to get display names and colors
                var categoriesByKey = new Dictionary<string, TagCategory>();
                var categories = await _client.GetTagCategories(CurrentAuthContext);

                if (categories != null)
                {
                    foreach (var cat in categories)
                    {
                        categoriesByKey[cat.CategoryKey] = cat;
                    }
                }

                var allItemTags = await _client.GetAllHubItemTags(null, null, itemTypeId, null, CurrentAuthContext);

                if (allItemTags != null)
                {
                    foreach (var tag in allItemTags)
                    {
                        if (!tagsByItemId.TryGetValue(tag.TaggedItemId, out var itemTags))
                        {
                            itemTags = new List<TagSummary>();
                            tagsByItemId[tag.TaggedItemId] = itemTags;
                        }

                        categoriesByKey.TryGetValue(tag.CategoryKey, out var category);

                        itemTags.Add(new TagSummary
                        {
                            CategoryKey = tag.CategoryKey,
                            CategoryDisplayName = category?.DisplayName ?? tag.CategoryKey,
                            Value = tag.Value,
                            ColorHint = category?.ColorHint
                        });
                    }
                }
            }
            catch
            {
                // if tag loading fails, continue without tags
            }

            return tagsByItemId;
        }

        /// <summary>
        /// Summarise a set of managed certificate summaries into overall status counts.
        /// </summary>
        /// <remarks>
        /// The counts must match those an instance reports for itself, because this endpoint falls back to the
        /// pre-aggregated instance summaries when no filtering applies. In particular ExternallyManaged counts only
        /// items discovered via an external certificate manager provider, not certificate subscriptions.
        /// </remarks>
        private static StatusSummary SummariseManagedItems(IEnumerable<ManagedCertificateSummary> items, string? instanceId)
        {
            var summary = new StatusSummary { InstanceId = instanceId ?? string.Empty };

            foreach (var item in items)
            {
                summary.Total++;
                summary.TotalDomains += item.Identifiers?.Count() ?? 0;

                if (!item.HasCertificate)
                {
                    summary.NoCertificate++;
                }

                if (item.IsExternallyManaged)
                {
                    summary.ExternallyManaged++;
                }

                if (Enum.TryParse(item.Status, true, out ManagedCertificateHealth health))
                {
                    switch (health)
                    {
                        case ManagedCertificateHealth.OK:
                            summary.Healthy++;
                            break;
                        case ManagedCertificateHealth.Warning:
                            summary.Warning++;
                            break;
                        case ManagedCertificateHealth.Error:
                            summary.Error++;
                            break;
                        case ManagedCertificateHealth.AwaitingUser:
                            summary.AwaitingUser++;
                            break;
                    }
                }
            }

            return summary;
        }

        /// <summary>
        /// Get all hub managed instances
        /// </summary>
        /// <param name="tagScopes">optional set of tag scopes to match, each expressed as "category" (any value in the category) or "category=value"</param>
        /// <param name="requireAllTags">if true an instance must match every supplied tag scope, otherwise matching any one scope is enough</param>
        /// <param name="includeUntagged">if true instances with no tags at all are also included when tag scopes are supplied</param>
        /// <returns></returns>
        [HttpGet]
        [Route("instances")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedInstanceInfo>))]
        public async Task<IActionResult> GetHubManagedInstances([FromQuery] string[]? tagScopes = null, bool requireAllTags = false, bool includeUntagged = false)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

            // Get all known instances from database (including disconnected ones)
            var allKnownInstances = await _client.GetHubManagedInstances(CurrentAuthContext);

            // Get currently connected instances from in-memory state
            var connectedInstances = _mgmtStateProvider.GetConnectedInstances();

            // Merge: update known instances with current connection status
            foreach (var knownInstance in allKnownInstances)
            {
                var connected = connectedInstances.FirstOrDefault(c => c.InstanceId == knownInstance.InstanceId);
                if (connected != null)
                {
                    // Instance is currently connected - update with real-time data
                    knownInstance.DateLastReported = connected.DateLastReported;
                    knownInstance.ConnectionStatus = connected.ConnectionStatus;
                    knownInstance.License = connected.License;
                    knownInstance.IsAuthenticated = true;

                    if (!string.IsNullOrWhiteSpace(connected.InternalInstanceId))
                    {
                        knownInstance.InternalInstanceId = connected.InternalInstanceId;
                    }

                    // Copy db values to in-memory connected instance representation
                    connected.DateRegistered = knownInstance.DateRegistered;
                    connected.Tags = knownInstance.Tags;
                }
                else
                {
                    // Instance is not currently connected - mark as disconnected
                    knownInstance.ConnectionStatus = ConnectionStatus.Disconnected;
                    knownInstance.IsAuthenticated = true; // Still authenticated, just not connected
                }

                // Get latest status summary for instance (if any)
                var statusSummary = _mgmtStateProvider.GetManagedInstanceStatusSummary(knownInstance.InstanceId);
                knownInstance.Summary = statusSummary;

            }

            var scopes = TagScopeFilter.ParseAll(tagScopes);

            var results = (IEnumerable<ManagedInstanceInfo>)allKnownInstances;

            if (scopes.Count > 0)
            {
                // instance tags are held in the item tag store rather than on the stored instance record
                var instanceTags = await GetItemTagsByItemId(TaggedItemTypes.ManagedInstance);

                results = results.Where(i =>
                {
                    instanceTags.TryGetValue(i.Id ?? "", out var itemTags);
                    return TagScopeFilter.Matches(itemTags, scopes, requireAllTags, includeUntagged);
                });
            }

            // Return all instances (both connected and disconnected) ordered by display title
            return new OkObjectResult(results.OrderBy(o => o.DisplayTitle));
        }

        /// <summary>
        /// Get a managed instance by id.
        /// </summary>
        [HttpGet]
        [Route("instances/{id}")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedInstanceInfo))]
        public async Task<IActionResult> GetHubManagedInstance(string id)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

            var instance = await _client.GetHubManagedInstance(id, CurrentAuthContext);

            if (instance == null)
            {
                return NotFound();
            }

            return new OkObjectResult(instance);
        }

        /// <summary>
        /// Update a managed instance by id.
        /// </summary>
        [HttpPut]
        [Route("instances/{id}")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(global::Certify.Models.Config.ActionResult))]
        public async Task<IActionResult> UpdateHubManagedInstance(string id, [FromBody] ManagedInstanceInfo item)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceUpdate));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

            if (item == null || string.IsNullOrWhiteSpace(id))
            {
                return BadRequest();
            }

            item.Id = id;
            item.InstanceId = id;

            var result = await _client.UpdateHubManagedInstance(item, CurrentAuthContext);

            return new OkObjectResult(result);
        }

        /// <summary>
        /// Flush all hub managed instances
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("flush")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> FlushHubManagedInstances()
        {
            _mgmtAPI.ReconnectInstances();

            return new OkResult();
        }

        /// <summary>
        /// Get info about the hub instance
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("info")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HubInfo))]
        public async Task<IActionResult> GetHubInfo()
        {
            // see also SystemController.CheckJoining which has similar/same info
            var hubInfo = await _client.GetHubInfo();
            return new OkObjectResult(hubInfo);
        }

        /// <summary>
        /// Retrieves the current system status items 
        /// </summary>
        /// <returns>Returns an OK response containing a list of ActionStep objects.</returns>
        [HttpGet]
        [Route("status/{instanceId?}")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ActionStep>))]
        public async Task<IActionResult> GetSystemStatusItems(string? instanceId = null)
        {

            var status = _mgmtStateProvider.GetSystemStatusItems();

            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                var instanceStatus = await _mgmtAPI.GetInstanceStatusItems(instanceId, CurrentAuthContext);

                if (instanceStatus?.Count > 0)
                {
                    status.AddRange(instanceStatus);
                }
            }

            return new OkObjectResult(status);
        }

        /// <summary>
        /// Get the tag scopes that should restrict what the current user can see/manage.
        /// Returns null if no restrictions apply (user can see everything).
        /// </summary>
        private async Task<List<TagScope>?> GetUserTagScopes()
        {
            // Only apply tag scope restrictions if we have a valid user context
            if (CurrentAuthContext?.UserId == null)
            {
                return null;
            }

            try
            {
                // Get all assigned roles for the user
                var assignedRoles = await _client.GetSecurityPrincipalAssignedRoles(CurrentAuthContext.UserId, CurrentAuthContext);

                if (assignedRoles == null || !assignedRoles.Any())
                {
                    return null;
                }

                // If this is a scoped token (API access), only consider the scoped roles
                // Otherwise, consider all assigned roles
                IEnumerable<AssignedRole> rolesToCheck;

                if (CurrentAuthContext.ScopedAssignedRoles != null && CurrentAuthContext.ScopedAssignedRoles.Any())
                {
                    // API token with specific scoped roles - only check those roles
                    rolesToCheck = assignedRoles.Where(r => CurrentAuthContext.ScopedAssignedRoles.Contains(r.Id));
                }
                else
                {
                    // Regular user authentication - check all assigned roles for tag scopes
                    rolesToCheck = assignedRoles;
                }

                // Collect tag scopes from the applicable roles
                var tagScopes = new List<TagScope>();

                foreach (var role in rolesToCheck)
                {
                    if (role.ScopedTags != null && role.ScopedTags.Any())
                    {
                        tagScopes.AddRange(role.ScopedTags);
                    }
                }

                // If no tag restrictions found, return null (meaning no filtering)
                if (!tagScopes.Any())
                {
                    return null;
                }

                return tagScopes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user tag scopes");
                return null;
            }
        }
    }
}
