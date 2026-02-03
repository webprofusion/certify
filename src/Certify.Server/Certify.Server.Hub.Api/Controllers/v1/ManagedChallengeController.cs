using Certify.Client;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Provides managed challenges such as DNS challenges on behalf of other ACME clients.
    /// Access is controlled via API tokens with optional tag-based scoping.
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public partial class ManagedChallengeController : ApiControllerBase
    {

        private readonly ILogger<ManagedChallengeController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public ManagedChallengeController(ILogger<ManagedChallengeController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// Request a challenge response. Requires API token with ManagedChallengeConsumer role.
        /// If the token has tag-scoped restrictions, only challenges with matching tags can be used.
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("request")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Certify.Models.Config.ActionResult))]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> PerformManagedChallenge(ManagedChallengeRequest request)
        {
            // Validate API token access for ManagedChallengeRequest action
            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedChallenge,
                ResourceActionId = StandardResourceActions.ManagedChallengeRequest
            };

            var authResult = await CheckRequestAuthorized(_client, accessCheck);

            if (!authResult.IsSuccess)
            {
                return Problem(
                    detail: authResult.Message ?? "Access denied",
                    statusCode: StatusCodes.Status401Unauthorized
                );
            }

            // Get tag scopes from the API token's scoped assigned roles
            var tagScopes = await GetTagScopesFromApiToken();

            // If there are tag restrictions, validate that a matching challenge exists
            if (tagScopes != null && tagScopes.Any())
            {
                var isAllowed = await ValidateChallengeAccessByTags(request, tagScopes);
                if (!isAllowed)
                {
                    return Problem(
                        detail: "Access denied. No accessible managed challenge found for this domain with your API token's tag scope.",
                        statusCode: StatusCodes.Status403Forbidden
                    );
                }
            }

            // Perform the challenge
            var result = await _client.PerformManagedChallenge(request, null);

            if (result.IsSuccess)
            {
                return new OkObjectResult(result);
            }
            else
            {
                return Problem(
                    detail: result.Message,
                    statusCode: StatusCodes.Status502BadGateway
                );
            }
        }

        /// <summary>
        /// Perform optional cleanup of a previously requested challenge response.
        /// Requires API token with ManagedChallengeConsumer role.
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("cleanup")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Certify.Models.Config.ActionResult))]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CleanupManagedChallenge(ManagedChallengeRequest request)
        {
            // Validate API token access for ManagedChallengeCleanup action
            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedChallenge,
                ResourceActionId = StandardResourceActions.ManagedChallengeCleanup
            };

            var authResult = await CheckRequestAuthorized(_client, accessCheck);

            if (!authResult.IsSuccess)
            {
                return Problem(
                    detail: authResult.Message ?? "Access denied",
                    statusCode: StatusCodes.Status401Unauthorized
                );
            }

            var result = await _client.CleanupManagedChallenge(request, null);
            return new OkObjectResult(result);
        }

        /// <summary>
        /// Validate that the requester has access to a challenge matching the domain via tags
        /// </summary>
        private async Task<bool> ValidateChallengeAccessByTags(ManagedChallengeRequest request, ICollection<TagScope> tagScopes)
        {
            try
            {
                // Get all managed challenges
                var challenges = await _client.GetManagedChallenges(null);

                if (challenges == null || !challenges.Any())
                {
                    return false;
                }

                // Get tags for all managed challenges
                var allChallengeTags = await _client.GetAllHubItemTags(null, null, TaggedItemTypes.ManagedChallenge, null, null);
                var tagsByChallengeId = allChallengeTags?.GroupBy(t => t.TaggedItemId)
                    .ToDictionary(g => g.Key, g => g.ToList()) ?? new Dictionary<string, List<ItemTag>>();

                // Find challenges that could match the domain
                var potentialMatches = challenges.Where(c =>
                    c.ChallengeConfig != null &&
                    (string.IsNullOrEmpty(c.ChallengeConfig.DomainMatch) || // wildcard config
                     DomainMatchesConfig(request.Identifier, c.ChallengeConfig.DomainMatch))).ToList();

                // Check if any potential match has tags that satisfy the scope
                foreach (var challenge in potentialMatches)
                {
                    tagsByChallengeId.TryGetValue(challenge.Id, out var challengeTags);

                    if (challengeTags == null || !challengeTags.Any())
                    {
                        // Untagged challenges are NOT accessible to tag-scoped tokens
                        continue;
                    }

                    // Check if challenge has at least one matching tag (OR logic)
                    var hasMatchingTag = tagScopes.Any(scope =>
                        challengeTags.Any(t => t.CategoryKey == scope.CategoryKey &&
                            (scope.Value == null || t.Value == scope.Value)));

                    if (hasMatchingTag)
                    {
                        return true; // Found an accessible challenge
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating challenge access by tags");
                return false;
            }
        }

        /// <summary>
        /// Check if a domain matches a challenge config domain match pattern
        /// </summary>
        private static bool DomainMatchesConfig(string domain, string domainMatch)
        {
            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(domainMatch))
            {
                return false;
            }

            domain = domain.ToLowerInvariant();
            domainMatch = domainMatch.ToLowerInvariant().Replace(",", ";");

            var patterns = domainMatch.Split(';').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p));

            foreach (var pattern in patterns)
            {
                if (pattern == domain)
                {
                    return true; // Exact match
                }

                if (pattern.StartsWith("*.") && domain.EndsWith(pattern.Substring(1)))
                {
                    return true; // Wildcard match
                }

                if (domain.EndsWith("." + pattern))
                {
                    return true; // Subdomain match
                }
            }

            return false;
        }

        /// <summary>
        /// Extract tag scopes from the API token's scoped assigned roles
        /// </summary>
        private async Task<ICollection<TagScope>?> GetTagScopesFromApiToken()
        {
            var accessToken = GetAccessTokenFromRequest();

            if (accessToken == null)
            {
                return null;
            }

            try
            {
                // Get the assigned access token to find scoped roles
                var assignedTokens = await _client.GetAssignedAccessTokens(null);

                if (assignedTokens == null)
                {
                    return null;
                }

                // Find the token matching our credentials
                AssignedAccessToken? matchingToken = null;
                foreach (var at in assignedTokens)
                {
                    var match = at.AccessTokens?.FirstOrDefault(t =>
                        t.ClientId == accessToken.ClientId && t.Secret == accessToken.Secret);
                    if (match != null)
                    {
                        matchingToken = at;
                        break;
                    }
                }

                if (matchingToken?.ScopedAssignedRoles == null || !matchingToken.ScopedAssignedRoles.Any())
                {
                    return null;
                }

                // Get the assigned roles for the security principal
                var assignedRoles = await _client.GetSecurityPrincipalAssignedRoles(matchingToken.SecurityPrincipalId, null);

                if (assignedRoles == null)
                {
                    return null;
                }

                // Filter to only the scoped roles for this token
                var scopedRoles = assignedRoles.Where(r => matchingToken.ScopedAssignedRoles.Contains(r.Id)).ToList();

                // Collect tag scopes
                var tagScopes = new List<TagScope>();
                foreach (var role in scopedRoles)
                {
                    if (role.ScopedTags != null)
                    {
                        tagScopes.AddRange(role.ScopedTags);
                    }
                }

                return tagScopes.Any() ? tagScopes : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tag scopes from API token");
                return null;
            }
        }
    }
}
