using System.Net.Http.Headers;
using System.Linq;
using System.Security.Claims;
using Certify.Client;
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
        internal AuthContext SystemAuthContext = new AuthContext { UserId = StandardSecurityPrincipals.System };

        /// <summary>
        /// Check resource action access for the current user
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="check"></param>
        /// <returns></returns>
        internal async Task<bool> IsAuthorized(ICertifyInternalApiClient internalApiClient, AccessCheck check)
        {
            if (string.IsNullOrWhiteSpace(CurrentAuthContext?.UserId))
            {
                return false;
            }

            /// if check does not specify security principal use the current user
            if (check.SecurityPrincipalId == null)
            {
                check.SecurityPrincipalId = CurrentAuthContext.UserId;
            }

            // An API access token is issued scoped to specific role assignments, and being authenticated as a
            // principal is not authority to act as every role that principal holds. The scope arrives as claims on
            // the request, but only the check itself crosses to the access control store, so it has to be carried
            // there or the token is evaluated against the principal's full role set.
            //
            // This only applies when the check is about the principal the caller authenticated as: an explicit
            // security principal id asks whether some other principal has access, which the caller's own token
            // scope does not narrow.
            if (check.SecurityPrincipalId == CurrentAuthContext.UserId
                && !(check.ScopedAssignedRoles?.Count > 0)
                && CurrentAuthContext.ScopedAssignedRoles?.Count > 0)
            {
                check.ScopedAssignedRoles = CurrentAuthContext.ScopedAssignedRoles;
            }

            return await internalApiClient.CheckSecurityPrincipalHasAccess(check, CurrentAuthContext);
        }

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
        /// The Domain Match rules restricting a security principal for a resource action, taken from the domain-typed
        /// IncludedResources on the roles authorizing that action.
        /// An empty list means the principal is unrestricted; null means the scope could not be evaluated and the
        /// caller should fail closed rather than treat the principal as unrestricted.
        /// </summary>
        internal async Task<List<string>?> GetDomainRestrictionRulesForPrincipal(
            ICertifyInternalApiClient internalApiClient,
            string? securityPrincipalId,
            string resourceActionId,
            ICollection<string>? scopedAssignedRoles = null)
        {
            if (string.IsNullOrWhiteSpace(securityPrincipalId))
            {
                return null;
            }

            var check = new AccessCheck
            {
                SecurityPrincipalId = securityPrincipalId,
                ResourceType = ResourceTypes.Domain,
                ResourceActionId = resourceActionId
            };

            if (scopedAssignedRoles?.Count > 0)
            {
                check.ScopedAssignedRoles = scopedAssignedRoles.ToList();
            }

            try
            {
                var scope = await internalApiClient.EvaluateAccessScope(check, SystemAuthContext) ?? new ResourceAccessScope();
                return ResourceAccess.GetDomainRestrictionRules(scope.AuthorizingRoles);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Check the current security principal's domain restrictions for a resource action. Restrictions are
        /// Domain Match rules held as domain-typed IncludedResources on the roles authorizing that action, so a
        /// principal whose authorizing roles carry no domain resources is unrestricted. Every supplied identifier
        /// must be permitted, as the caller gains access to all of them.
        /// </summary>
        internal async Task<Certify.Models.Config.ActionResult> CheckIdentifiersAuthorized(
            ICertifyInternalApiClient internalApiClient,
            string resourceActionId,
            IEnumerable<string?>? identifiers)
        {
            // the caller may have authorized by bearer token or by API access token, and an access token request
            // reaching an [AllowAnonymous] endpoint has no authenticated HttpContext.User to read the principal from
            var authContext = RequestAuthContext;

            if (string.IsNullOrWhiteSpace(authContext?.UserId))
            {
                return new Certify.Models.Config.ActionResult("No authenticated security principal to check domain restrictions for", false);
            }

            // resolve the rule set once, then evaluate each identifier locally against the shared Domain Match rules
            var domainRules = await GetDomainRestrictionRulesForPrincipal(
                internalApiClient,
                authContext.UserId,
                resourceActionId,
                authContext.ScopedAssignedRoles);

            if (domainRules == null)
            {
                // fail closed, a transient evaluation error must not promote a restricted principal to unrestricted
                return new Certify.Models.Config.ActionResult("Could not evaluate access scope for domain restrictions", false);
            }

            if (domainRules.Count == 0)
            {
                return new Certify.Models.Config.ActionResult("No domain restrictions apply", true);
            }

            var identifierList = identifiers?
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            if (identifierList.Count == 0)
            {
                return new Certify.Models.Config.ActionResult("No identifiers to check against the domain restrictions on this role assignment", false);
            }

            var denied = identifierList.FirstOrDefault(i => !ResourceAccess.IsIdentifierPermittedByDomainRules(domainRules, i));

            return denied == null
                ? new Certify.Models.Config.ActionResult("Authorized for all identifiers", true)
                : new Certify.Models.Config.ActionResult($"Identifier '{denied}' is not permitted by the domain restrictions on this role assignment", false);
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
                var principal = HttpContext?.User;
                if (principal?.Identity?.IsAuthenticated == true)
                {
                    var userIdFromClaims = principal.FindFirst(ClaimTypes.Sid)?.Value;
                    if (!string.IsNullOrWhiteSpace(userIdFromClaims))
                    {
                        var authContext = new AuthContext { UserId = userIdFromClaims };

                        var scopedAssignedRoles = principal
                            .FindAll(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType)
                            .Select(c => c.Value)
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Distinct()
                            .ToList();

                        if (scopedAssignedRoles.Any())
                        {
                            authContext.ScopedAssignedRoles = scopedAssignedRoles;
                        }

                        var authHeaderValue = Request.Headers["Authorization"];
                        if (!string.IsNullOrWhiteSpace(authHeaderValue) && authHeaderValue.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            authContext.Token = AuthenticationHeaderValue.Parse(authHeaderValue!).Parameter;
                        }

                        return authContext;
                    }
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
