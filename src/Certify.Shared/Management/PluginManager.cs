using Certify.Models.Plugins;
using System;
using System.ComponentModel.Composition;
using System.Composition.Hosting;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Certify.Management
{
    public class PluginManager
    {
        [Import]
        public ILicensingManager LicensingManager { get; set; }

        [Import]
        public IDashboardClient DashboardClient { get; set; }

        [Import]
        public IACMEClientProvider AcmeClientProvider { get; set; }

        [Import]
        public IVaultProvider VaultProvider { get; set; }

        private string GetPluginFolderPath()
        {
            var executableLocation = Assembly.GetEntryAssembly().Location;
            var path = Path.Combine(Path.GetDirectoryName(executableLocation), "Plugins");
            return path;
        }

        private object LoadPlugin(string dllFileName, Type interfaceType)
        {
            try
            {
                var assembly = Assembly.LoadFrom(Path.Combine(GetPluginFolderPath(), dllFileName));
                var configuration = new ContainerConfiguration().WithAssembly(assembly);

                using (var container = configuration.CreateContainer())
                {
                    object plugin = container.GetExport(interfaceType);
                    return plugin;
                }
            }
            catch (Exception exp)
            {
                PluginLog(exp.ToString());
            }
            return null;
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

            LicensingManager = LoadPlugin("Licensing.dll", typeof(ILicensingManager)) as ILicensingManager;
            DashboardClient = LoadPlugin("DashboardClient.dll", typeof(IDashboardClient)) as IDashboardClient;

            //AcmeClientProvider = LoadPlugin("Certify.Providers.ACMESharp.dll", typeof(IACMEClientProvider)) as IACMEClientProvider;
            //VaultProvider = LoadPlugin("Certify.Providers.ACMESharp.dll", typeof(IVaultProvider)) as IVaultProvider;

            s.Stop();

            Debug.WriteLine($"Plugin load took {s.ElapsedMilliseconds}ms");
        }
    }
}