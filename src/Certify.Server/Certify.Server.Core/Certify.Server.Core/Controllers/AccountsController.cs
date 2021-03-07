using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service
{
    [ApiController]
    [Route("api/accounts")]
    public class AccountsController : Controllers.ControllerBase
    {
        private ICertifyManager _certifyManager = null;

        public AccountsController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("")]
        public async Task<List<AccountDetails>> GetAccounts()
        {
            return await _certifyManager.GetAccountRegistrations();
        }

        [HttpPost, Route("")]
        public async Task<Models.Config.ActionResult> AddAccount(ContactRegistration registration)
        {
            DebugLog();

            return await _certifyManager.AddAccount(registration);
        }

        [HttpDelete, Route("remove/{storageKey}")]
        public async Task<Models.Config.ActionResult> RemoveAccount(string storageKey)
        {
            DebugLog();
            return await _certifyManager.RemoveAccount(storageKey);
        }

        [HttpGet, Route("authorities")]
        public async Task<List<CertificateAuthority>> GetCertificateAuthorities()
        {
            return await _certifyManager.GetCertificateAuthorities();
        }
    }
}
