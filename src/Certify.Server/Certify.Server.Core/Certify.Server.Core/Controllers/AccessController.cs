using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Models.Config.AccessControl;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/access")]
    public class AccessController : ControllerBase
    {
        private IAccessControl _accessControl;

        private string GetContextUserId()
        {
            // TODO: sign passed value provided by public API using public APIs access token
            var contextUserId = Request.Headers["X-Context-User-Id"];
            return contextUserId;
        }

        public AccessController(IAccessControl manager)
        {
            _accessControl = manager;
        }

        [HttpGet, Route("securityprinciples")]
        public async Task<List<SecurityPrinciple>> GetSecurityPrinciples()
        {

            return await _accessControl.GetSecurityPrinciples(GetContextUserId());
        }

        [HttpPost, Route("updatepassword")]
        public async Task<bool> UpdatePassword(string id, string oldpassword, string newpassword)
        {
            return await _accessControl.UpdateSecurityPrinciplePassword(GetContextUserId(), id: id, oldpassword: oldpassword, newpassword: newpassword);
        }
    }
}
