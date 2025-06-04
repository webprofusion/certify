using System;
using System.IO;
using Certify.Models;
using Certify.Providers;
using Certify.Shared;
using Newtonsoft.Json;

namespace Certify.SharedUtils
{
    public class ServiceConfigManager : IServiceConfigProvider
    {
        /// <summary>
        /// Get default or saved service config settings
        /// </summary>
        /// <returns>  </returns>
        public static ServiceConfig GetAppServiceConfig()
        {
            var serviceConfig = new ServiceConfig
            {
                ConfigStatus = ConfigStatus.DefaultFailed
            };

            var appDataPath = string.Empty;
            try
            {
                appDataPath = EnvironmentUtil.EnsuredAppDataPath();
            }
            catch (Exception exp)
            {
                System.Console.WriteLine($"ServiceConfigManager: Failed to get AppData path. {exp.Message}");
                serviceConfig.ServiceFaultMsg = $"Failed to get AppData path. {exp.Message}";
                return serviceConfig;
            }

            var serviceConfigFile = Path.Combine(appDataPath, "serviceconfig.json");
#if DEBUG
            serviceConfigFile = Path.Combine(appDataPath, "serviceconfig.debug.json");
#endif
            try
            {
                if (File.Exists(serviceConfigFile))
                {
                    var config = File.ReadAllText(serviceConfigFile);
                    if (!string.IsNullOrWhiteSpace(config))
                    {
                        serviceConfig = JsonConvert.DeserializeObject<ServiceConfig>(config);
                    }
                    else
                    {
                        System.Console.WriteLine($"ServiceConfigManager: Empty service config found at {serviceConfigFile}");
                    }

                    serviceConfig.ConfigStatus = ConfigStatus.NotModified;
                }
                else
                {
                    serviceConfig.ConfigStatus = ConfigStatus.New;
                    System.Console.WriteLine($"ServiceConfigManager: No service config found at {serviceConfigFile}");
                }
            }
            catch (UnauthorizedAccessException uaExp)
            {
                serviceConfig.ConfigStatus = ConfigStatus.DefaultFailed;
                serviceConfig.ServiceFaultMsg = $"Access denied to service configuration file at {serviceConfigFile}. {uaExp.Message}";
                System.Console.WriteLine($"ServiceConfigManager: {serviceConfig.ServiceFaultMsg}");
            }
            catch (Exception exp)
            {
                if (serviceConfig != null)
                {
                    serviceConfig.ConfigStatus = ConfigStatus.DefaultFailed;
                    serviceConfig.ServiceFaultMsg = $"There was a problem loading the service configuration from {serviceConfigFile} {exp.Message}";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ServiceConfigManager: Fault loading service config found at {exp}");
                }
            }

            // if something went wrong, default to standard config
            if (serviceConfig == null)
            {
                System.Console.WriteLine($"ServiceConfigManager: Falling back to default service config.");
                serviceConfig = new ServiceConfig()
                {
                    ConfigStatus = ConfigStatus.DefaultFailed
                };
            }

            return serviceConfig;
        }

        public static void StoreUpdatedAppServiceConfig(ServiceConfig config, bool throwOnError = false)
        {
            if (config == null)
            {
                return;
            }

            var appDataPath = EnvironmentUtil.EnsuredAppDataPath();
            var serviceConfigFile = Path.Combine(appDataPath, "serviceconfig.json");
#if DEBUG
            serviceConfigFile = Path.Combine(appDataPath, "serviceconfig.debug.json");
#endif
            try
            {
                File.WriteAllText(serviceConfigFile, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            catch
            {
                if (throwOnError)
                {
                    throw;
                }
            }
        }

        public ServiceConfig GetServiceConfig()
        {
            return GetAppServiceConfig();
        }
    }
}
