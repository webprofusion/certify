using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
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
        /// <param name="instanceId"></param>
        /// <param name="keyword"></param>
        /// <param name="health"></param>
        /// <param name="tagCategory"></param>
        /// <param name="tagValue"></param>
        /// <param name="requireAllTags"></param>
        /// <param name="includeUntagged"></param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("items")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedCertificateSummaryResult))]
        public async Task<IActionResult> GetHubManagedItems(string? instanceId, string? keyword, string? health = null, string? tagCategory = null, string? tagValue = null, bool requireAllTags = false, bool includeUntagged = true, int? page = null, int? pageSize = null)
        {
            var result = new ManagedCertificateSummaryResult();

            var managedItems = _mgmtStateProvider.GetManagedInstanceItems();
            var instances = _mgmtStateProvider.GetConnectedInstances();

            // Pre-load all item tags for ManagedCertificates to display in the list
            Dictionary<string, List<TagSummary>> tagsByItemId = new();
            Dictionary<string, TagCategory> categoriesByKey = new();
            var isTagFilterActive = !string.IsNullOrEmpty(tagCategory);

            // Always load tags for display in the list
            try
            {
                // Load tag categories to get display names and colors
                var categories = await _client.GetTagCategories(CurrentAuthContext);
                if (categories != null)
                {
                    foreach (var cat in categories)
                    {
                        categoriesByKey[cat.CategoryKey] = cat;
                    }
                }

                var allItemTags = await _client.GetAllHubItemTags(null, null, "ManagedCertificate", CurrentAuthContext);
                if (allItemTags != null)
                {
                    // Group tags by item ID for efficient lookup
                    foreach (var tag in allItemTags)
                    {
                        if (!tagsByItemId.ContainsKey(tag.TaggedItemId))
                        {
                            tagsByItemId[tag.TaggedItemId] = new List<TagSummary>();
                        }

                        // Get category info for color and display name
                        categoriesByKey.TryGetValue(tag.CategoryKey, out var category);

                        tagsByItemId[tag.TaggedItemId].Add(new TagSummary
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
                // If tag loading fails, continue without tags
            }

            var list = new List<ManagedCertificateSummary>();

            ManagedCertificateHealth? healthFilter = null;

            if (!string.IsNullOrEmpty(health))
            {
                if (Enum.TryParse(health, true, out ManagedCertificateHealth healthValue))
                {
                    healthFilter = healthValue;
                }
            }

            foreach (var remote in managedItems.Values)
            {
                if (string.IsNullOrEmpty(instanceId) || (instanceId == remote.InstanceId))
                {
                    list.AddRange(
                        remote.Items
                        .Where(i => string.IsNullOrWhiteSpace(keyword) || (!string.IsNullOrWhiteSpace(keyword) && i.Name?.Contains(keyword, StringComparison.InvariantCultureIgnoreCase) == true))
                        .Where(i => healthFilter == null || (healthFilter != null && i.Health == healthFilter))
                        .Select(i =>
                        {
                            var instance = instances.FirstOrDefault(i => i.InstanceId == remote.InstanceId);

                            // Get tags for this managed certificate from pre-loaded data
                            var tags = tagsByItemId.TryGetValue(i.Id ?? "", out var itemTags) ? itemTags : new List<TagSummary>();

                            return new ManagedCertificateSummary
                            {
                                InstanceId = remote.InstanceId,
                                InstanceTitle = instance?.Title,
                                Id = i.Id ?? "",
                                Title = $"{i.Name}" ?? "",
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
                                IsExternallyManaged = i.ItemType == ManagedCertificateType.SSL_ExternallyManaged,
                                Tags = tags
                            };
                        }
                        )
                    );
                }
            }

            // Apply tag filtering if specified
            if (isTagFilterActive)
            {
                var tagScopes = new List<TagScope> { new TagScope { CategoryKey = tagCategory!, Value = tagValue } };

                list = list.Where(item =>
                {
                    var hasTags = item.Tags.Any();

                    if (!hasTags)
                    {
                        // Item has no tags - include only if includeUntagged is true AND no specific tag filter value is set
                        return includeUntagged && string.IsNullOrEmpty(tagValue);
                    }

                    if (requireAllTags)
                    {
                        // Item must match ALL scopes
                        return tagScopes.All(scope =>
                            item.Tags.Any(tag => tag.CategoryKey == scope.CategoryKey &&
                                (scope.Value == null || tag.Value == scope.Value)));
                    }
                    else
                    {
                        // Item must match ANY scope
                        return tagScopes.Any(scope =>
                            item.Tags.Any(tag => tag.CategoryKey == scope.CategoryKey &&
                                (scope.Value == null || tag.Value == scope.Value)));
                    }
                }).ToList();
            }

            result.TotalResults = list.Count;

            var skip = 0;

            if (page != null && page > 0)
            {
                skip = (int)page * (pageSize ?? 100);
            }

            result.Results = list.OrderBy(l => l.Title).Skip(skip).Take(pageSize ?? 100);

            return new OkObjectResult(result);
        }

        /// <summary>
        /// Get all hub managed instances
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("instances")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedInstanceInfo>))]
        public async Task<IActionResult> GetHubManagedInstances()
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

            // Return all instances (both connected and disconnected) ordered by title
            return new OkObjectResult(allKnownInstances.OrderBy(o => o.Title));
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
    }
}
