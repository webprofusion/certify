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
        Task<bool> IsSecurityPrincipalAuthorised(string contextUserId, AccessCheck check);
        Task<Models.Config.ActionResult> IsAccessTokenAuthorised(string contextUserId, AccessToken accessToken, AccessCheck check);
        Task<bool> IsPrincipalInRole(string contextUserId, string id, string roleId);
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

        Task<bool> DeleteAssignedAccessToken(string contextUserId, string id);
        Task<bool> IsInitialized();
    }
}
