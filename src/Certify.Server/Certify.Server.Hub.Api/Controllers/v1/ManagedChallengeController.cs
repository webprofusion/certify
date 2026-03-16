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
            var authResult = await AuthorizeManagedChallengeRequestAsync(request);
            if (authResult != null)
            {
                return authResult;
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
        /// Start a managed challenge operation and return an operation id for polling.
        /// Requires API token with ManagedChallengeConsumer role.
        /// </summary>
        [HttpPost]
        [Route("requestbegin")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(ManagedChallengeOperation))]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> BeginManagedChallenge(ManagedChallengeRequest request)
        {
            var authResult = await AuthorizeManagedChallengeRequestAsync(request);
            if (authResult != null)
            {
                return authResult;
            }

            var operation = await _client.BeginManagedChallenge(request, null);
            return AcceptedAtAction(nameof(GetManagedChallengeOperationStatus), new { id = operation.Id }, operation);
        }

        /// <summary>
        /// Get the status of a previously started managed challenge operation.
        /// </summary>
        [HttpGet]
        [Route("requeststatus/{id}")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedChallengeOperation))]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetManagedChallengeOperationStatus(string id)
        {
            var operation = await _client.GetManagedChallengeOperation(id, null);

            if (operation == null)
            {
                return NotFound();
            }

            var authResult = await AuthorizeManagedChallengeActionAsync(StandardResourceActions.ManagedChallengeRequest, operation?.Request);

            if (authResult != null)
            {
                return authResult;
            }

            return Ok(operation);
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
            var authResult = await AuthorizeManagedChallengeActionAsync(StandardResourceActions.ManagedChallengeCleanup, request);
            if (authResult != null)
            {
                return authResult;
            }

            var result = await _client.CleanupManagedChallenge(request, null);
            return new OkObjectResult(result);
        }

        private sealed class ManagedChallengeAuthorizationResult
        {
            public bool IsSuccess { get; init; }
            public bool WasEvaluated { get; init; }
            public int StatusCode { get; init; } = StatusCodes.Status401Unauthorized;
            public string Message { get; init; } = "Access denied";
        }

        private async Task<IActionResult?> AuthorizeManagedChallengeActionAsync(string actionId, ManagedChallengeRequest? request = null)
        {
            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedChallenge,
                ResourceActionId = actionId
            };

            if (await IsAuthorized(_client, accessCheck))
            {
                if (request != null)
                {
                    var tagScopes = await GetTagScopesFromAccessToken(GetAccessTokenFromRequest(), null);
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
                }

                return null;
            }

            var accessToken = GetAccessTokenFromRequestOrManagedChallenge(request);
            if (accessToken != null)
            {
                var authResult = await IsAccessTokenAuthorized(_client, accessToken, accessCheck);
                if (authResult.IsSuccess)
                {
                    if (request != null)
                    {
                        var tagScopes = await GetTagScopesFromAccessToken(accessToken, request);
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
                    }

                    return null;
                }

                if (request != null)
                {
                    var managedInstanceAuthorization = await AuthorizeManagedInstanceManagedChallengeAsync(request, actionId, accessToken);
                    if (managedInstanceAuthorization.IsSuccess)
                    {
                        return null;
                    }

                    if (managedInstanceAuthorization.WasEvaluated)
                    {
                        return Problem(
                            detail: managedInstanceAuthorization.Message,
                            statusCode: managedInstanceAuthorization.StatusCode
                        );
                    }
                }

                return Problem(
                detail: "Authorization header, X-Client-ID/X-Client-Secret headers, or AuthKey/AuthSecret request values are required.",
                    statusCode: StatusCodes.Status401Unauthorized
                );
            }

            if (request != null)
            {
                var managedInstanceAuthorization = await AuthorizeManagedInstanceManagedChallengeAsync(request, actionId, null);
                if (managedInstanceAuthorization.IsSuccess)
                {
                    return null;
                }

                if (managedInstanceAuthorization.WasEvaluated)
                {
                    return Problem(
                        detail: managedInstanceAuthorization.Message,
                        statusCode: managedInstanceAuthorization.StatusCode
                    );
                }
            }

            return Problem(
                detail: "Authorization header, X-Client-ID/X-Client-Secret headers, or AuthKey/AuthSecret request values are required.",
                statusCode: StatusCodes.Status401Unauthorized
            );
        }

        private async Task<IActionResult?> AuthorizeManagedChallengeRequestAsync(ManagedChallengeRequest request)
        {
            return await AuthorizeManagedChallengeActionAsync(StandardResourceActions.ManagedChallengeRequest, request);
        }

        private AccessToken? GetAccessTokenFromRequestOrManagedChallenge(ManagedChallengeRequest? request)
        {
            var accessToken = GetAccessTokenFromRequest();
            if (accessToken != null)
            {
                return accessToken;
            }

            return GetAccessTokenFromManagedChallengeRequest(request);
        }

        private static AccessToken? GetAccessTokenFromManagedChallengeRequest(ManagedChallengeRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.AuthKey) || string.IsNullOrWhiteSpace(request.AuthSecret))
            {
                return null;
            }

            return new AccessToken
            {
                ClientId = request.AuthKey,
                Secret = request.AuthSecret
            };
        }

        private async Task<ManagedChallengeAuthorizationResult> AuthorizeManagedInstanceManagedChallengeAsync(ManagedChallengeRequest request, string actionId, AccessToken? accessToken)
        {
            if (accessToken == null)
            {
                return new ManagedChallengeAuthorizationResult { WasEvaluated = false };
            }

            var joiningAccessCheck = await IsAccessTokenAuthorized(_client, accessToken, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));
            if (!joiningAccessCheck.IsSuccess)
            {
                return new ManagedChallengeAuthorizationResult
                {
                    WasEvaluated = true,
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Message = joiningAccessCheck.Message ?? "Managed instance joining key is not authorized."
                };
            }

            var requestingInstanceId = Request.Headers["X-Certify-HubAssignedId"].ToString();
            if (string.IsNullOrWhiteSpace(requestingInstanceId))
            {
                return new ManagedChallengeAuthorizationResult
                {
                    WasEvaluated = true,
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Message = "X-Certify-HubAssignedId header is required."
                };
            }

            var instanceAuth = await ValidateManagedInstanceRequestAuthAsync();
            if (!instanceAuth.IsSuccess)
            {
                return new ManagedChallengeAuthorizationResult
                {
                    WasEvaluated = true,
                    StatusCode = instanceAuth.StatusCode,
                    Message = instanceAuth.Message
                };
            }

            var matchingInstance = instanceAuth.ManagedInstance;

            if (matchingInstance == null || string.IsNullOrWhiteSpace(matchingInstance.SecurityPrincipalId))
            {
                return new ManagedChallengeAuthorizationResult
                {
                    WasEvaluated = true,
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Message = "Managed instance is not registered with a linked security principal."
                };
            }

            var isAuthorized = await ValidateManagedInstanceChallengeAccessAsync(request, matchingInstance, actionId);

            return isAuthorized
                ? new ManagedChallengeAuthorizationResult { IsSuccess = true, WasEvaluated = true, Message = "Authorized as managed instance challenge consumer." }
                : new ManagedChallengeAuthorizationResult
                {
                    WasEvaluated = true,
                    StatusCode = StatusCodes.Status403Forbidden,
                    Message = "Managed instance is not permitted to access a matching managed challenge for this request."
                };
        }

        private async Task<bool> ValidateManagedInstanceChallengeAccessAsync(ManagedChallengeRequest request, ManagedInstanceInfo managedInstance, string actionId)
        {
            try
            {
                var potentialMatches = await GetPotentialManagedChallenges(request);

                foreach (var challenge in potentialMatches)
                {
                    var challengeTags = (await _client.GetHubItemTags(TaggedItemTypes.ManagedChallenge, challenge.Id, SystemAuthContext))?.ToList() ?? [];

                    var accessCheck = new AccessCheck
                    {
                        SecurityPrincipalId = managedInstance.SecurityPrincipalId,
                        ResourceType = ResourceTypes.ManagedChallenge,
                        ResourceActionId = actionId,
                        Identifier = challenge.Id,
                        ResourceTags = challengeTags
                    };

                    var checkAuthContext = new AuthContext { UserId = managedInstance.SecurityPrincipalId };
                    if (await _client.CheckSecurityPrincipalHasAccess(accessCheck, checkAuthContext))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating managed instance challenge access");
                return false;
            }
        }

        /// <summary>
        /// Validate that the requester has access to a challenge matching the domain via tags
        /// </summary>
        private async Task<bool> ValidateChallengeAccessByTags(ManagedChallengeRequest request, ICollection<TagScope> tagScopes)
        {
            try
            {
                var potentialMatches = await GetPotentialManagedChallenges(request);
                if (!potentialMatches.Any())
                {
                    return false;
                }

                var allChallengeTags = await _client.GetAllHubItemTags(null, null, TaggedItemTypes.ManagedChallenge, null, SystemAuthContext);
                var tagsByChallengeId = allChallengeTags?.GroupBy(t => t.TaggedItemId)
                    .ToDictionary(g => g.Key, g => g.ToList()) ?? new Dictionary<string, List<ItemTag>>();

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

        private async Task<List<ManagedChallenge>> GetPotentialManagedChallenges(ManagedChallengeRequest request)
        {
            var challenges = await _client.GetManagedChallenges(SystemAuthContext);

            if (challenges == null || !challenges.Any())
            {
                return [];
            }

            return challenges.Where(c =>
                c.ChallengeConfig != null &&
                (string.IsNullOrEmpty(c.ChallengeConfig.DomainMatch) ||
                 DomainMatchesConfig(request.Identifier, c.ChallengeConfig.DomainMatch))).ToList();
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
        private async Task<ICollection<TagScope>?> GetTagScopesFromAccessToken(AccessToken? accessToken, ManagedChallengeRequest? request)
        {
            accessToken ??= GetAccessTokenFromManagedChallengeRequest(request);

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
