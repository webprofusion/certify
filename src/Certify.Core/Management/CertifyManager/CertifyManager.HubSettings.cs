using System;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private HubSettings? _cachedHubSettings;

        /// <summary>
        /// Get the current hub feature settings from the configuration data store, returning defaults if not yet stored.
        /// </summary>
        public async Task<HubSettings> GetHubSettings()
        {
            if (_cachedHubSettings != null)
            {
                return _cachedHubSettings;
            }

            HubSettings? settings = null;

            try
            {
                settings = await _configStore.Get<HubSettings>(nameof(HubSettings), HubSettings.SettingsId);
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Failed to read hub settings from configuration store, using defaults: {exp}");
            }

            settings ??= new HubSettings();

            settings.ManagedChallenge ??= new ManagedChallengeSettings();
            settings.ManagedAcme ??= new ManagedAcmeSettings();

            _cachedHubSettings = settings;

            return settings;
        }

        /// <summary>
        /// Add/update the hub feature settings in the configuration data store.
        /// </summary>
        public async Task<ActionResult> UpdateHubSettings(HubSettings settings)
        {
            if (settings == null)
            {
                return new ActionResult { IsSuccess = false, Message = "Hub settings are required" };
            }

            settings.Id = HubSettings.SettingsId;
            settings.ItemType = nameof(HubSettings);
            settings.ManagedChallenge ??= new ManagedChallengeSettings();
            settings.ManagedAcme ??= new ManagedAcmeSettings();

            try
            {
                await _configStore.Update<HubSettings>(nameof(HubSettings), settings);

                _cachedHubSettings = settings;

                return new ActionResult { IsSuccess = true };
            }
            catch (Exception exp)
            {
                _serviceLog?.Error($"Failed to store hub settings: {exp}");
                return new ActionResult { IsSuccess = false, Message = "Failed to store hub settings" };
            }
        }
    }
}
