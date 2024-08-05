using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Certify.Models;
using Certify.Models.Providers;
using Newtonsoft.Json;

namespace Certify.Shared.Core.Management
{
    public class DataStoreConnectionManager
    {
        private static string GetConfigPath()
        {
            var appDataPath = EnvironmentUtil.CreateAppDataPath();
            var configFile = Path.Combine(appDataPath, "datastores.json");
            return configFile;
        }

        /// Get list of saved data store connections
        /// </summary>
        /// <returns>  </returns>
        public static List<DataStoreConnection> GetDataStoreConnections(ILog log)
        {
            var defaultConnectionList = new List<DataStoreConnection>
            {
                new DataStoreConnection { Id = "(default)", TypeId = "sqlite", ConnectionConfig = "", Title = "(Default)" }
            };

            var configFile = GetConfigPath();

            try
            {
                if (File.Exists(configFile))
                {
                    var config = File.ReadAllText(configFile);
                    if (!string.IsNullOrWhiteSpace(config))
                    {
                        var savedList = JsonConvert.DeserializeObject<List<DataStoreConnection>>(config);
                        if (savedList?.Any() == true)
                        {
                            return savedList;
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                // failed to load
                log?.Error($"Failed to load data store connection configuration [{configFile}] :: {exp}");
            }

            // failed to get any list of server connections, return default
            return defaultConnectionList;
        }

        public static void Save(ILog log, List<DataStoreConnection> connections)
        {
            if (connections == null)
            {
                return;
            }

            var configFile = GetConfigPath();

            try
            {
                File.WriteAllText(configFile, JsonConvert.SerializeObject(connections, Formatting.Indented));
            }
            catch (Exception exp)
            {
                // failed to save
                log?.Error($"Failed to save data store connection configuration [{configFile}] :: {exp}");
                throw;
            }
        }
    }
}
