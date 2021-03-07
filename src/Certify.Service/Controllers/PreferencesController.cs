using System.Web.Http;
using Certify.Models;

namespace Certify.Service
{
    [RoutePrefix("api/preferences")]
    public class PreferencesController : Controllers.ControllerBase
    {
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

            return updated;
        }
    }
}