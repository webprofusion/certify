using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Hub;

namespace Certify.Core.Management.Access
{
    public interface IAccessControl
    {
        Task<bool> AddResourcePolicy(string contextUserId, ResourcePolicy resourceProfile, bool bypassIntegrityCheck = false);
        Task<bool> AddSecurityPrincipal(string contextUserId, SecurityPrincipal principal, bool bypassIntegrityCheck = false);
        Task<bool> DeleteSecurityPrincipal(string contextUserId, string id, bool allowSelfDelete = false);
        Task<List<SecurityPrincipal>> GetSecurityPrincipals(string contextUserId, bool includePassword = false);
        Task<SecurityPrincipal> GetSecurityPrincipal(string contextUserId, string id, bool includePassword = false);

        /// <summary>
        /// Get the list of standard roles built-in to the system
        /// </summary>
        /// <returns></returns>
        Task<List<Role>> GetRoles(string contextUserId);

        /// <summary>
        /// Get the list of stored resource policies
        /// </summary>
        /// <returns></returns>
        Task<List<ResourcePolicy>> GetResourcePolicies(string contextUserId);

        /// <summary>
        /// Get the list of stored resource actions
        /// </summary>
        /// <returns></returns>
        Task<List<ResourceAction>> GetResourceActions(string contextUserId);

        // Two kinds of method live on this interface, and the signature says which.
        //
        // A method taking a contextUserId gates on it: that principal must be an administrator, the system, or the
        // subject itself, or the call is refused and audited. Everything below which does not take one is a pure
        // evaluator - it answers a question about the principal named in its arguments and has no opinion on who is
        // asking, which is the API layer's job. They used to take a contextUserId as well and ignore it, so a caller
        // could not tell from the signature whether passing the wrong actor would deny the call or do nothing at all;
        // the mutation gates gave that away by passing the same id twice to IsPrincipalInRole.

        Task<bool> IsSecurityPrincipalAuthorised(AccessCheck check);

        /// <summary>
        /// Resolve an access token and confirm the principal it authenticates as may perform the given action.
        /// On success the result carries that principal, as <see cref="ResolveAccessToken"/> does.
        /// </summary>
        Task<Models.Config.ActionResult<AccessTokenAuthorizationContext>> IsAccessTokenAuthorised(AccessToken accessToken, AccessCheck check);

        /// <summary>
        /// Resolve an access token to the security principal and role scope it authenticates as, without checking
        /// whether that principal may perform any particular action.
        /// </summary>
        Task<Models.Config.ActionResult<AccessTokenAuthorizationContext>> ResolveAccessToken(AccessToken accessToken);

        /// <summary>
        /// Evaluate the access scope for a principal/action, including authorizing roles and whether
        /// access is unrestricted or constrained by tag scopes / included resources.
        /// </summary>
        Task<ResourceAccessScope> EvaluateAccessScope(AccessCheck check);

        /// <summary>
        /// True when a concrete resource (represented by its tags) is within the resolved access scope.
        /// </summary>
        bool IsResourceInScope(ResourceAccessScope scope, IEnumerable<TagSummary>? resourceTags);

        Task<bool> IsPrincipalInRole(string securityPrincipalId, string roleId);
        Task<List<AssignedRole>> GetAssignedRoles(string contextUserId, string id);
        Task<RoleStatus> GetSecurityPrincipalRoleStatus(string contextUserId, string id);
        Task<bool> UpdateSecurityPrincipal(string contextUserId, SecurityPrincipal principal);
        Task<bool> UpdateAssignedRoles(string contextUserId, SecurityPrincipalAssignedRoleUpdate update);
        Task<bool> UpdateSecurityPrincipalPassword(string contextUserId, SecurityPrincipalPasswordUpdate passwordUpdate, bool requirePasswordConfirmation = true);
        Task<SecurityPrincipalCheckResponse> CheckSecurityPrincipalPassword(string contextUserId, SecurityPrincipalPasswordCheck passwordCheck);

        Task<bool> AddRole(string contextUserId, Role role, bool bypassIntegrityCheck = false);
        Task<bool> AddAssignedRole(string contextUserId, AssignedRole assignedRole, bool bypassIntegrityCheck = false);
        Task<bool> AddResourceAction(string contextUserId, ResourceAction action, bool bypassIntegrityCheck = false);

        Task<List<AssignedAccessToken>> GetAssignedAccessTokens(string contextUserId);
        Task<bool> AddAssignedAccessToken(string contextUserId, AssignedAccessToken token);

        /// <summary>
        /// Update the title, description and role scope of an assigned access token without changing the token itself
        /// </summary>
        Task<Models.Config.ActionResult> UpdateAssignedAccessToken(string contextUserId, AssignedAccessToken token);

        Task<bool> DeleteAssignedAccessToken(string contextUserId, string id);
        Task<bool> IsInitialized();
    }
}
