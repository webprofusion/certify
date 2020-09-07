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
        private CredentialsManager credentialsManager = new CredentialsManager();

        [HttpGet, Route("")]
        public async Task<List<StoredCredential>> GetCredentials()
        {
            return await credentialsManager.GetCredentials();
        }

        [HttpPost, Route("")]
        public async Task<StoredCredential> UpdateCredentials(StoredCredential credential)
        {
            DebugLog();

            return await credentialsManager.Update(credential);
        }

        [HttpDelete, Route("{storageKey}")]
        public async Task<bool> DeleteCredential(string storageKey)
        {
            DebugLog();

            return await credentialsManager.Delete(storageKey);
        }

        [HttpPost, Route("{storageKey}/test")]
        public async Task<ActionResult> TestCredentials(string storageKey)
        {
            DebugLog();

            return await credentialsManager.TestCredentials(storageKey);
        }
    }
}
