using System.Net.Http.Headers;
using System.Linq;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Base class for public api controllers
    /// </summary>
    public partial class ApiControllerBase : ControllerBase
    {

        /// <summary>
        /// Special auth context used internally for operations where the requesting user may not be authorized to query system state
        /// </summary>
        internal AuthContext SystemAuthContext = PrincipalAccess.SystemAuthContext;

        /// <summary>
        /// Check resource action access for the current user
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="check"></param>
        /// <returns></returns>
        internal Task<bool> IsAuthorized(ICertifyInternalApiClient internalApiClient, AccessCheck check)
            => PrincipalAccess.IsAuthorized(internalApiClient, CurrentAuthContext, check);

        /// <summary>
        /// Check resource action access for the given API access token
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="token"></param>
        /// <param name="check"></param>
        /// <returns></returns>
        internal async Task<Certify.Models.Config.ActionResult<AccessTokenAuthorizationContext>> IsAccessTokenAuthorized(ICertifyInternalApiClient internalApiClient, AccessToken token, AccessCheck check)
        {
            var result = await internalApiClient.CheckApiTokenHasAccess(token, check, CurrentAuthContext);

            if (result?.IsSuccess == true && !string.IsNullOrWhiteSpace(result.Result?.SecurityPrincipalId))
            {
                _accessTokenAuthContext = new AuthContext
                {
                    UserId = result.Result.SecurityPrincipalId,
                    ScopedAssignedRoles = result.Result.ScopedAssignedRoles?.Count > 0 ? result.Result.ScopedAssignedRoles : null
                };
            }

            return result;
        }

        /// <summary>
        /// Security principal resolved from an API access token while authorizing this request.
        /// </summary>
        private AuthContext? _accessTokenAuthContext;

        /// <summary>
        /// The security principal which authorized the current request: from the bearer token when there is one,
        /// otherwise from the API access token which authorized it. An access token authorized request reaching an
        /// [AllowAnonymous] endpoint has not been through the ApiToken authentication scheme, so HttpContext.User
        /// carries no principal for it and <see cref="CurrentAuthContext"/> alone is null.
        /// </summary>
        internal AuthContext? RequestAuthContext => CurrentAuthContext ?? _accessTokenAuthContext;

        /// <summary>
        /// Check that the caller may perform the given resource action.
        ///
        /// There is one principal to check, whichever credential the caller presented: the authentication
        /// middleware resolved it before the endpoint ran. This used to check the bearer token and then, on
        /// failure, read an API token off the request headers and resolve it a second time - repeating for the
        /// same credential the work the ApiToken scheme had already done, and reporting a missing header as the
        /// reason a bearer-authenticated caller was refused.
        /// </summary>
        internal async Task<Certify.Models.Config.ActionResult> CheckRequestAuthorized(ICertifyInternalApiClient internalApiClient, AccessCheck check)
        {
            if (string.IsNullOrWhiteSpace(CurrentAuthContext?.UserId))
            {
                return new Certify.Models.Config.ActionResult(
                    "No authenticated caller. Present a bearer token, or X-Client-ID and X-Client-Secret headers.",
                    false);
            }

            return await IsAuthorized(internalApiClient, check)
                ? new Certify.Models.Config.ActionResult("Authorized", true)
                : new Certify.Models.Config.ActionResult($"Not authorized to perform {check.ResourceActionId}", false);
        }
        /// <summary>
        /// The role assignment scope an API access token narrows its security principal to, used when previewing
        /// what a given identity can reach rather than what the principal holds overall.
        ///
        /// An empty result means the token is unscoped, so it carries the principal's whole role set. A failed
        /// result means the token could not be resolved for this principal, which must not be reported as
        /// unscoped: that would preview more access than the token actually grants.
        /// </summary>
        internal async Task<Certify.Models.Config.ActionResult<List<string>>> GetAssignedAccessTokenScope(
            ICertifyInternalApiClient internalApiClient,
            string? securityPrincipalId,
            string assignedAccessTokenId)
        {
            if (string.IsNullOrWhiteSpace(securityPrincipalId))
            {
                return new Certify.Models.Config.ActionResult<List<string>>("No security principal to resolve the access token for.", false);
            }

            try
            {
                var assignedTokens = await internalApiClient.GetAssignedAccessTokens(SystemAuthContext);

                var assignedToken = assignedTokens?.FirstOrDefault(t => string.Equals(t.Id, assignedAccessTokenId, StringComparison.OrdinalIgnoreCase));

                if (assignedToken == null || assignedToken.SecurityPrincipalId != securityPrincipalId)
                {
                    return new Certify.Models.Config.ActionResult<List<string>>(
                        $"Access token {assignedAccessTokenId} is not assigned to security principal {securityPrincipalId}.",
                        false);
                }

                return new Certify.Models.Config.ActionResult<List<string>>(
                    "Resolved access token scope",
                    true,
                    assignedToken.ScopedAssignedRoles?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList() ?? []);
            }
            catch (Exception exp)
            {
                return new Certify.Models.Config.ActionResult<List<string>>($"Failed to resolve access token scope: {exp.Message}", false);
            }
        }

        /// <summary>
        /// Identify the caller on an endpoint which is reachable without credentials but still behaves differently
        /// for a caller who presented some.
        ///
        /// This runs the same authentication handlers the middleware would have run for an [AuthorizedApi] endpoint,
        /// so there is one implementation of "are these credentials valid" rather than a second one for endpoints
        /// which opt out. A caller who presents nothing, or something invalid, is simply left unauthenticated: this
        /// identifies, it does not authorize, and every endpoint still checks its own resource action afterwards.
        /// </summary>
        internal async Task IdentifyOptionalCallerAsync()
        {
            if (HttpContext.User?.Identity?.IsAuthenticated == true)
            {
                return;
            }

            foreach (var scheme in new[]
            {
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                ApiKeyAuthenticationDefaults.AuthenticationScheme
            })
            {
                var result = await HttpContext.AuthenticateAsync(scheme);

                if (result.Succeeded && result.Principal != null)
                {
                    // a joining token is refused by the default policy on every other endpoint, so it does not
                    // identify a caller here either - this runs outside that policy, being an anonymous endpoint
                    if (HubTokenPurposes.IsManagementHubJoinToken(result.Principal))
                    {
                        continue;
                    }

                    HttpContext.User = result.Principal;
                    return;
                }
            }
        }

        internal AccessToken? GetAccessTokenFromRequest()
        {
            var clientId = Request.Headers["X-Client-ID"];
            var secret = Request.Headers["X-Client-Secret"];

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }
            else
            {
                return new AccessToken
                {
                    ClientId = clientId,
                    Secret = secret
                };
            }
        }

        internal async Task<ManagedInstanceRequestAuthValidationResult> ValidateManagedInstanceRequestAuthAsync()
        {
            var validator = HttpContext.RequestServices.GetService<ManagedInstanceRequestAuthValidator>()
                ?? ActivatorUtilities.CreateInstance<ManagedInstanceRequestAuthValidator>(HttpContext.RequestServices);

            return await validator.ValidateAsync(Request, HttpContext.RequestAborted);
        }

        /// <summary>
        /// Get the corresponding auth context to pass to the backend service
        /// </summary>
        /// <returns></returns>
        internal AuthContext? CurrentAuthContext
        {
            get
            {
                var authContext = PrincipalAccess.GetAuthContext(HttpContext?.User);
                if (authContext != null)
                {
                    var authHeaderValue = Request.Headers["Authorization"];
                    if (!string.IsNullOrWhiteSpace(authHeaderValue) && authHeaderValue.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        authContext.Token = AuthenticationHeaderValue.Parse(authHeaderValue!).Parameter;
                    }

                    return authContext;
                }

                // No authenticated principal, so there is no caller to report. Credentials are validated by the
                // authentication middleware for an [AuthorizedApi] endpoint, and by IdentifyOptionalCallerAsync
                // for one which is reachable anonymously: this used to re-validate the bearer token here instead,
                // which meant a second implementation of token validation with its own cache and expiry handling.
                return null;
            }
        }
    }
}
