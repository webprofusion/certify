using Certify.Models.Plugins;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Certify.Management
{
    public class PluginManager
    {
        public ILicensingManager LicensingManager { get; set; }
        public IDashboardClient DashboardClient { get; set; }

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
                PluginLog(exp.ToString());
            }
            return default(T);
        }

        public void PluginLog(string msg)
        {
            var path = Certify.Management.Util.GetAppDataFolder() + "\\plugin_log.txt";

            msg = "\r\n[" + DateTime.UtcNow.ToString() + "] " + msg;

            if (System.IO.File.Exists(path))
            {
                System.IO.File.AppendAllText(path, msg);
            }
            else
            {
                System.IO.File.WriteAllText(path, msg);
            }
        }

        public void LoadPlugins()
        {
            var s = Stopwatch.StartNew();

            LicensingManager = LoadPlugin<ILicensingManager>("Licensing.dll", typeof(ILicensingManager)) as ILicensingManager;
            DashboardClient = LoadPlugin<IDashboardClient>("DashboardClient.dll", typeof(IDashboardClient)) as IDashboardClient;

            s.Stop();

            Debug.WriteLine($"Plugin load took {s.ElapsedMilliseconds}ms");
        }
    }
}
