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

            if (subFolder != null) parts.Add(subFolder);

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
            if (File.Exists(serviceConfigFile))
            {
                serviceConfig = JsonConvert.DeserializeObject<ServiceConfig>(File.ReadAllText(serviceConfigFile));
            }
            return serviceConfig;
        }

        public static void StoreCurrentAppServiceConfig()
        {
            var appDataPath = GetAppDataFolder();
            var config = GetAppServiceConfig();
            var serviceConfigFile = appDataPath + "\\serviceconfig.json";
#if DEBUG
            serviceConfigFile = appDataPath + "\\serviceconfig.debug.json";
#endif
            File.WriteAllText(serviceConfigFile, JsonConvert.SerializeObject(config));
        }

        /// <summary>
        /// Stored updated config for app service
        /// </summary>
        /// <param name="port">  </param>
        /// <returns>  </returns>
        public static bool SetAppServicePort(int port)
        {
            var appDataPath = GetAppDataFolder();
            var serviceConfigFile = appDataPath + "\\serviceconfig.json";
#if DEBUG
            serviceConfigFile = appDataPath + "\\serviceconfig.debug.json";
#endif
            try
            {
                ServiceConfig settings = new ServiceConfig();

                if (File.Exists(serviceConfigFile))
                {
                    settings = JsonConvert.DeserializeObject<ServiceConfig>(File.ReadAllText(serviceConfigFile));
                }

                settings.Port = port;

                File.WriteAllText(serviceConfigFile, JsonConvert.SerializeObject(settings));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

    }
}
