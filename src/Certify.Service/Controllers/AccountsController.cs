using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.Service
{
    [RoutePrefix("api/accounts")]
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
        public async Task<ActionResult> AddAccount(ContactRegistration registration)
        {
            DebugLog();

            return await _certifyManager.AddAccount(registration);
        }

        [HttpDelete, Route("{storageKey}")]
        public async Task<bool> DeleteAccount(string storageKey)
        {
            DebugLog($"Deleting a account {storageKey}" );

            // not implemented
            return await Task.FromResult(false);
        }
    }
}
