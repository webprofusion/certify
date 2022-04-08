using System.Threading.Tasks;
using Certify.Models;
using PropertyChanged;

namespace Certify.UI.ViewModel
{
    public partial class AppViewModel : BindableBase
    {
        /// <summary>
        /// If true, an app update is currently available
        /// </summary>
        public bool IsUpdateAvailable { get; set; }

        /// <summary>
        /// If an update is available this will contain more info about the new update 
        /// </summary>
        public UpdateCheck UpdateCheckResult { get; set; }

        /// <summary>
        /// Perform an app update check via service
        /// </summary>
        /// <returns></returns>
        public async Task<UpdateCheck> CheckForUpdates()
        {
            return await _certifyClient.CheckForUpdates();
        }

        /// <summary>
        /// Update preference to indicate this app install is registered to the reporting dashboard. 
        /// </summary>
        /// <returns></returns>
        internal async Task SetInstanceRegisteredOnDashboard()
        {
            var prefs = await GetPreferences();
            prefs.IsInstanceRegistered = true;
            await SetPreferences(prefs);
            Preferences = prefs;
        }

        /// <summary>
        /// Check if app install is currently actively licensed
        /// </summary>
        /// <returns></returns>
        public async Task<bool> CheckLicenseIsActive()
        {
            var licensingManager = PluginManager?.LicensingManager;

            if (licensingManager != null && !await licensingManager.IsInstallActive(ProductTypeId, EnvironmentUtil.GetAppDataFolder()))
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
        /// If true, a license upgrade is recommended based on current usage
        /// </summary>
        [DependsOn(nameof(NumManagedCerts), nameof(IsRegisteredVersion))]
        public bool IsLicenseUpgradeRecommended
        {
            get
            {
                if (!IsRegisteredVersion && NumManagedCerts >= 1)
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
        /// If true, app is unlicensed or license has expired and will revert to basic features
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
