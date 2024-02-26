using System;
using System.Threading.Tasks;
using Certify.Models;

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
                if (licensingManager.IsInstallRegistered(ProductTypeID, EnvironmentUtil.CreateAppDataPath()))
                {
                    return true;
                }
            }

            return false;
        }

        internal async Task Activate(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Not enough arguments");
                return;
            }

            var email = args[1];
            var key = args[2];

            var result = await Activate(email, key);

            if (result.IsSuccess)
            {
                Console.WriteLine("License Activated");
            }
            else
            {
                Console.WriteLine(result.Message);
            }
        }

        private async Task<Models.Shared.LicenseKeyInstallResult> Activate(string email, string key)
        {
            InitPlugins();

            var licensingManager = _pluginManager.LicensingManager;
            if (licensingManager != null)
            {
                var settingsPath = EnvironmentUtil.CreateAppDataPath();

                var activated = await licensingManager.IsInstallActive(ProductTypeID, settingsPath);
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

                        if (result.IsSuccess)
                        {
                            licensingManager.FinaliseInstall(ProductTypeID, result, settingsPath);
                        }

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

        internal async Task Deactivate(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Not enough arguments");
                return;
            }

            var email = args[1];

            var deactivated = await Deactivate(email);

            if (deactivated)
            {
                Console.WriteLine("License Deactivated");
            }
            else
            {
                Console.WriteLine("Failed to deactivate license");
            }
        }

        private async Task<bool> Deactivate(string email)
        {
            InitPlugins();

            var licensingManager = _pluginManager.LicensingManager;
            if (licensingManager != null)
            {
                var instance = new Models.Shared.RegisteredInstance
                {
                    InstanceId = _prefs.InstanceId,
                    AppVersion = Management.Util.GetAppVersion().ToString()
                };

                var deactivated = await licensingManager.DeactivateInstall(ProductTypeID, EnvironmentUtil.CreateAppDataPath(), email, instance);

                return deactivated;
            }
            else
            {
                return false;
            }
        }
    }
}
