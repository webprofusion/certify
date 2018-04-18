using Certify.Management;
using Certify.Models.Config;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/credentials")]
    public class CredentialsController : Controllers.ControllerBase
    {
        private CredentialsManager credentialsManager = new CredentialsManager();

        [HttpGet, Route("")]
        public async Task<List<StoredCredential>> GetCredentials()
        {
            return await credentialsManager.GetStoredCredentials();
        }

        [HttpPost, Route("")]
        public async Task<StoredCredential> UpdateCredentials(StoredCredential credential)
        {
            DebugLog();

            return await credentialsManager.UpdateCredential(credential);
        }

        [HttpDelete, Route("{storageKey}")]
        public async Task<bool> DeleteCredential(string storageKey)
        {
            DebugLog();

            return await credentialsManager.DeleteCredential(storageKey);
        }
    }
}