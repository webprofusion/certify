using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Hub;

namespace Certify.Core.Management.Access
{
    public interface IAccessControl
    {
        Task<bool> AddResourcePolicy(string contextUserId, ResourcePolicy resourceProfile, bool bypassIntegrityCheck = false);
        Task<bool> AddSecurityPrinciple(string contextUserId, SecurityPrinciple principle, bool bypassIntegrityCheck = false);
        Task<bool> DeleteSecurityPrinciple(string contextUserId, string id, bool allowSelfDelete = false);
        Task<List<SecurityPrinciple>> GetSecurityPrinciples(string contextUserId);
        Task<SecurityPrinciple> GetSecurityPrinciple(string contextUserId, string id);

        /// <summary>
        /// Get the list of standard roles built-in to the system
        /// </summary>
        /// <returns></returns>
        Task<List<Role>> GetRoles();
        Task<bool> IsSecurityPrincipleAuthorised(string contextUserId, AccessCheck check);
        Task<Models.Config.ActionResult> IsAccessTokenAuthorised(string contextUserId, AccessToken accessToken, AccessCheck check);
        Task<bool> IsPrincipleInRole(string contextUserId, string id, string roleId);
        Task<List<AssignedRole>> GetAssignedRoles(string contextUserId, string id);
        Task<RoleStatus> GetSecurityPrincipleRoleStatus(string contextUserId, string id);
        Task<bool> UpdateSecurityPrinciple(string contextUserId, SecurityPrinciple principle);
        Task<bool> UpdateAssignedRoles(string contextUserId, SecurityPrincipleAssignedRoleUpdate update);
        Task<bool> UpdateSecurityPrinciplePassword(string contextUserId, SecurityPrinciplePasswordUpdate passwordUpdate);
        Task<SecurityPrincipleCheckResponse> CheckSecurityPrinciplePassword(string contextUserId, SecurityPrinciplePasswordCheck passwordCheck);

        Task AddRole(Role role);
        Task AddAssignedRole(AssignedRole assignedRole);
        Task AddResourceAction(ResourceAction action);
        Task<bool> IsInitialized();
    }
}
