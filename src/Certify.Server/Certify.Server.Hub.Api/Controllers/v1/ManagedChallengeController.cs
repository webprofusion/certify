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
            var authResult = await AuthorizeManagedChallengeRequestAsync(request);
            if (authResult != null)
            {
                return authResult;
            }

            await ApplyCallerScopeToRequestAsync(request);

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
            var authResult = await AuthorizeManagedChallengeRequestAsync(request);
            if (authResult != null)
            {
                return authResult;
            }

            await ApplyCallerScopeToRequestAsync(request);

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

            await ApplyCallerScopeToRequestAsync(request);

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
            var managedCertId = request?.ManagedCertId ?? "<none>";
            var identifier = request?.Identifier ?? "<none>";
            var challengeType = request?.ChallengeType ?? "<none>";
            var hasManagedInstanceHeader = HasManagedInstanceRequestHeader();

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

                var scopeDenied = await AuthorizeIdentifierScopeAsync(GetAccessTokenFromRequest(), request, actionId);
                if (scopeDenied != null)
                {
                    return scopeDenied;
                }

                return null;
            }

            _logger.LogDebug(
                "AuthorizeManagedChallengeActionAsync direct access token authorization failed for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}.",
                actionId,
                managedCertId,
                identifier,
                challengeType);

            var accessToken = GetAccessTokenFromRequestOrManagedChallenge(request);
            if (accessToken != null)
            {
                ManagedChallengeAuthorizationResult? managedInstanceAuthorization = null;
                if (request != null && hasManagedInstanceHeader)
                {
                    managedInstanceAuthorization = await AuthorizeManagedInstanceManagedChallengeAsync(request, actionId, accessToken);
                    if (managedInstanceAuthorization.IsSuccess)
                    {
                        return null;
                    }

                    _logger.LogWarning(
                        "AuthorizeManagedChallengeActionAsync managed-instance authorization failed for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}: {message}",
                        actionId,
                        managedCertId,
                        identifier,
                        challengeType,
                        managedInstanceAuthorization.Message);
                }

                var authResult = await IsAccessTokenAuthorized(_client, accessToken, accessCheck);
                if (authResult.IsSuccess)
                {
                    var scopeDenied = await AuthorizeIdentifierScopeAsync(accessToken, request, actionId);
                    if (scopeDenied != null)
                    {
                        return scopeDenied;
                    }

                    return null;
                }

                if (managedInstanceAuthorization?.WasEvaluated == true)
                {
                    _logger.LogWarning(
                        "AuthorizeManagedChallengeActionAsync denied by managed-instance authorization for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}: {message}",
                        actionId,
                        managedCertId,
                        identifier,
                        challengeType,
                        managedInstanceAuthorization.Message);

                    return Problem(
                        detail: managedInstanceAuthorization.Message,
                        statusCode: managedInstanceAuthorization.StatusCode
                    );
                }

                _logger.LogWarning(
                    "AuthorizeManagedChallengeActionAsync found no valid access token or managed-instance auth for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}.",
                    actionId,
                    managedCertId,
                    identifier,
                    challengeType);

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
                    _logger.LogWarning(
                        "AuthorizeManagedChallengeActionAsync denied by managed-instance authorization without access token for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}: {message}",
                        actionId,
                        managedCertId,
                        identifier,
                        challengeType,
                        managedInstanceAuthorization.Message);

                    return Problem(
                        detail: managedInstanceAuthorization.Message,
                        statusCode: managedInstanceAuthorization.StatusCode
                    );
                }
            }

            _logger.LogWarning(
                "AuthorizeManagedChallengeActionAsync rejected request due to missing authorization for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}.",
                actionId,
                managedCertId,
                identifier,
                challengeType);

            return Problem(
                detail: "Authorization header, X-Client-ID/X-Client-Secret headers, or AuthKey/AuthSecret request values are required.",
                statusCode: StatusCodes.Status401Unauthorized
            );
        }

        private bool HasManagedInstanceRequestHeader()
        {
            return !string.IsNullOrWhiteSpace(Request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName].ToString());
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
        /// Deny the request when the caller's API token is tag-scoped and no accessible managed
        /// challenge covers the requested identifier. Unrestricted principals are unaffected.
        /// </summary>
        private async Task<IActionResult?> AuthorizeIdentifierScopeAsync(AccessToken? accessToken, ManagedChallengeRequest? request, string actionId)
        {
            if (string.IsNullOrWhiteSpace(request?.Identifier))
            {
                return null;
            }

            var principal = await _scopeService.ResolveAccessTokenPrincipal(accessToken);
            if (principal == null)
            {
                // not an API-token principal - other authorization paths apply
                return null;
            }

            var (isAuthorized, failureReason) = await _scopeService.AuthorizeIdentifiersForPrincipal(
                principal.SecurityPrincipalId,
                [request.Identifier],
                principal.ScopedAssignedRoles,
                actionId);

            if (isAuthorized)
            {
                return null;
            }

            _logger.LogWarning(
                "AuthorizeManagedChallengeActionAsync denied by role scope for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}.",
                actionId,
                request.ManagedCertId,
                request.Identifier,
                request.ChallengeType);

            return Problem(
                detail: failureReason ?? "Access denied. No accessible managed challenge found for this domain with your API token's role scope.",
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        /// <summary>
        /// Attach the caller's security principal and scoped assigned roles to the challenge request
        /// so fulfillment only selects challenges within that scope. Caller-supplied values are never
        /// trusted: identity is always derived from the authenticated access token, or cleared.
        /// </summary>
        private async Task ApplyCallerScopeToRequestAsync(ManagedChallengeRequest request)
        {
            if (request == null)
            {
                return;
            }

            // never trust principal/scope supplied by the client
            var principal = await _scopeService.ResolveAccessTokenPrincipal(GetAccessTokenFromRequestOrManagedChallenge(request));

            request.SecurityPrincipalId = principal?.SecurityPrincipalId;
            request.ScopedAssignedRoles = principal?.ScopedAssignedRoles;
        }
    }
}
