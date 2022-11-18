using System.Web.Http;
using Certify.Management;
using Certify.Models;

namespace Certify.Service
{
    [RoutePrefix("api/preferences")]
    public class PreferencesController : Controllers.ControllerBase
    {
        private ICertifyManager _certifyManager;

        public PreferencesController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("")]
        public Preferences GetPreferences()
        {
            DebugLog();

            return Management.SettingsManager.ToPreferences();
        }

        [HttpPost, Route("")]
        public bool SetPreferences(Preferences preferences)
        {
            DebugLog();

            var updated = Management.SettingsManager.FromPreferences(preferences);
            Management.SettingsManager.SaveAppSettings();

            _certifyManager.ApplyPreferences();

            return updated;
        }
    }
}
