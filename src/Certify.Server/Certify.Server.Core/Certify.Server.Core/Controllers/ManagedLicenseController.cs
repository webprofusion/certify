using Certify.Management;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/managedlicense")]
    public class ManagedLicenseController : ControllerBase
    {
        private ICertifyManager _certifyManager;

        public ManagedLicenseController(ICertifyManager certifyManager)
        {
            if (certifyManager == null)
            {
                throw new ArgumentNullException(nameof(certifyManager), "CertifyManager cannot be null");
            }

            _certifyManager = certifyManager;
        }

        [HttpDelete, Route("delete/{id}")]
        public async Task<Models.Config.ActionResult> RemoveManagedLicense(string id)
        {
            return await _certifyManager.RemoveManagedLicenses(id);
        }

        [HttpGet, Route("list")]
        public async Task<ICollection<ManagedLicense>> GetManagedLicenses()
        {
            return await _certifyManager.GetManagedLicenses();
        }

        [HttpPost, Route("add")]
        public async Task<Models.Config.ActionResult> AddManagedLicense(ManagedLicense item)
        {
            return await _certifyManager.AddManagedLicense(item);
        }

        [HttpPost, Route("update")]
        public async Task<Models.Config.ActionResult> UpdateManagedLicense(ManagedLicense item)
        {
            return await _certifyManager.UpdateManagedLicense(item);
        }
    }
}
