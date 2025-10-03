using Certify.Management;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/oidcprovider")]
    public class OidcProviderController : ControllerBase
    {
        private ICertifyManager _certifyManager;

        public OidcProviderController(ICertifyManager certifyManager)
        {
            if (certifyManager == null)
            {
                throw new ArgumentNullException(nameof(certifyManager), "CertifyManager cannot be null");
            }

            _certifyManager = certifyManager;
        }

        [HttpDelete, Route("remove/{id}")]
        public async Task<Models.Config.ActionResult> RemovOidcProvider(string id)
        {
            return await _certifyManager.RemovOidcProvider(id);
        }

        [HttpGet, Route("list")]
        public async Task<ICollection<OidcProviderConfig>> GetOidcProviders()
        {
            return await _certifyManager.GetOidcProviders(includeSecret: false);
        }

        [HttpGet, Route("{id}/withsecret")]
        public async Task<OidcProviderConfig> GetOidcProvidersWithSecret(string id)
        {
            var results = await _certifyManager.GetOidcProviders(includeSecret: true);
            return results.FirstOrDefault(p => p.Id == id);
        }

        [HttpPost, Route("add")]
        public async Task<Models.Config.ActionResult> AddOidcProvider(OidcProviderConfig item)
        {
            return await _certifyManager.AddOidcProvider(item);
        }

        [HttpPost, Route("update")]
        public async Task<Models.Config.ActionResult> UpdateOidcProvider(OidcProviderConfig item)
        {
            return await _certifyManager.UpdateOidcProvider(item);
        }
    }
}
