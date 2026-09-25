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
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

            // the tag scopes and domain restrictions on the user's assigned roles limit which items they can see
            var visibility = await ResourceScope.Resolve(_client, CurrentAuthContext, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList);
            var list = await ManagedItemListing.GetItems(_client, _mgmtAPI, visibility, instanceId, keyword, health, tagScopes, requireAllTags, includeUntagged);

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
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

            var scopes = TagScopeFilter.ParseAll(tagScopes);
            var visibility = await ResourceScope.Resolve(_client, CurrentAuthContext, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList);

            // when nothing needs per-item evaluation we can use the pre-aggregated summaries reported by each instance
            if (scopes.Count == 0 && string.IsNullOrWhiteSpace(keyword))
            {
                return new OkObjectResult(await ManagedItemListing.GetSummary(_client, _mgmtAPI, visibility, instanceId, CurrentAuthContext));
            }

            var list = await ManagedItemListing.GetItems(_client, _mgmtAPI, visibility, instanceId, keyword, null, tagScopes, requireAllTags, includeUntagged);

            return new OkObjectResult(ManagedItemListing.Summarise(list, instanceId));
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

            // instance tags are held in the item tag store rather than on the stored instance record, and are returned
            // with each instance so that clients can show and filter by them
            var instanceTags = await ManagedItemListing.GetItemTagsByItemId(_client, TaggedItemTypes.ManagedInstance);

            foreach (var instance in allKnownInstances)
            {
                instance.Tags = instanceTags.TryGetValue(instance.Id ?? "", out var itemTags) ? itemTags : [];
            }

            var scopes = TagScopeFilter.ParseAll(tagScopes);

            // a caller whose role is tag scoped or domain restricted sees the instances within that scope
            var instanceScope = await ResourceScope.Resolve(_client, CurrentAuthContext, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList);
            var instancesInScope = await GetInstancesInScope(_client, _mgmtAPI, instanceScope, allKnownInstances.Select(i => i.InstanceId));

            var results = allKnownInstances.Where(i => instancesInScope.Contains(i.InstanceId));

            if (scopes.Count > 0)
            {
                results = results.Where(i => TagScopeFilter.Matches(i.Tags, scopes, requireAllTags, includeUntagged));
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

            var outOfScope = await CheckInstanceInScope(_client, _mgmtAPI, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList, id);
            if (outOfScope != null)
            {
                return outOfScope;
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

            var outOfScope = await CheckInstanceInScope(_client, _mgmtAPI, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceUpdate, id);
            if (outOfScope != null)
            {
                return outOfScope;
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
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

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
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.System, StandardResourceActions.SystemStatusList));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

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

    }
}
