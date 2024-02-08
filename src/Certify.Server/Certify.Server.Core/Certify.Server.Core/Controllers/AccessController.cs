using Certify.Core.Management.Access;
using Certify.Management;
using Certify.Models.API;
using Certify.Models.Config.AccessControl;
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

        private string GetContextUserId()
        {
            // TODO: sign passed value provided by public API using public APIs access token
            var contextUserId = Request.Headers["X-Context-User-Id"];

            return contextUserId;
        }

        [HttpPost, Route("securityprinciple")]
        public async Task<Models.Config.ActionResult> AddSecurityPrinciple([FromBody] SecurityPrinciple principle)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var addResultOk = await accessControl.AddSecurityPrinciple(GetContextUserId(), principle);

            return new Models.Config.ActionResult
            {
                IsSuccess = addResultOk,
                Message = addResultOk ? "Added" : "Failed to add"
            };
        }

        [HttpPost, Route("securityprinciple/update")]
        public async Task<Models.Config.ActionResult> UpdateSecurityPrinciple([FromBody] SecurityPrinciple principle)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var addResultOk = await accessControl.UpdateSecurityPrinciple(GetContextUserId(), principle);

            return new Models.Config.ActionResult
            {
                IsSuccess = addResultOk,
                Message = addResultOk ? "Updated" : "Failed to update"
            };
        }

        [HttpPost, Route("securityprinciple/roles/update")]
        public async Task<Models.Config.ActionResult> UpdateSecurityPrincipleAssignedRoles([FromBody] SecurityPrincipleAssignedRoleUpdate update)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var resultOk = await accessControl.UpdateAssignedRoles(GetContextUserId(), update);

            return new Models.Config.ActionResult
            {
                IsSuccess = resultOk,
                Message = resultOk ? "Updated" : "Failed to update"
            };
        }

        [HttpDelete, Route("securityprinciple/{id}")]
        public async Task<Models.Config.ActionResult> DeleteSecurityPrinciple(string id)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var resultOk = await accessControl.DeleteSecurityPrinciple(GetContextUserId(), id);

            return new Models.Config.ActionResult
            {
                IsSuccess = resultOk,
                Message = resultOk ? "Deleted" : "Failed to delete security principle"
            };
        }

        [HttpGet, Route("securityprinciples")]
        public async Task<List<SecurityPrinciple>> GetSecurityPrinciples()
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            var results = await accessControl.GetSecurityPrinciples(GetContextUserId());

            foreach (var r in results)
            {
                r.AuthKey = "<sanitized>";
                r.Password = "<sanitized>";
            }

            return results;
        }

        [HttpGet, Route("roles")]
        public async Task<List<Role>> GetRoles()
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var roles = await accessControl.GetSystemRoles();
            return roles;
        }

        [HttpGet, Route("securityprinciple/{id}/assignedroles")]
        public async Task<List<AssignedRole>> GetSecurityPrincipleAssignedRoles(string id)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            var results = await accessControl.GetAssignedRoles(GetContextUserId(), id);

            return results;
        }

        [HttpPost, Route("updatepassword")]
        public async Task<Models.Config.ActionResult> UpdatePassword([FromBody] SecurityPrinciplePasswordUpdate passwordUpdate)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var result = await accessControl.UpdateSecurityPrinciplePassword(GetContextUserId(), passwordUpdate);

            return new Models.Config.ActionResult
            {
                IsSuccess = result,
                Message = result ? "Updated" : "Failed to update"
            };
        }

        [HttpPost, Route("validate")]
        public async Task<SecurityPrincipleCheckResponse> Validate([FromBody] SecurityPrinciplePasswordCheck passwordCheck)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            var result =  await accessControl.CheckSecurityPrinciplePassword(GetContextUserId(), passwordCheck);

            return result;
        }

        [HttpPost, Route("serviceauth")]
        public async Task<Models.Config.ActionResult> ValidateServiceAuth()
        {
            var protector = _dataProtectionProvider.CreateProtector("serviceauth");

            protector.Unprotect(Request.Headers["X-Service-Auth"]);

            return new Models.Config.ActionResult();
        }
    }
}
