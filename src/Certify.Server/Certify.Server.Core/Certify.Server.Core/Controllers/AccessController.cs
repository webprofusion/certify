using Certify.Management;
using Certify.Models.Hub;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/access")]
    public class AccessController : ControllerBase
    {
        private ICertifyManager _certifyManager;
        private IDataProtectionProvider _dataProtectionProvider;

        public AccessController(ICertifyManager certifyManager, IDataProtectionProvider dataProtectionProvider)
        {
            _certifyManager = certifyManager;
            _dataProtectionProvider = dataProtectionProvider;
        }

        [HttpPost, Route("securityprincipal")]
        public async Task<Models.Config.ActionResult> AddSecurityPrincipal([FromBody] SecurityPrincipal principal)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var addResultOk = await accessControl.AddSecurityPrincipal(GetContextUserId(), principal);

            return new Models.Config.ActionResult
            {
                IsSuccess = addResultOk,
                Message = addResultOk ? "Added" : "Failed to add"
            };
        }

        [HttpPost, Route("securityprincipal/update")]
        public async Task<Models.Config.ActionResult> UpdateSecurityPrincipal([FromBody] SecurityPrincipal principal)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var addResultOk = await accessControl.UpdateSecurityPrincipal(GetContextUserId(), principal);

            return new Models.Config.ActionResult
            {
                IsSuccess = addResultOk,
                Message = addResultOk ? "Updated" : "Failed to update"
            };
        }

        [HttpPost, Route("securityprincipal/roles/update")]
        public async Task<Models.Config.ActionResult> UpdateSecurityPrincipalAssignedRoles([FromBody] SecurityPrincipalAssignedRoleUpdate update)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var resultOk = await accessControl.UpdateAssignedRoles(GetContextUserId(), update);

            return new Models.Config.ActionResult
            {
                IsSuccess = resultOk,
                Message = resultOk ? "Updated" : "Failed to update"
            };
        }

        [HttpDelete, Route("securityprincipal/{id}")]
        public async Task<Models.Config.ActionResult> DeleteSecurityPrincipal(string id)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var resultOk = await accessControl.DeleteSecurityPrincipal(GetContextUserId(), id);

            return new Models.Config.ActionResult
            {
                IsSuccess = resultOk,
                Message = resultOk ? "Deleted" : "Failed to delete security principal"
            };
        }

        [HttpGet, Route("securityprincipals")]
        public async Task<ICollection<SecurityPrincipal>> GetSecurityPrincipals()
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            var results = await accessControl.GetSecurityPrincipals(GetContextUserId());

            foreach (var r in results)
            {
                r.AuthKey = "<sanitized>";
                r.Password = "<sanitized>";
            }

            return results;
        }

        [HttpGet, Route("roles")]
        public async Task<ICollection<Role>> GetRoles()
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            return await accessControl.GetRoles(GetContextUserId());
        }

        [HttpPost, Route("securityprincipal/allowedaction/")]
        public async Task<bool> CheckSecurityPrincipalHasAccess(AccessCheck check)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            return await accessControl.IsSecurityPrincipalAuthorised(GetContextUserId(), check);
        }

        [HttpPost, Route("apitoken/check/")]
        public async Task<Certify.Models.Config.ActionResult> CheckApiTokenHasAccess(AccessTokenCheck tokenCheck)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            return await accessControl.IsAccessTokenAuthorised(GetContextUserId(), tokenCheck.Token, tokenCheck.Check);
        }

        [HttpGet, Route("assignedtoken/list/")]
        public async Task<ICollection<AssignedAccessToken>> GetAssignedAccessTokens()
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            return await accessControl.GetAssignedAccessTokens(GetContextUserId());
        }

        [HttpPost, Route("assignedtoken/")]
        public async Task<Models.Config.ActionResult> AddAssignedccessToken([FromBody] AssignedAccessToken token)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            token.AccessTokens?.ForEach(a =>
            {
                a.DateCreated = DateTime.UtcNow;
                a.Id = Guid.NewGuid().ToString();
                a.Secret = Guid.NewGuid().ToString();
            });

            var addResultOk = await accessControl.AddAssignedAccessToken(GetContextUserId(), token);

            return new Models.Config.ActionResult
            {
                IsSuccess = addResultOk,
                Message = addResultOk ? "Added" : "Failed to add"
            };
        }

        [HttpDelete, Route("assignedtoken/{id}")]
        public async Task<Models.Config.ActionResult> RemoveAssignedAccessToken(string id)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var addResultOk = await accessControl.DeleteAssignedAccessToken(GetContextUserId(), id);

            return new Models.Config.ActionResult
            {
                IsSuccess = addResultOk,
                Message = addResultOk ? "Added" : "Failed to add"
            };
        }

        [HttpGet, Route("securityprincipal/{id}/assignedroles")]
        public async Task<ICollection<AssignedRole>> GetSecurityPrincipalAssignedRoles(string id)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            var results = await accessControl.GetAssignedRoles(GetContextUserId(), id);

            return results;
        }

        [HttpGet, Route("securityprincipal/{id}/rolestatus")]
        public async Task<RoleStatus> GetSecurityPrincipalRoleStatus(string id)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            var result = await accessControl.GetSecurityPrincipalRoleStatus(GetContextUserId(), id);

            return result;
        }

        [HttpPost, Route("updatepassword")]
        public async Task<Models.Config.ActionResult> UpdatePassword([FromBody] SecurityPrincipalPasswordUpdate passwordUpdate)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var result = await accessControl.UpdateSecurityPrincipalPassword(GetContextUserId(), passwordUpdate);

            return new Models.Config.ActionResult
            {
                IsSuccess = result,
                Message = result ? "Updated" : "Failed to update"
            };
        }

        [HttpPost, Route("validate")]
        public async Task<SecurityPrincipalCheckResponse> Validate([FromBody] SecurityPrincipalPasswordCheck passwordCheck)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var result = await accessControl.CheckSecurityPrincipalPassword(GetContextUserId(), passwordCheck);

            return result;
        }
    }
}
