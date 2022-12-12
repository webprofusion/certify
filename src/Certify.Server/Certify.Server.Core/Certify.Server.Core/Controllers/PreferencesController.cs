using Certify.Management;
using Certify.Models;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/preferences")]
    public class PreferencesController : ControllerBase
    {
        private ICertifyManager _certifyManager;

        public PreferencesController(ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("")]
        public Preferences GetPreferences()
        {
            DebugLog();

            return SettingsManager.ToPreferences();
        }

        [HttpPost, Route("")]
        public bool SetPreferences(Preferences preferences)
        {
            DebugLog();

            var updated = SettingsManager.FromPreferences(preferences);
            SettingsManager.SaveAppSettings();

            _certifyManager.ApplyPreferences();

            return updated;
        }
    }
}
