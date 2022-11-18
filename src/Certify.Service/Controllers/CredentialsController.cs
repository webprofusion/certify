using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using Certify.Management;
using Certify.Models.Config;

namespace Certify.Service
{
    [RoutePrefix("api/credentials")]
    public class CredentialsController : Controllers.ControllerBase
    {
        private ICertifyManager _certifyManager;
        private ICredentialsManager _credentialsManager;

        public CredentialsController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;

            _credentialsManager = _certifyManager.GetCredentialsManager();
        }

        [HttpGet, Route("")]
        public async Task<List<StoredCredential>> GetCredentials()
        {
            return await _credentialsManager.GetCredentials();
        }

        [HttpPost, Route("")]
        public async Task<StoredCredential> UpdateCredentials(StoredCredential credential)
        {
            DebugLog();

            return await _credentialsManager.Update(credential);
        }

        [HttpDelete, Route("{storageKey}")]
        public async Task<bool> DeleteCredential(string storageKey)
        {
            DebugLog();

            return await _credentialsManager.Delete(_certifyManager.GetManagedItemStore(), storageKey);
        }

        [HttpPost, Route("{storageKey}/test")]
        public async Task<Models.Config.ActionResult> TestCredentials(string storageKey)
        {
            DebugLog();

            return await _credentialsManager.TestCredentials(storageKey);
        }
    }
}
