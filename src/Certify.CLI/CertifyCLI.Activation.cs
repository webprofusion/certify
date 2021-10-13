using System.Threading.Tasks;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        private static int ProductTypeID = 1;

        private bool IsRegistered()
        {
            var licensingManager = _pluginManager.LicensingManager;
            if (licensingManager != null)
            {
                if (licensingManager.IsInstallRegistered(ProductTypeID, Certify.Management.Util.GetAppDataFolder()))
                {
                    return true;
                }
            }
            return false;
        }

        private async Task<Models.Shared.LicenseKeyInstallResult> Activate(string email, string key)
        {
            var licensingManager = _pluginManager.LicensingManager;
            if (licensingManager != null)
            {
                var activated = await licensingManager.IsInstallActive(ProductTypeID, Certify.Management.Util.GetAppDataFolder());
                if (!activated)
                {
                    var validationResult = await licensingManager.Validate(ProductTypeID, email, key);
                    if (validationResult.IsValid)
                    {
                        var instance = new Models.Shared.RegisteredInstance
                        {
                            InstanceId = _prefs.InstanceId,
                            AppVersion = Management.Util.GetAppVersion().ToString()
                        };

                        // activate install
                        var result = await licensingManager.RegisterInstall(ProductTypeID, email, key, instance);

                        return result;
                    }
                    else
                    {
                        return new Models.Shared.LicenseKeyInstallResult { IsSuccess = false, Message = validationResult.ValidationMessage };
                    }
                }
                else
                {
                    return new Models.Shared.LicenseKeyInstallResult { IsSuccess = true, Message = "Instance already activated" };
                }
            }
            else
            {
                return new Models.Shared.LicenseKeyInstallResult { IsSuccess = false, Message = "Licensing plugin unavailable" };
            }
        }

        private async Task<bool> Deactivate(string email)
        {
            var licensingManager = _pluginManager.LicensingManager;
            if (licensingManager != null)
            {
                var instance = new Models.Shared.RegisteredInstance
                {
                    InstanceId = _prefs.InstanceId,
                    AppVersion = Management.Util.GetAppVersion().ToString()
                };

                var deactivated = await licensingManager.DeactivateInstall(ProductTypeID, Certify.Management.Util.GetAppDataFolder(), email, instance);

                return deactivated;
            }
            else
            {
                return false;
            }
        }
    }
}
