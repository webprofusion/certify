using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Certify.Models.Plugins;
using Serilog;

namespace Certify.Management
{
    public class PluginManager
    {
        public ILicensingManager LicensingManager { get; set; }
        public IDashboardClient DashboardClient { get; set; }

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
            var executableLocation = Assembly.GetEntryAssembly().Location;
            var path = Path.Combine(Path.GetDirectoryName(executableLocation), "Plugins");
            return path;
        }

        private T LoadPlugin<T>(string dllFileName, Type interfaceType)
        {
            try
            {
                // https://stackoverflow.com/questions/10732933/can-i-use-activator-createinstance-with-an-interface
                var loadedType = (from t in Assembly.LoadFrom(GetPluginFolderPath() + "\\" + dllFileName).GetExportedTypes()
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

        public void LoadPlugins()
        {
            var s = Stopwatch.StartNew();

            LicensingManager = LoadPlugin<ILicensingManager>("Licensing.dll", typeof(ILicensingManager)) as ILicensingManager;
            DashboardClient = LoadPlugin<IDashboardClient>("DashboardClient.dll", typeof(IDashboardClient)) as IDashboardClient;

            s.Stop();

            _log?.Debug($"Plugin load took {s.ElapsedMilliseconds}ms");
        }
    }
}
