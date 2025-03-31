using Certify.Management;
using Certify.Models.Hub;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/managedinstance")]
    public class ManagedInstanceController : ControllerBase
    {
        private ICertifyManager _certifyManager;
        private IDataProtectionProvider _dataProtectionProvider;

        public ManagedInstanceController(ICertifyManager certifyManager)
        {
            _certifyManager = certifyManager;

        }

        /// <summary>
        /// Get a managed instance using it's hub assigned id
        /// </summary>
        /// <param name="id">Hub assigned instance id</param>
        /// <returns></returns>
        [HttpGet]
        public async Task<ManagedInstanceInfo> Get(string id)
        {
            return await _certifyManager.GetHubManagedInstance(id);

        }

        [HttpPost]
        public async Task<Certify.Models.Config.ActionResult<ManagedInstanceInfo>> Add(ManagedInstanceInfo item)
        {
            return await _certifyManager.AddHubManagedInstance(item);
        }

        [HttpPost]
        [Route("update")]
        public async Task<Certify.Models.Config.ActionResult> Update(ManagedInstanceInfo item)
        {
            return await _certifyManager.UpdateHubManagedInstance(item);
        }

        [HttpGet]
        public async Task<ICollection<ManagedInstanceInfo>> List()
        {
            return await _certifyManager.GetHubManagedInstances();
        }

        [HttpDelete]
        public async Task<Certify.Models.Config.ActionResult> Remove(string id)
        {
            return await _certifyManager.RemoveHubManagedInstance(id);
        }
    }
}
