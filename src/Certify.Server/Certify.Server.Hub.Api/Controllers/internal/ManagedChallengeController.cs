using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Internal API controller for managed challenge administration with access control.
    /// </summary>
    [ApiController]
    [Route("internal/v1/managedchallenges")]
    public partial class InternalManagedChallengeController : ApiControllerBase
    {
        private readonly ILogger<InternalManagedChallengeController> _logger;
        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public InternalManagedChallengeController(ILogger<InternalManagedChallengeController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// Get all managed challenges with tags, filtered by user's access scope
        /// </summary>
        /// <returns>List of managed challenge summaries with their tags</returns>
        [HttpGet]
        [Route("")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ICollection<ManagedChallengeSummary>))]
        public async Task<IActionResult> GetManagedChallengeSummaries()
        {
            // Check if user has permission to list managed challenges
            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedChallenge,
                ResourceActionId = StandardResourceActions.ManagedChallengeList
            };

            if (!await IsAuthorized(_client, accessCheck))
            {
                return Forbid();
            }

            // Get all managed challenges
            var challenges = await _client.GetManagedChallenges(CurrentAuthContext);

            // Get user's tag scopes from their scoped assigned roles
            var tagScopes = await GetUserTagScopesForResource();

            // Load all tags for managed challenges
            var allChallengeTags = await _client.GetAllHubItemTags(null, null, TaggedItemTypes.ManagedChallenge, null, CurrentAuthContext);
            var tagsByChallengeId = allChallengeTags?.GroupBy(t => t.TaggedItemId)
                .ToDictionary(g => g.Key, g => g.ToList()) ?? new Dictionary<string, List<ItemTag>>();

            // Load categories for display names
            var categories = await _client.GetTagCategories(CurrentAuthContext);
            var categoriesByKey = categories?.ToDictionary(c => c.CategoryKey, c => c) ?? new Dictionary<string, TagCategory>();

            var summaries = new List<ManagedChallengeSummary>();

            foreach (var challenge in challenges)
            {
                // Get tags for this challenge
                tagsByChallengeId.TryGetValue(challenge.Id, out var itemTags);
                var challengeTags = itemTags ?? new List<ItemTag>();

                // If user has tag restrictions, check if this challenge matches
                if (tagScopes != null && tagScopes.Any())
                {
                    // Challenge must have at least one matching tag (OR logic)
                    var hasMatchingTag = tagScopes.Any(scope =>
                        challengeTags.Any(t => t.CategoryKey == scope.CategoryKey &&
                            (scope.Value == null || t.Value == scope.Value)));

                    if (!hasMatchingTag)
                    {
                        continue; // Skip challenges that don't match tag scope
                    }
                }

                // Build tag summaries
                var tagSummaries = challengeTags.Select(t =>
                {
                    categoriesByKey.TryGetValue(t.CategoryKey, out var category);
                    return new TagSummary
                    {
                        CategoryKey = t.CategoryKey,
                        CategoryDisplayName = category?.DisplayName ?? t.CategoryKey,
                        Value = t.Value,
                        ColorHint = category?.ColorHint
                    };
                }).ToList();

                summaries.Add(new ManagedChallengeSummary
                {
                    Id = challenge.Id,
                    Title = challenge.Title,
                    ChallengeConfig = challenge.ChallengeConfig,
                    Tags = tagSummaries
                });
            }

            return new OkObjectResult(summaries);
        }

        /// <summary>
        /// Get managed challenges a specific security principal can use, based on assigned roles and tag restrictions.
        /// </summary>
        /// <param name="id">The security principal ID</param>
        /// <returns>List of accessible managed challenge summaries</returns>
        [HttpGet]
        [Route("available/securityprincipal/{id}")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ICollection<ManagedChallengeSummary>))]
        public async Task<IActionResult> GetSubscribableManagedChallengesBySecurityPrincipal(string id)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalCheckAccess));
            if (!accessCheck.IsSuccess)
            {
                accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedChallenge, StandardResourceActions.ManagedChallengeList));
                if (!accessCheck.IsSuccess)
                {
                    return Problem(detail: accessCheck.Message, statusCode: StatusCodes.Status401Unauthorized);
                }
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return new OkObjectResult(new List<ManagedChallengeSummary>());
            }

            var challenges = await _client.GetManagedChallenges(SystemAuthContext);

            var allChallengeTags = await _client.GetAllHubItemTags(null, null, TaggedItemTypes.ManagedChallenge, null, SystemAuthContext);
            var tagsByChallengeId = allChallengeTags?.GroupBy(t => t.TaggedItemId)
                .ToDictionary(g => g.Key, g => g.ToList()) ?? new Dictionary<string, List<ItemTag>>();

            var categories = await _client.GetTagCategories(SystemAuthContext);
            var categoriesByKey = categories?.ToDictionary(c => c.CategoryKey, c => c) ?? new Dictionary<string, TagCategory>();

            var summaries = new List<ManagedChallengeSummary>();

            foreach (var challenge in challenges)
            {
                tagsByChallengeId.TryGetValue(challenge.Id, out var itemTags);
                var challengeTags = itemTags ?? new List<ItemTag>();

                var tagSummaries = challengeTags.Select(t =>
                {
                    categoriesByKey.TryGetValue(t.CategoryKey, out var category);
                    return new TagSummary
                    {
                        CategoryKey = t.CategoryKey,
                        CategoryDisplayName = category?.DisplayName ?? t.CategoryKey,
                        Value = t.Value,
                        ColorHint = category?.ColorHint
                    };
                }).ToList();

                var challengeAccessCheck = new AccessCheck
                {
                    SecurityPrincipalId = id,
                    ResourceType = ResourceTypes.ManagedChallenge,
                    ResourceActionId = StandardResourceActions.ManagedChallengeRequest,
                    Identifier = challenge.Id,
                    ResourceTags = tagSummaries
                };

                if (!await _client.CheckSecurityPrincipalHasAccess(challengeAccessCheck, new AuthContext { UserId = id }))
                {
                    continue;
                }

                summaries.Add(new ManagedChallengeSummary
                {
                    Id = challenge.Id,
                    Title = challenge.Title,
                    ChallengeConfig = challenge.ChallengeConfig,
                    Tags = tagSummaries
                });
            }

            return new OkObjectResult(summaries);
        }

        /// <summary>
        /// Get tags for a specific managed challenge
        /// </summary>
        /// <param name="id">The managed challenge ID</param>
        /// <returns>List of tags assigned to the managed challenge</returns>
        [HttpGet]
        [Route("{id}/tags")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ICollection<TagSummary>))]
        public async Task<IActionResult> GetManagedChallengeTags(string id)
        {
            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedChallenge,
                ResourceActionId = StandardResourceActions.ManagedChallengeList,
                Identifier = id
            };

            if (!await IsAuthorized(_client, accessCheck))
            {
                return Forbid();
            }

            var tags = await _client.GetHubItemTags(id, TaggedItemTypes.ManagedChallenge, CurrentAuthContext);
            return new OkObjectResult(tags);
        }

        /// <summary>
        /// Add tags to a managed challenge
        /// </summary>
        /// <param name="id">The managed challenge ID</param>
        /// <param name="tags">Tags to add</param>
        /// <returns>Action result</returns>
        [HttpPost]
        [Route("{id}/tags")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Certify.Models.Config.ActionResult))]
        public async Task<IActionResult> AddManagedChallengeTags(string id, [FromBody] ICollection<TagScope> tags)
        {
            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedChallenge,
                ResourceActionId = StandardResourceActions.ManagedChallengeUpdate,
                Identifier = id
            };

            if (!await IsAuthorized(_client, accessCheck))
            {
                return Forbid();
            }

            var itemTags = tags.Where(t => t.Value != null).Select(t => new ItemTag
            {
                TaggedItemId = id,
                TaggedItemType = TaggedItemTypes.ManagedChallenge,
                CategoryKey = t.CategoryKey,
                Value = t.Value!
            }).ToList();

            var result = await _client.AddHubItemTags(itemTags, CurrentAuthContext);
            return new OkObjectResult(result);
        }

        /// <summary>
        /// Remove a tag from a managed challenge
        /// </summary>
        /// <param name="id">The managed challenge ID</param>
        /// <param name="categoryKey">The tag category key</param>
        /// <param name="value">The tag value</param>
        /// <returns>Action result</returns>
        [HttpDelete]
        [Route("{id}/tags")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Certify.Models.Config.ActionResult))]
        public async Task<IActionResult> RemoveManagedChallengeTag(string id, [FromQuery] string categoryKey, [FromQuery] string value)
        {
            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedChallenge,
                ResourceActionId = StandardResourceActions.ManagedChallengeUpdate,
                Identifier = id
            };

            if (!await IsAuthorized(_client, accessCheck))
            {
                return Forbid();
            }

            // Get all tags for this item and find the one to remove
            var allTags = await _client.GetAllHubItemTags(categoryKey, value, TaggedItemTypes.ManagedChallenge, null, CurrentAuthContext);
            var tagToRemove = allTags?.FirstOrDefault(t => t.TaggedItemId == id);

            if (tagToRemove == null)
            {
                return new OkObjectResult(new Certify.Models.Config.ActionResult("Tag not found", false));
            }

            var result = await _client.RemoveHubItemTags(new[] { tagToRemove.Id }, CurrentAuthContext);
            return new OkObjectResult(result);
        }

        /// <summary>
        /// Get the user's tag scopes from their scoped assigned roles
        /// </summary>
        private async Task<ICollection<TagScope>?> GetUserTagScopesForResource()
        {
            if (string.IsNullOrWhiteSpace(CurrentAuthContext?.UserId))
            {
                return null;
            }

            try
            {
                // Get scoped assigned roles from the current auth context
                // These are passed through from the access token validation
                if (CurrentAuthContext.ScopedAssignedRoles == null || !CurrentAuthContext.ScopedAssignedRoles.Any())
                {
                    return null;
                }

                // Get the assigned roles to extract tag scopes
                var assignedRoles = await _client.GetSecurityPrincipalAssignedRoles(CurrentAuthContext.UserId, CurrentAuthContext);

                if (assignedRoles == null || !assignedRoles.Any())
                {
                    return null;
                }

                // Filter to only the scoped roles for this token
                var scopedRoles = assignedRoles.Where(r => CurrentAuthContext.ScopedAssignedRoles.Contains(r.Id)).ToList();

                // Collect tag scopes from the scoped roles
                var tagScopes = new List<TagScope>();

                foreach (var role in scopedRoles)
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
