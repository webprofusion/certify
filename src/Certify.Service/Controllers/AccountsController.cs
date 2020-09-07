using System;
using System.Collections.Generic;
using System.Linq;
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

        [HttpDelete, Route("remove/{storageKey}")]
        public async Task<ActionResult> RemoveAccount(string storageKey)
        {
            DebugLog();
            return await _certifyManager.RemoveAccount(storageKey);
        }

        [HttpGet, Route("authorities")]
        public async Task<List<CertificateAuthority>> GetCertificateAuthorities()
        {
            return await _certifyManager.GetCertificateAuthorities();
        }

        [HttpPost, Route("authorities")]
        public async Task<ActionResult> UpdateCertificateAuthority(CertificateAuthority certificateAuthority)
        {
            DebugLog();
            return await _certifyManager.UpdateCertificateAuthority(certificateAuthority);
        }

        [HttpDelete, Route("authorities/{id}")]
        public async Task<ActionResult> RemoveCertificateAuthority(string id)
        {
            DebugLog();
            return await _certifyManager.RemoveCertificateAuthority(id);
        }
    }
}
