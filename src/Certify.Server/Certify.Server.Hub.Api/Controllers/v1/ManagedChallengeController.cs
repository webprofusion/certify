using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Services;
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

        private readonly ManagedChallengeScopeService _scopeService;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="scopeService"></param>
        public ManagedChallengeController(ILogger<ManagedChallengeController> logger, ICertifyInternalApiClient client, ManagedChallengeScopeService scopeService)
        {
            _logger = logger;
            _client = client;
            _scopeService = scopeService;
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
            var authorization = await AuthorizeManagedChallengeRequestAsync(request);
            if (authorization.Denial != null)
            {
                return authorization.Denial;
            }

            authorization.ApplyTo(request);

            // Perform the challenge
            var result = await _client.PerformManagedChallenge(request, null);

            if (result.IsSuccess)
            {
                return new OkObjectResult(result);
            }
            else
            {
                _logger.LogWarning(
                    "PerformManagedChallenge failed for managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}: {message}",
                    request?.ManagedCertId,
                    request?.Identifier,
                    request?.ChallengeType,
                    result.Message);
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
            var authorization = await AuthorizeManagedChallengeRequestAsync(request);
            if (authorization.Denial != null)
            {
                return authorization.Denial;
            }

            authorization.ApplyTo(request);

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

            var authorization = await AuthorizeManagedChallengeActionAsync(StandardResourceActions.ManagedChallengeRequest, operation?.Request);

            if (authorization.Denial != null)
            {
                return authorization.Denial;
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
            var authorization = await AuthorizeManagedChallengeActionAsync(StandardResourceActions.ManagedChallengeCleanup, request);
            if (authorization.Denial != null)
            {
                return authorization.Denial;
            }

            authorization.ApplyTo(request);

            var result = await _client.CleanupManagedChallenge(request, null);
            return new OkObjectResult(result);
        }

        /// <summary>
        /// The outcome of authorizing a managed challenge action: either a denial to return to the caller,
        /// or the security principal and role scope the request was authorized as. Fulfillment must run as
        /// that principal, so whichever authorization path succeeds also decides the identity applied to the
        /// request - re-deriving it afterwards would substitute a different principal than the one checked.
        /// </summary>
        private sealed class ManagedChallengeAuthorization
        {
            public IActionResult? Denial { get; init; }

            /// <summary>
            /// Why authorization was refused, for logging alongside the denial the caller receives.
            /// </summary>
            public string? DenialReason { get; init; }

            public string? SecurityPrincipalId { get; init; }

            public List<string>? ScopedAssignedRoles { get; init; }

            public bool IsAllowed => Denial == null;

            public static ManagedChallengeAuthorization Allowed(string? securityPrincipalId = null, List<string>? scopedAssignedRoles = null)
                => new() { SecurityPrincipalId = securityPrincipalId, ScopedAssignedRoles = scopedAssignedRoles };

            /// <summary>
            /// Attach the authorized principal and role scope to the request so fulfillment only selects
            /// challenges within that scope. Caller supplied values are never trusted: identity comes from
            /// authorization, or is cleared.
            /// </summary>
            public void ApplyTo(ManagedChallengeRequest request)
            {
                request.SecurityPrincipalId = SecurityPrincipalId;
                request.ScopedAssignedRoles = ScopedAssignedRoles;
            }
        }

        /// <summary>
        /// Refuse the request, keeping the reason available for logging as well as for the caller.
        /// </summary>
        private ManagedChallengeAuthorization Deny(string detail, int statusCode) => new()
        {
            Denial = Problem(detail: detail, statusCode: statusCode),
            DenialReason = detail
        };

        private async Task<ManagedChallengeAuthorization> AuthorizeManagedChallengeActionAsync(string actionId, ManagedChallengeRequest? request = null)
        {
            var managedCertId = request?.ManagedCertId ?? "<none>";
            var identifier = request?.Identifier ?? "<none>";
            var challengeType = request?.ChallengeType ?? "<none>";
            var hasManagedInstanceHeader = HasManagedInstanceRequestHeader();
            var accessToken = GetAccessTokenFromRequestOrManagedChallenge(request);

            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedChallenge,
                ResourceActionId = actionId
            };

            _logger.LogDebug(
                "AuthorizeManagedChallengeActionAsync evaluating action {actionId} for managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}. Managed instance header present: {hasManagedInstanceHeader}.",
                actionId,
                managedCertId,
                identifier,
                challengeType,
                hasManagedInstanceHeader);

            if (await IsAuthorized(_client, accessCheck))
            {
                _logger.LogDebug(
                    "AuthorizeManagedChallengeActionAsync succeeded via direct access token for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}.",
                    actionId,
                    managedCertId,
                    identifier,
                    challengeType);

                return await AuthorizeAccessTokenScopeAsync(accessToken, request, actionId);
            }

            _logger.LogDebug(
                "AuthorizeManagedChallengeActionAsync direct access token authorization failed for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}.",
                actionId,
                managedCertId,
                identifier,
                challengeType);

            if (accessToken != null)
            {
                ManagedChallengeAuthorization? managedInstanceAuthorization = null;
                if (request != null && hasManagedInstanceHeader)
                {
                    managedInstanceAuthorization = await AuthorizeManagedInstanceManagedChallengeAsync(request, actionId, accessToken);
                    if (managedInstanceAuthorization.IsAllowed)
                    {
                        return managedInstanceAuthorization;
                    }

                    _logger.LogWarning(
                        "AuthorizeManagedChallengeActionAsync managed-instance authorization failed for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}: {message}",
                        actionId,
                        managedCertId,
                        identifier,
                        challengeType,
                        managedInstanceAuthorization.DenialReason);
                }

                var authResult = await IsAccessTokenAuthorized(_client, accessToken, accessCheck);
                if (authResult.IsSuccess)
                {
                    return await AuthorizeAccessTokenScopeAsync(accessToken, request, actionId);
                }

                if (managedInstanceAuthorization != null)
                {
                    _logger.LogWarning(
                        "AuthorizeManagedChallengeActionAsync denied by managed-instance authorization for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}: {message}",
                        actionId,
                        managedCertId,
                        identifier,
                        challengeType,
                        managedInstanceAuthorization.DenialReason);

                    return managedInstanceAuthorization;
                }

                _logger.LogWarning(
                    "AuthorizeManagedChallengeActionAsync found no valid access token or managed-instance auth for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}.",
                    actionId,
                    managedCertId,
                    identifier,
                    challengeType);

                return DenyMissingAuthorization();
            }

            // no access token at all: a managed instance signs with the joining credentials, so instance
            // authorization has nothing to check either
            _logger.LogWarning(
                "AuthorizeManagedChallengeActionAsync rejected request due to missing authorization for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}.",
                actionId,
                managedCertId,
                identifier,
                challengeType);

            return DenyMissingAuthorization();
        }

        private ManagedChallengeAuthorization DenyMissingAuthorization()
            => Deny(
                "Authorization header, X-Client-ID/X-Client-Secret headers, or AuthKey/AuthSecret request values are required.",
                StatusCodes.Status401Unauthorized);

        private bool HasManagedInstanceRequestHeader()
        {
            return !string.IsNullOrWhiteSpace(Request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName].ToString());
        }

        private async Task<ManagedChallengeAuthorization> AuthorizeManagedChallengeRequestAsync(ManagedChallengeRequest request)
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

        /// <summary>
        /// Authorize the caller as a managed instance signing with the hub joining credentials. On success the
        /// instance's own security principal is returned: the joining credentials belong to the shared managed
        /// instance service principal, which only grants hub joining, so fulfillment scoped to it would deny
        /// every managed challenge.
        /// </summary>
        private async Task<ManagedChallengeAuthorization> AuthorizeManagedInstanceManagedChallengeAsync(ManagedChallengeRequest request, string actionId, AccessToken accessToken)
        {
            var joiningAccessCheck = await IsAccessTokenAuthorized(_client, accessToken, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));
            if (!joiningAccessCheck.IsSuccess)
            {
                return Deny(joiningAccessCheck.Message ?? "Managed instance joining key is not authorized.", StatusCodes.Status401Unauthorized);
            }

            var requestingInstanceId = Request.Headers["X-Certify-HubAssignedId"].ToString();
            if (string.IsNullOrWhiteSpace(requestingInstanceId))
            {
                return Deny("X-Certify-HubAssignedId header is required.", StatusCodes.Status401Unauthorized);
            }

            var instanceAuth = await ValidateManagedInstanceRequestAuthAsync();
            if (!instanceAuth.IsSuccess)
            {
                return Deny(instanceAuth.Message, instanceAuth.StatusCode);
            }

            var matchingInstance = instanceAuth.ManagedInstance;

            if (matchingInstance == null || string.IsNullOrWhiteSpace(matchingInstance.SecurityPrincipalId))
            {
                return Deny("Managed instance is not registered with a linked security principal.", StatusCodes.Status401Unauthorized);
            }

            var isAuthorized = await ValidateManagedInstanceChallengeAccessAsync(request, matchingInstance, actionId);

            return isAuthorized
                ? ManagedChallengeAuthorization.Allowed(matchingInstance.SecurityPrincipalId)
                : Deny("Managed instance is not permitted to access a matching managed challenge for this request.", StatusCodes.Status403Forbidden);
        }

        private async Task<bool> ValidateManagedInstanceChallengeAccessAsync(ManagedChallengeRequest request, ManagedInstanceInfo managedInstance, string actionId)
        {
            if (string.IsNullOrWhiteSpace(request?.Identifier))
            {
                return false;
            }

            var (canSatisfy, failureReason, _) = await _scopeService.ValidatePrincipalCanSatisfyIdentifiers(
                managedInstance.SecurityPrincipalId,
                [request.Identifier],
                scopedAssignedRoles: null,
                requiredActionId: actionId);

            if (!canSatisfy)
            {
                _logger.LogWarning(
                    "ValidateManagedInstanceChallengeAccessAsync found no accessible managed challenge for managed instance {managedInstanceId} / security principal {securityPrincipalId}: {message}",
                    managedInstance.InstanceId,
                    managedInstance.SecurityPrincipalId,
                    failureReason);
            }

            return canSatisfy;
        }

        /// <summary>
        /// Authorize the caller as the security principal their access token belongs to, denying the request
        /// when that principal is tag-scoped and no accessible managed challenge covers the requested
        /// identifier. Unrestricted principals are unaffected, and a caller authenticated without an access
        /// token (an interactive hub user) resolves to no principal, leaving fulfillment unscoped.
        /// </summary>
        private async Task<ManagedChallengeAuthorization> AuthorizeAccessTokenScopeAsync(AccessToken? accessToken, ManagedChallengeRequest? request, string actionId)
        {
            var principal = await _scopeService.ResolveAccessTokenPrincipal(accessToken);
            if (principal == null)
            {
                // not an API-token principal - other authorization paths apply
                return ManagedChallengeAuthorization.Allowed();
            }

            var allowed = ManagedChallengeAuthorization.Allowed(principal.SecurityPrincipalId, principal.ScopedAssignedRoles);

            if (string.IsNullOrWhiteSpace(request?.Identifier))
            {
                return allowed;
            }

            var (isAuthorized, failureReason) = await _scopeService.AuthorizeIdentifiersForPrincipal(
                principal.SecurityPrincipalId,
                [request.Identifier],
                principal.ScopedAssignedRoles,
                actionId);

            if (isAuthorized)
            {
                return allowed;
            }

            _logger.LogWarning(
                "AuthorizeManagedChallengeActionAsync denied by role scope for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}.",
                actionId,
                request.ManagedCertId,
                request.Identifier,
                request.ChallengeType);

            return Deny(
                failureReason ?? "Access denied. No accessible managed challenge found for this domain with your API token's role scope.",
                StatusCodes.Status403Forbidden);
        }
    }
}
