using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config.AccessControl;

namespace Certify.Core.Management.Access
{
    public interface IAccessControl
    {
        Task<bool> AddResourcePolicy(string contextUserId, ResourcePolicy resourceProfile, bool bypassIntegrityCheck = false);
        Task<bool> AddSecurityPrinciple(string contextUserId, SecurityPrinciple principle, bool bypassIntegrityCheck = false);
        Task<bool> DeleteSecurityPrinciple(string contextUserId, string id, bool allowSelfDelete = false);
        Task<List<ResourcePolicy>> GetSecurityPrincipleResourcePolicies(string contextUserId, string userId);
        Task<List<SecurityPrinciple>> GetSecurityPrinciples(string contextUserId);
        
        /// <summary>
        /// Get the list of standard roles built-in to the system
        /// </summary>
        /// <returns></returns>
        Task<List<Role>> GetSystemRoles();
        Task<bool> IsAuthorised(string contextUserId, string principleId, string roleId, string resourceType, string actionId, string identifier);
        Task<bool> IsPrincipleInRole(string contextUserId, string id, string roleId);
        Task<bool> UpdateSecurityPrinciple(string contextUserId, SecurityPrinciple principle);
        Task<bool> UpdateSecurityPrinciplePassword(string contextUserId, string id, string oldpassword, string newpassword);
    }
}
