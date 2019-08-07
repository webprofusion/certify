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
            var serviceConfig = new ServiceConfig();

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
                }
            }
            catch { }

            // if something went wrong, default to standard config
            if (serviceConfig == null)
            {
                serviceConfig = new ServiceConfig();
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

        /// <summary>
        /// Stored updated config for app service
        /// </summary>
        /// <param name="port">  </param>
        /// <returns>  </returns>
        public static bool SetAppServicePort(int port)
        {
            var config = GetAppServiceConfig();
            config.Port = port;
            StoreUpdatedAppServiceConfig(config);

            return true;
        }

    }
}
