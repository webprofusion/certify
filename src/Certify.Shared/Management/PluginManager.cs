using Certify.Models.Plugins;
using System;
using System.Composition.Hosting;
using System.IO;
using System.Reflection;

namespace Certify.Management
{
    public class PluginManager
    {
        public ILicensingManager LicensingManager { get; set; }

        private string GetPluginFolderPath()
        {
            var executableLocation = Assembly.GetEntryAssembly().Location;
            var path = Path.Combine(Path.GetDirectoryName(executableLocation), "Plugins");
            return path;
        }

        private void LoadLicensingManager()
        {
            try
            {
                var assembly = Assembly.LoadFrom(Path.Combine(GetPluginFolderPath(), "Licensing.dll"));
                var configuration = new ContainerConfiguration().WithAssembly(assembly);

                using (var container = configuration.CreateContainer())
                {
                    LicensingManager = container.GetExport<ILicensingManager>();
                }
            }
            catch (Exception exp)
            {
                PluginLog(exp.ToString());
            }
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
            LoadLicensingManager();

            //TODO: validation/ installation plugins
        }
    }
}