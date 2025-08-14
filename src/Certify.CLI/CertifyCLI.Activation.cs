using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        private static int ProductTypeID = 1;

        public partial class LicenseCheckResult
        {
            public bool IsLicensed { get; set; }
            public bool IsValid { get; set; }
            public string Status { get; set; } = string.Empty;

        }

        [JsonSerializable(typeof(LicenseCheckResult))]

        public partial class LicenseCheckContext : JsonSerializerContext
        {
        }

        JsonSerializerOptions _licenseCheckSerializationOptions = new()
        {
            TypeInfoResolver = LicenseCheckContext.Default,
            WriteIndented = true
        };

        private bool IsRegistered()
        {
            if (_licensingManager?.IsInstallRegistered(ProductTypeID, EnvironmentUtil.EnsuredAppDataPath()) == true)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        internal async Task<bool> LicenseCheck(string[] args)
        {
            var licenseCheckResult = new LicenseCheckResult
            {
                IsLicensed = false,
                IsValid = false,
                Status = "unlicensed"
            };

            if (_licensingManager != null)
            {
                var instance = new Models.Shared.RegisteredInstance
                {
                    InstanceId = _prefs.InstanceId,
                    AppVersion = Management.Util.GetAppVersion().ToString()
                };

                var registered = _licensingManager.IsInstallRegistered(ProductTypeID, EnvironmentUtil.EnsuredAppDataPath());

                if (registered)
                {
                    licenseCheckResult.IsLicensed = true;
                    var isActive = await _licensingManager.IsInstallActive(ProductTypeID, EnvironmentUtil.EnsuredAppDataPath());
                    if (isActive)
                    {
                        licenseCheckResult.IsValid = true;
                        licenseCheckResult.Status = "active";
                    }
                    else
                    {
                        licenseCheckResult.IsValid = false;
                        licenseCheckResult.Status = "invalid";
                    }
                }

                var output = System.Text.Json.JsonSerializer.Serialize(licenseCheckResult, options: _licenseCheckSerializationOptions);

                Console.WriteLine(output);

                return licenseCheckResult.IsLicensed && licenseCheckResult.IsValid;
            }
            else
            {
                var output = System.Text.Json.JsonSerializer.Serialize(licenseCheckResult, options: _licenseCheckSerializationOptions);
                Console.WriteLine(output);
                return false;
            }
        }

        internal async Task Activate(string[] args)
        {
            var email = string.Empty;
            var key = string.Empty;

            if (args[0].ToLower() == "license")
            {
                if (args.Length < 4)
                {
                    Console.WriteLine("Not enough arguments");
                    return;
                }
                else
                {
                    // allow syntax as "certify license activate <email> <key>"
                    email = args[2];
                    key = args[3];
                }
            }
            else
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Not enough arguments");
                    return;
                }
                else
                {
                    // allow syntax as "certify activate <email> <key>"
                    email = args[1];
                    key = args[2];
                }
            }

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

            if (_licensingManager != null)
            {
                var settingsPath = EnvironmentUtil.EnsuredAppDataPath();

                var activated = await _licensingManager.IsInstallActive(ProductTypeID, settingsPath);
                if (!activated)
                {
                    var validationResult = await _licensingManager.Validate(ProductTypeID, email, key);
                    if (validationResult.IsValid)
                    {
                        var instance = new Models.Shared.RegisteredInstance
                        {
                            InstanceId = _prefs.InstanceId,
                            AppVersion = Management.Util.GetAppVersion().ToString()
                        };

                        // activate install
                        var result = await _licensingManager.RegisterInstall(ProductTypeID, email, key, instance);

                        if (result.IsSuccess)
                        {
                            _licensingManager.FinaliseInstall(ProductTypeID, result, settingsPath);
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

            var email = string.Empty;

            if (args[0].ToLower() == "license")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Not enough arguments");
                    return;
                }
                else
                {
                    // allow syntax as "certify license deactivate <email> <key>"
                    email = args[2];
                }
            }
            else
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Not enough arguments");
                    return;
                }
                else
                {
                    // allow syntax as "certify deactivate <email> <key>"
                    email = args[1];
                }
            }

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
            if (_licensingManager != null)
            {
                var instance = new Models.Shared.RegisteredInstance
                {
                    InstanceId = _prefs.InstanceId,
                    AppVersion = Management.Util.GetAppVersion().ToString()
                };

                var deactivated = await _licensingManager.DeactivateInstall(ProductTypeID, EnvironmentUtil.EnsuredAppDataPath(), email, instance);

                return deactivated;
            }
            else
            {
                return false;
            }
        }

        internal async Task JoinHub(string[] args)
        {
            if (args.Length < 5)
            {
                Console.WriteLine("Not enough arguments");
                return;
            }

            var url = args[2];
            var clientid = args[3];
            var secret = args[4];

            var result = await _certifyClient.JoinManagementHub(new Models.Hub.HubJoiningClientSecret { ClientId = clientid, Secret = secret, Url = url });

            if (result.IsSuccess)
            {
                Console.WriteLine("Joined Hub OK");
            }
            else
            {
                Console.WriteLine(result.Message);
            }
        }
    }
}
