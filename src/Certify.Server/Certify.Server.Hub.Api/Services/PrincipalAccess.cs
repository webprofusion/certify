using System.Security.Claims;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Access checks for an authenticated caller, shared by the hub API controllers and the UI status hub so that a
    /// caller is evaluated the same way whether they are making a request or receiving realtime updates.
    /// </summary>
    public static class PrincipalAccess
    {
        /// <summary>
        /// Special auth context used internally for operations where the requesting user may not be authorized to query system state.
        /// A new instance each time, as an auth context is mutable.
        /// </summary>
        public static AuthContext SystemAuthContext => new AuthContext { UserId = StandardSecurityPrincipals.System };

        /// <summary>
        /// The caller identified by an authenticated principal: the security principal id, and the role assignments
        /// an API access token narrows it to. Null when there is no authenticated principal.
        /// </summary>
        public static AuthContext? GetAuthContext(ClaimsPrincipal? principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var userId = principal.FindFirst(ClaimTypes.Sid)?.Value;

            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            var authContext = new AuthContext { UserId = userId };

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

            return authContext;
        }

        /// <summary>
        /// Check resource action access for the caller
        /// </summary>
        public static async Task<bool> IsAuthorized(ICertifyInternalApiClient internalApiClient, AuthContext? authContext, AccessCheck check)
        {
            if (string.IsNullOrWhiteSpace(authContext?.UserId))
            {
                return false;
            }

            // if check does not specify security principal use the caller
            if (check.SecurityPrincipalId == null)
            {
                check.SecurityPrincipalId = authContext.UserId;
            }

            // An API access token is issued scoped to specific role assignments, and being authenticated as a
            // principal is not authority to act as every role that principal holds. The scope arrives as claims on
            // the request, but only the check itself crosses to the access control store, so it has to be carried
            // there or the token is evaluated against the principal's full role set.
            //
            // This only applies when the check is about the principal the caller authenticated as: an explicit
            // security principal id asks whether some other principal has access, which the caller's own token
            // scope does not narrow.
            if (check.SecurityPrincipalId == authContext.UserId
                && !(check.ScopedAssignedRoles?.Count > 0)
                && authContext.ScopedAssignedRoles?.Count > 0)
            {
                check.ScopedAssignedRoles = authContext.ScopedAssignedRoles;
            }

            return await internalApiClient.CheckSecurityPrincipalHasAccess(check, authContext);
        }

        /// <summary>
        /// The Domain Match rules restricting a security principal for a resource action, taken from the domain-typed
        /// IncludedResources on the roles authorizing that action.
        /// An empty list means the principal is unrestricted; null means the scope could not be evaluated and the
        /// caller should fail closed rather than treat the principal as unrestricted.
        /// </summary>
        public static async Task<List<string>?> GetDomainRestrictionRules(
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
        /// The tag scopes restricting what the caller may see, or null when they are unrestricted.
        ///
        /// A role assignment may be scoped to tags, which limits the resources that assignment can reach. An API
        /// access token scoped to specific assignments considers only those; any other caller considers every
        /// assignment they hold.
        /// </summary>
        public static async Task<List<TagScope>?> GetTagScopes(ICertifyInternalApiClient internalApiClient, AuthContext? authContext)
        {
            if (string.IsNullOrWhiteSpace(authContext?.UserId))
            {
                return null;
            }

            try
            {
                var assignedRoles = await internalApiClient.GetSecurityPrincipalAssignedRoles(authContext.UserId, authContext);

                if (assignedRoles?.Any() != true)
                {
                    return null;
                }

                // Getting the assignment id comparison wrong here removes the tag filtering rather than tightening
                // it, so this shares the matching used by the authorization check itself.
                var applicableRoles = ResourceAccess.FilterToScopedAssignments(assignedRoles, authContext.ScopedAssignedRoles);

                var tagScopes = applicableRoles
                    .Where(r => r.ScopedTags?.Count > 0)
                    .SelectMany(r => r.ScopedTags!)
                    .ToList();

                // no tag restrictions on any applicable assignment, so the caller is unrestricted
                return tagScopes.Count > 0 ? tagScopes : null;
            }
            catch (Exception)
            {
                // Fail closed on an empty scope set rather than null: null means unrestricted, so reporting it here
                // would widen what an unreadable role assignment can see instead of narrowing it.
                return [];
            }
        }

        /// <summary>
        /// True when the caller holds the Administrator role, considering only the role assignments an API access
        /// token is scoped to where it is scoped. False when the assignments cannot be read.
        /// </summary>
        public static async Task<bool> IsAdministrator(ICertifyInternalApiClient internalApiClient, AuthContext? authContext)
        {
            if (string.IsNullOrWhiteSpace(authContext?.UserId))
            {
                return false;
            }

            try
            {
                var assignedRoles = await internalApiClient.GetSecurityPrincipalAssignedRoles(authContext.UserId, authContext);

                return ResourceAccess.FilterToScopedAssignments(assignedRoles, authContext.ScopedAssignedRoles)
                    .Any(r => r.RoleId == StandardRoles.Administrator.Id);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
