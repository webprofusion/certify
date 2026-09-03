using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Providers.Internal;
using PropertyChanged;

namespace Certify.UI.ViewModel
{
    public partial class AppViewModel : BindableBase
    {
        private readonly ILicensingManager _licensingManager = new LicensingManager();
        private IDashboardClient _dashboardClient = new Providers.Internal.DashboardClient();
        /// <summary>
        /// If true, an app update is currently available
        /// </summary>
        public bool IsUpdateAvailable { get; set; }

        /// <summary>
        /// If an update is available this will contain more info about the new update 
        /// </summary>
        public UpdateCheck UpdateCheckResult { get; set; }

        public IDashboardClient DashboardClient { get => _dashboardClient; }
        public ILicensingManager LicensingManager { get => _licensingManager; }

        /// <summary>
        /// Perform an app update check via service
        /// </summary>
        /// <returns></returns>
        public async Task<UpdateCheck> CheckForUpdates()
        {
            if (!TryGetAvailableCertifyClient(out var client))
            {
                return null;
            }

            return await client.CheckForUpdates();
        }

        /// <summary>
        /// Update preference to indicate this app install is registered to the reporting dashboard. 
        /// </summary>
        /// <returns></returns>
        internal async Task SetInstanceRegisteredOnDashboard()
            => await SetInstanceRegisteredOnDashboard(true);

        /// <summary>
        /// Update preference to indicate whether this app install is registered to the reporting dashboard.
        /// </summary>
        internal async Task SetInstanceRegisteredOnDashboard(bool isRegistered)
        {
            var prefs = await GetPreferences();
            prefs.IsInstanceRegistered = isRegistered;
            await SetPreferences(prefs);
        }

        internal async Task<ActionResult> QueueAllDashboardStatusReports()
        {
            return await _certifyClient.QueueAllStatusReports();
        }

        /// <summary>
        /// Check if app install is currently actively licensed
        /// </summary>
        /// <returns></returns>
        public async Task<bool> CheckLicenseIsActive()
        {

            if (_licensingManager != null && !await _licensingManager.IsInstallActive(ProductTypeId, EnvironmentUtil.EnsuredAppDataPath(), Preferences?.InstanceId ?? string.Empty))
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// If true, app is running in licensed mode
        /// </summary>
        public bool IsRegisteredVersion { get; set; }

        /// <summary>
        /// If true, the license status of this install has been checked, so IsRegisteredVersion reflects the real license state.
        /// Until then the license state is unknown and the UI must not advise the user that their license is not activated.
        /// </summary>
        public bool IsLicenseStatusKnown { get; set; }

        /// <summary>
        /// If true, a license upgrade is recommended based on current usage
        /// </summary>
        [DependsOn(nameof(NumManagedCerts), nameof(IsRegisteredVersion), nameof(IsLicenseStatusKnown))]
        public bool IsLicenseUpgradeRecommended
        {
            get
            {
                // only recommend a license once we have confirmed this install has no active license
                if (IsLicenseStatusKnown && !IsRegisteredVersion && UISettings?.CommunityMode != "personal")
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool IsPersonalUse
        {
            get
            {
                return UISettings?.CommunityMode == "personal";
            }
        }

        /// <summary>
        /// If true, app is running in Evaluation Mode (no license, or the license has expired). All features remain available.
        /// </summary>
        [DependsOn(nameof(IsRegisteredVersion), nameof(IsLicenseExpired))]
        public bool IsEvaluationMode
        {
            get
            {
                if (!IsRegisteredVersion)
                {
                    return true;
                }
                else if (IsRegisteredVersion && IsLicenseExpired)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// If true, the current registered license check has failed and is not currently active
        /// </summary>
        public bool IsLicenseExpired { get; set; }
    }
}
