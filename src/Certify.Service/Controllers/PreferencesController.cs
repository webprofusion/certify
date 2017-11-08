using Certify.Models;
using System.Collections.Generic;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/preferences")]
    public class PreferencesController : ApiController
    {
        [HttpGet, Route("")]
        public Preferences GetPreferences()
        {
            return Management.SettingsManager.ToPreferences();
        }

        [HttpPost, Route("")]
        public bool SetPreferences(Preferences preferences)
        {
            var updated = Management.SettingsManager.FromPreferences(preferences);
            Management.SettingsManager.SaveAppSettings();

            return updated;
        }
    }
}