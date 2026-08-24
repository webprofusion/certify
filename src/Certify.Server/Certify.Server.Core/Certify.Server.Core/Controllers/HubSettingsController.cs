using Certify.Management;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/hubsettings")]
    public class HubSettingsController : ControllerBase
    {
        private ICertifyManager _certifyManager;

        public HubSettingsController(ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("")]
        public async Task<HubSettings> Get()
        {
            DebugLog();

            return await _certifyManager.GetHubSettings();
        }

        [HttpPost, Route("")]
        public async Task<Models.Config.ActionResult> Update(HubSettings settings)
        {
            DebugLog();

            return await _certifyManager.UpdateHubSettings(settings);
        }
    }
}
