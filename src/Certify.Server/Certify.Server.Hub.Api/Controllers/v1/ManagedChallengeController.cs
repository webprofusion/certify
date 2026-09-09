using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
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
        [AuthorizedApi]
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

            // perform the challenge as the principal the request was authorized as
            var result = await _client.PerformManagedChallenge(authorization.Authorize(request), null);

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
        [AuthorizedApi]
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

            var operation = await _client.BeginManagedChallenge(authorization.Authorize(request), null);
            return AcceptedAtAction(nameof(GetManagedChallengeOperationStatus), new { id = operation.Id }, operation);
        }

        /// <summary>
        /// Get the status of a previously started managed challenge operation.
        /// </summary>
        /// <remarks>
        /// This endpoint cannot require authentication yet, so it is the one managed challenge operation which
        /// does not go through the authentication middleware. The Certify managed DNS provider polls it with no
        /// credentials at all (only a managed instance sends anything, and only its request signature), so the
        /// authorization below falls back to the credentials stored on the operation itself. That makes the
        /// unguessable operation id the effective capability.
        ///
        /// Requiring credentials here needs the provider to send them on the poll as well, which means an agent
        /// side change before the hub side can be tightened. Until then this endpoint identifies an optional
        /// caller explicitly rather than reimplementing token validation.
        /// </remarks>
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

            // a hub user may hold a bearer token even though the endpoint does not require one
            await IdentifyOptionalCallerAsync();

            var authorization = await AuthorizeOperationStatusAsync(operation);

            if (authorization.Denial != null)
            {
                return authorization.Denial;
            }

            return Ok(WithoutCallerIdentity(operation));
        }

        /// <summary>
        /// Authorize a status query for an operation.
        ///
        /// A caller who presented credentials is checked against their own roles. One who did not is relying on
        /// holding the operation id, which is an unguessable value issued only to whoever created the operation;
        /// the principal it was created for must still hold the action, so revoking that role stops the polling.
        /// This used to be expressed by re-resolving the API credentials stored on the operation, which meant
        /// keeping a live credential in memory for the operation's lifetime to answer a question about a role.
        /// </summary>
        private async Task<ManagedChallengeAuthorization> AuthorizeOperationStatusAsync(ManagedChallengeOperation operation)
        {
            var caller = operation.Caller;
            var actionId = ManagedChallengeRequestOrigins.GetRequiredResourceAction(
                caller?.Origin,
                StandardResourceActions.ManagedChallengeRequest);

            if (!string.IsNullOrWhiteSpace(CurrentAuthContext?.UserId))
            {
                return await IsAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedChallenge, actionId))
                    ? ManagedChallengeAuthorization.Allowed()
                    : Deny("Access denied. You are not authorized to read managed challenge operations.", StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(caller?.SecurityPrincipalId))
            {
                return DenyMissingAuthorization();
            }

            var check = new AccessCheck(caller.SecurityPrincipalId, ResourceTypes.ManagedChallenge, actionId);

            if (caller.ScopedAssignedRoles?.Count > 0)
            {
                check.ScopedAssignedRoles = caller.ScopedAssignedRoles;
            }

            return await _client.CheckSecurityPrincipalHasAccess(check, SystemAuthContext)
                ? ManagedChallengeAuthorization.Allowed()
                : Deny("Access denied. The security principal this operation was created for is no longer authorized.", StatusCodes.Status403Forbidden);
        }

        /// <summary>
        /// A copy of the operation with the identity it was authorized as removed.
        ///
        /// That identity is internal: this endpoint reads it to authorize the query, and an external caller has no
        /// business learning which security principal an operation belongs to. The stored request itself no longer
        /// carries the credentials it was made with - they authenticate the request and are dropped once they have,
        /// so there is nothing left here to strip beyond the resolved principal.
        ///
        /// The operation is copied rather than edited in place: the core holds operations in memory, so the instance
        /// returned here is the live one, and blanking its fields would destroy what a later status query needs.
        /// </summary>
        private static ManagedChallengeOperation WithoutCallerIdentity(ManagedChallengeOperation operation)
        {
            return new ManagedChallengeOperation
            {
                Id = operation.Id,
                Status = operation.Status,
                Result = operation.Result,
                DateCreated = operation.DateCreated,
                DateLastUpdated = operation.DateLastUpdated,
                DateStarted = operation.DateStarted,
                DateCompleted = operation.DateCompleted,
                Request = operation.Request,

                // Caller is deliberately not carried over
            };
        }

        /// <summary>
        /// Perform optional cleanup of a previously requested challenge response.
        /// Requires API token with ManagedChallengeConsumer role.
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("cleanup")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Certify.Models.Config.ActionResult))]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CleanupManagedChallenge(ManagedChallengeRequest request)
        {
            var authorization = await AuthorizeManagedChallengeActionAsync(StandardResourceActions.ManagedChallengeCleanup, request);
            if (authorization.Denial != null)
            {
                return authorization.Denial;
            }

            var result = await _client.CleanupManagedChallenge(authorization.Authorize(request), null);
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
            /// Pair the request with the identity it was authorized as, so fulfillment only selects challenges
            /// within that scope.
            ///
            /// The identity is built here rather than written onto the request: it comes from authorization and
            /// there is nothing of the caller's to overwrite. The origin is set for the same reason as the
            /// principal - it decides which resource action fulfillment checks, so a caller who could choose it
            /// could be checked against a role other than the one this endpoint authorized them under.
            ///
            /// The credentials the caller presented are dropped here too. They authenticated the request and have
            /// no purpose beyond that, so nothing downstream stores or returns them.
            /// </summary>
            public AuthorizedManagedChallengeRequest Authorize(ManagedChallengeRequest request)
            {
                return new AuthorizedManagedChallengeRequest
                {
                    Request = new ManagedChallengeRequest
                    {
                        ChallengeType = request.ChallengeType,
                        Identifier = request.Identifier,
                        ResponseKey = request.ResponseKey,
                        ResponseValue = request.ResponseValue,
                        DateTimePerformed = request.DateTimePerformed,
                        ManagedCertId = request.ManagedCertId
                    },
                    Caller = new ManagedChallengeCaller
                    {
                        SecurityPrincipalId = SecurityPrincipalId,
                        ScopedAssignedRoles = ScopedAssignedRoles,
                        Origin = ManagedChallengeRequestOrigins.ManagedChallengeApi
                    }
                };
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

                return await AuthorizeCallerScopeAsync(accessToken, request, actionId);
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
                    return await AuthorizeCallerScopeAsync(accessToken, request, actionId);
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
        /// Check that hub joining is authorized before the hub assigned id header is trusted enough to look an
        /// instance up by it.
        ///
        /// Normally the caller is authenticated and this is a question about the principal they authenticated as,
        /// which the authentication middleware already resolved their credentials to. The operation status endpoint
        /// is the exception: it is reachable without credentials and authorizes against the token stored on the
        /// operation, so on that path the token still has to be resolved here.
        /// </summary>
        private async Task<Certify.Models.Config.ActionResult> CheckJoiningAuthorizedAsync(AccessToken accessToken)
        {
            var joiningCheck = new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin);

            if (!string.IsNullOrWhiteSpace(CurrentAuthContext?.UserId))
            {
                return await IsAuthorized(_client, joiningCheck)
                    ? new Certify.Models.Config.ActionResult("Authorized to join the hub", true)
                    : new Certify.Models.Config.ActionResult("Caller is not authorized to join the hub as a managed instance.", false);
            }

            return await IsAccessTokenAuthorized(_client, accessToken, joiningCheck);
        }

        /// <summary>
        /// Authorize the caller as a managed instance signing with the hub joining credentials. On success the
        /// instance's own security principal is returned: the joining credentials belong to the shared managed
        /// instance service principal, which only grants hub joining, so fulfillment scoped to it would deny
        /// every managed challenge.
        /// </summary>
        private async Task<ManagedChallengeAuthorization> AuthorizeManagedInstanceManagedChallengeAsync(ManagedChallengeRequest request, string actionId, AccessToken accessToken)
        {
            var joiningAccessCheck = await CheckJoiningAuthorizedAsync(accessToken);
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

            var authorized = await _client.AuthorizeManagedChallengeIdentifiers(
                new ManagedChallengeAuthorizationCheck
                {
                    SecurityPrincipalId = managedInstance.SecurityPrincipalId,
                    Identifiers = [request.Identifier],
                    RequiredActionId = actionId,
                    RequireSatisfiableChallenge = true
                },
                SystemAuthContext);

            if (!authorized.IsSuccess)
            {
                _logger.LogWarning(
                    "ValidateManagedInstanceChallengeAccessAsync found no accessible managed challenge for managed instance {managedInstanceId} / security principal {securityPrincipalId}: {message}",
                    managedInstance.InstanceId,
                    managedInstance.SecurityPrincipalId,
                    authorized.Message);
            }

            return authorized.IsSuccess;
        }

        /// <summary>
        /// Authorize the caller as the security principal they authenticated as, and scope fulfillment to it.
        ///
        /// Every caller is scoped by their own principal, including one signed in interactively. That used to be
        /// the exception: a caller with no API access token resolved to no principal at all, and fulfillment reads
        /// an absent principal as the unscoped system path, so a tag scoped operator calling this endpoint got
        /// access to every managed challenge rather than the ones their roles select.
        /// </summary>
        private async Task<ManagedChallengeAuthorization> AuthorizeCallerScopeAsync(AccessToken? accessToken, ManagedChallengeRequest? request, string actionId)
        {
            var principal = ResolveRequestPrincipal();

            if (principal == null && accessToken != null)
            {
                // Credentials which did not resolve: revoked, expired, or the lookup failed. Falling through to
                // Allowed() here would clear the principal from the request and hand the caller the unscoped path,
                // so a failure to establish who they are would widen their access rather than deny it.
                _logger.LogWarning(
                    "AuthorizeManagedChallengeActionAsync denied for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}: the access token presented did not resolve to a security principal.",
                    actionId,
                    request?.ManagedCertId ?? "<none>",
                    request?.Identifier ?? "<none>",
                    request?.ChallengeType ?? "<none>");

                return Deny(
                    "Access denied. The API credentials presented could not be resolved to a security principal.",
                    StatusCodes.Status403Forbidden);
            }

            if (principal == null)
            {
                return Deny(
                    "Access denied. The request could not be attributed to a security principal.",
                    StatusCodes.Status403Forbidden);
            }

            var allowed = ManagedChallengeAuthorization.Allowed(principal.SecurityPrincipalId, principal.ScopedAssignedRoles);

            if (string.IsNullOrWhiteSpace(request?.Identifier))
            {
                return allowed;
            }

            var authorized = await _client.AuthorizeManagedChallengeIdentifiers(
                new ManagedChallengeAuthorizationCheck
                {
                    SecurityPrincipalId = principal.SecurityPrincipalId,
                    Identifiers = [request.Identifier],
                    ScopedAssignedRoles = principal.ScopedAssignedRoles,
                    RequiredActionId = actionId
                },
                SystemAuthContext);

            if (authorized.IsSuccess)
            {
                return allowed;
            }

            _logger.LogWarning(
                "AuthorizeManagedChallengeActionAsync denied by role scope for action {actionId}, managed cert {managedCertId}, identifier {identifier}, challenge type {challengeType}: {message}",
                actionId,
                request.ManagedCertId,
                request.Identifier,
                request.ChallengeType,
                authorized.Message);

            return Deny(
                authorized.Message ?? "Access denied. No accessible managed challenge found for this domain within your role scope.",
                StatusCodes.Status403Forbidden);
        }

        /// <summary>
        /// The principal this request acts as.
        ///
        /// Normally that is the caller, whose credential the authentication middleware already resolved. On the
        /// operation status endpoint, which is reachable without credentials, there may be no authenticated caller
        /// and the principal is instead the one the stored operation's token resolved to during authorization
        /// above. <see cref="ApiControllerBase.RequestAuthContext"/> is exactly that distinction, so neither case
        /// needs a credential resolved a second time here.
        /// </summary>
        private RequestPrincipal? ResolveRequestPrincipal()
        {
            var authContext = RequestAuthContext;

            if (string.IsNullOrWhiteSpace(authContext?.UserId))
            {
                return null;
            }

            return new RequestPrincipal(authContext.UserId, authContext.ScopedAssignedRoles?.ToList());
        }

        /// <summary>
        /// The security principal and role scope a request acts as.
        /// </summary>
        private sealed record RequestPrincipal(string SecurityPrincipalId, List<string>? ScopedAssignedRoles);
    }
}
