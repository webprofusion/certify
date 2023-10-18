using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Management;
using Certify.Models.Config.AccessControl;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/access")]
    public class AccessController : ControllerBase
    {
        private ICertifyManager _certifyManager;

        private string GetContextUserId()
        {
            // TODO: sign passed value provided by public API using public APIs access token
            var contextUserId = Request.Headers["X-Context-User-Id"];

#if DEBUG
            if (string.IsNullOrEmpty(contextUserId))
            {
                // TODO: our context user has to at least come from a valid JWT claim
                contextUserId = "admin_01";
            }
#endif
            return contextUserId;
        }

        public AccessController(ICertifyManager certifyManager)
        {
            _certifyManager = certifyManager;
        }

#if DEBUG
        private async Task BootstrapTestAdminUserAndRoles(IAccessControl access)
        {

            var adminSp = new SecurityPrinciple
            {
                Id = "admin_01",
                Email = "admin@test.com",
                Description = "Primary test admin",
                PrincipleType = SecurityPrincipleType.User,
                Username = "admin",
                Provider = StandardProviders.INTERNAL
            };

            await access.AddSecurityPrinciple(adminSp.Id, adminSp, bypassIntegrityCheck: true);

            var actions = Policies.GetStandardResourceActions();

            foreach (var action in actions)
            {
                await access.AddAction(action);
            }

            // setup policies with actions

            var policies = Policies.GetStandardPolicies();

            // add policies to store
            foreach (var r in policies)
            {
                _ = await access.AddResourcePolicy(adminSp.Id, r, bypassIntegrityCheck: true);
            }

            // setup roles with policies
            var roles = await access.GetSystemRoles();

            foreach (var r in roles)
            {
                // add roles and policy assignments to store
                await access.AddRole(r);
            }

            // assign security principles to roles
            var assignedRoles = new List<AssignedRole> {
                 // administrator
                 new AssignedRole{
                     Id= Guid.NewGuid().ToString(),
                     RoleId=StandardRoles.Administrator.Id,
                     SecurityPrincipleId=adminSp.Id
                 }
            };

            foreach (var r in assignedRoles)
            {
                // add roles and policy assignments to store
                await access.AddAssignedRole(r);
            }
        }

#endif

        [HttpGet, Route("securityprinciples")]
        public async Task<List<SecurityPrinciple>> GetSecurityPrinciples()
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();

            var results = await accessControl.GetSecurityPrinciples(GetContextUserId());

#if DEBUG
            // bootstrap the default user
            if (!results.Any())
            {
                await BootstrapTestAdminUserAndRoles(accessControl);
                results = await accessControl.GetSecurityPrinciples(GetContextUserId());
            }
#endif
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
        public async Task<bool> UpdatePassword(string id, string oldpassword, string newpassword)
        {
            var accessControl = await _certifyManager.GetCurrentAccessControl();
            return await accessControl.UpdateSecurityPrinciplePassword(GetContextUserId(), id: id, oldpassword: oldpassword, newpassword: newpassword);
        }
    }
}
