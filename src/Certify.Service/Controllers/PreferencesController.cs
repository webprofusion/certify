using System.Web.Http;
using Certify.Management;
using Certify.Models;

namespace Certify.Service.Controllers
{
    [RoutePrefix("api/preferences")]
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
