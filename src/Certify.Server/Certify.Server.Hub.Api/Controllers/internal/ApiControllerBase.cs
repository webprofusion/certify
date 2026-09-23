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
        /// The Domain Match rules restricting a security principal for a resource action, taken from the domain-typed
        /// IncludedResources on the roles authorizing that action.
        /// An empty list means the principal is unrestricted; null means the scope could not be evaluated and the
        /// caller should fail closed rather than treat the principal as unrestricted.
        /// </summary>
        internal Task<List<string>?> GetDomainRestrictionRulesForPrincipal(
            ICertifyInternalApiClient internalApiClient,
            string? securityPrincipalId,
            string resourceActionId,
            ICollection<string>? scopedAssignedRoles = null)
            => PrincipalAccess.GetDomainRestrictionRules(internalApiClient, securityPrincipalId, resourceActionId, scopedAssignedRoles);

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
        /// Check that a managed item is within the caller's scope for a managed item action: the tag scopes and
        /// domain restrictions on their role assignments, applied to the item as the item listing applies them. The
        /// endpoint checks the action itself first; this narrows it to the one item, which the caller may have named
        /// by an id the listing never showed them.
        ///
        /// An item outside that scope is reported as not found, as the listing does not show it either.
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="mgmtAPI"></param>
        /// <param name="resourceActionId">the managed item action the endpoint performs</param>
        /// <param name="instanceId">instance holding the item</param>
        /// <param name="managedCertId">the item</param>
        /// <param name="item">the item where the endpoint has already fetched it, otherwise it is looked up</param>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckManagedItemInScope(
            ICertifyInternalApiClient internalApiClient,
            ManagementAPI mgmtAPI,
            string resourceActionId,
            string? instanceId,
            string? managedCertId,
            ManagedCertificate? item = null)
        {
            var scope = await ManagedItemVisibility.Resolve(internalApiClient, CurrentAuthContext, resourceActionId);

            if (!scope.HasAction)
            {
                return ManagedItemScopeNotEvaluated(resourceActionId);
            }

            if (scope.IsUnrestricted)
            {
                return null;
            }

            item ??= await FindManagedItem(mgmtAPI, instanceId, managedCertId);

            return item != null && await IsManagedItemInScope(internalApiClient, scope, item)
                ? null
                : Problem(detail: "Managed item not found", statusCode: StatusCodes.Status404NotFound);
        }

        /// <summary>
        /// Check a managed item configuration the caller has submitted, to save, test or preview, against their
        /// scope for a managed item action. Where it is an existing item, that item must be within scope as it
        /// stands, and is otherwise reported as not found. Every identifier the submitted configuration names must
        /// be within the domain restrictions for the action, so it can neither reach an item the caller could not
        /// otherwise nor take one outside their domains. The submitted configuration's tags are not considered, as
        /// tags are held by the hub and a new item is only tagged once it has been saved.
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="mgmtAPI"></param>
        /// <param name="resourceActionId">the managed item action the endpoint performs</param>
        /// <param name="instanceId">instance the configuration is for</param>
        /// <param name="submittedItem">the configuration as the caller submitted it</param>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckSubmittedManagedItemInScope(
            ICertifyInternalApiClient internalApiClient,
            ManagementAPI mgmtAPI,
            string resourceActionId,
            string? instanceId,
            ManagedCertificate? submittedItem)
        {
            var scope = await ManagedItemVisibility.Resolve(internalApiClient, CurrentAuthContext, resourceActionId);

            if (!scope.HasAction)
            {
                return ManagedItemScopeNotEvaluated(resourceActionId);
            }

            if (scope.IsUnrestricted)
            {
                return null;
            }

            var existingItem = await FindManagedItem(mgmtAPI, instanceId, submittedItem?.Id);

            if (existingItem != null && !await IsManagedItemInScope(internalApiClient, scope, existingItem))
            {
                return Problem(detail: "Managed item not found", statusCode: StatusCodes.Status404NotFound);
            }

            if (!scope.PermitsIdentifiers(submittedItem?.GetCertificateIdentifiers().Select(i => i.Value)))
            {
                return Problem(
                    detail: "The managed item's identifiers are not all permitted by the domain restrictions on this role assignment",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            return null;
        }

        private ObjectResult ManagedItemScopeNotEvaluated(string resourceActionId)
            => Problem(detail: $"Could not evaluate the managed item access scope for {resourceActionId}", statusCode: StatusCodes.Status401Unauthorized);

        /// <summary>
        /// The hub's cached copy of an item is what the item listing shows, so that is what is checked; an item not
        /// cached yet is asked for from its instance.
        /// </summary>
        private async Task<ManagedCertificate?> FindManagedItem(ManagementAPI mgmtAPI, string? instanceId, string? managedCertId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(managedCertId))
            {
                return null;
            }

            return mgmtAPI.GetCachedManagedCertificate(instanceId, managedCertId)
                ?? await mgmtAPI.GetManagedCertificate(instanceId, managedCertId, CurrentAuthContext);
        }

        private async Task<bool> IsManagedItemInScope(ICertifyInternalApiClient internalApiClient, ManagedItemVisibility scope, ManagedCertificate item)
        {
            // an item whose tags cannot be found is untagged, which no tag scoped caller reaches
            var tags = scope.RequiresTags && !string.IsNullOrWhiteSpace(item.Id)
                ? await internalApiClient.GetHubItemTags(TaggedItemTypes.ManagedCertificate, item.Id, SystemAuthContext)
                : null;

            return scope.Permits(tags, item.GetCertificateIdentifiers().Select(i => i.Value));
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

        /// <summary>
        /// The tag scopes restricting what the caller may see, or null when they are unrestricted.
        ///
        /// A role assignment may be scoped to tags, which limits the resources that assignment can reach. An API
        /// access token scoped to specific assignments considers only those; any other caller considers every
        /// assignment they hold.
        ///
        /// There were two copies of this and they did not agree. One returned null - meaning no filtering at all -
        /// whenever the caller had no token scope, so a signed in operator whose role was tag scoped saw every
        /// resource while an API token scoped to that same role saw only the matching ones.
        /// </summary>
        internal Task<List<TagScope>?> GetCallerTagScopes(ICertifyInternalApiClient internalApiClient)
            => PrincipalAccess.GetTagScopes(internalApiClient, CurrentAuthContext);

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
