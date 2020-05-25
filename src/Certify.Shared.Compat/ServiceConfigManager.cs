using System;
using System.Collections.Generic;
using System.IO;
using Certify.Shared;
using Newtonsoft.Json;

namespace Certify.SharedUtils
{
    public class ServiceConfigManager
    {
        public const string APPDATASUBFOLDER = "Certify";

        public static string GetAppDataFolder(string subFolder = null)
        {
            var parts = new List<string>()
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                APPDATASUBFOLDER
            };

            if (subFolder != null)
            {
                parts.Add(subFolder);
            }

            var path = Path.Combine(parts.ToArray());

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

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

            var appDataPath = GetAppDataFolder();
            var serviceConfigFile = appDataPath + "\\serviceconfig.json";
#if DEBUG
            serviceConfigFile = appDataPath + "\\serviceconfig.debug.json";
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

                    serviceConfig.ConfigStatus = ConfigStatus.NotModified;
                }
                else
                {
                    serviceConfig.ConfigStatus = ConfigStatus.New;
                }
            }
            catch (Exception exp) {
                if (serviceConfig != null)
                {
                    serviceConfig.ConfigStatus = ConfigStatus.DefaultFailed;
                    serviceConfig.ServiceFaultMsg = $"There was a problem loading the service configuration from {serviceConfigFile} {exp.Message}";
                }
            }

            // if something went wrong, default to standard config
            if (serviceConfig == null)
            {
                serviceConfig = new ServiceConfig()
                {
                    ConfigStatus = ConfigStatus.DefaultFailed
                };
            }

            return serviceConfig;
        }

        public static void StoreCurrentAppServiceConfig()
        {
            var config = GetAppServiceConfig();
            StoreUpdatedAppServiceConfig(config);
        }

        public static void StoreUpdatedAppServiceConfig(ServiceConfig config)
        {
            if (config == null)
            {
                return;
            }

            var appDataPath = GetAppDataFolder();
            var serviceConfigFile = appDataPath + "\\serviceconfig.json";
#if DEBUG
            serviceConfigFile = appDataPath + "\\serviceconfig.debug.json";
#endif
            try
            {
                File.WriteAllText(serviceConfigFile, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            catch { }
        }
    }
}
