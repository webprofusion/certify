using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Certify.Models.Plugins;
using Certify.Providers.DeploymentTasks;
using Serilog;

namespace Certify.Management
{
    public class PluginManager
    {
        public ILicensingManager LicensingManager { get; set; }
        public IDashboardClient DashboardClient { get; set; }
        public List<IDeploymentTaskProviderPlugin> DeploymentTaskProviders { get; set; }

        private Models.Providers.ILog _log = null;

        public PluginManager()
        {
            _log = new Models.Loggy(
                    new LoggerConfiguration()
                        .MinimumLevel.Information()
                        .WriteTo.File(Management.Util.GetAppDataFolder("logs") + "\\plugins.log", shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
                        .CreateLogger()
                );

        }

        private string GetPluginFolderPath()
        {
            var executableLocation = Assembly.GetExecutingAssembly().Location;
            var path = Path.Combine(Path.GetDirectoryName(executableLocation), "Plugins");
            return path;
        }

        private T LoadPlugin<T>(string dllFileName, Type interfaceType)
        {
            try
            {
                var pluginPath = GetPluginFolderPath() + "\\" + dllFileName;

                // https://stackoverflow.com/questions/10732933/can-i-use-activator-createinstance-with-an-interface
                var loadedType = (from t in Assembly.LoadFrom(pluginPath).GetExportedTypes()
                                  where !t.IsInterface && !t.IsAbstract
                                  where interfaceType.IsAssignableFrom(t)
                                  select t)
                                     .FirstOrDefault();

                var obj = (T)Activator.CreateInstance(loadedType);

                return obj;
            }
            catch (Exception exp)
            {
                _log?.Error(exp.ToString());
            }
            return default(T);
        }

        public void LoadPlugins(List<string> includeSet)
        {
            var s = Stopwatch.StartNew();
            
            if (includeSet.Contains("Licensing"))
            {
                LicensingManager = LoadPlugin<ILicensingManager>("Licensing.dll", typeof(ILicensingManager)) as ILicensingManager;

            }

            if (includeSet.Contains("DashboardClient"))
            {
                DashboardClient = LoadPlugin<IDashboardClient>("DashboardClient.dll", typeof(IDashboardClient)) as IDashboardClient;
            }

            if (includeSet.Contains("DeploymentTasks"))
            {
                var deploymentTaskPlugin = LoadPlugin<IDeploymentTaskProviderPlugin>("DeploymentTasks.dll", typeof(IDeploymentTaskProviderPlugin)) as IDeploymentTaskProviderPlugin;
                DeploymentTaskProviders = new List<IDeploymentTaskProviderPlugin>
                {
                    deploymentTaskPlugin
                };
            }

            s.Stop();

            _log?.Debug($"Plugin load took {s.ElapsedMilliseconds}ms");
        }
    }
}
