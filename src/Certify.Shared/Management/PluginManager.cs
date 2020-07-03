using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Providers.DeploymentTasks;
using Serilog;

namespace Certify.Management
{
    public class PluginManager
    {
        public const string APPDATASUBFOLDER = "Certify";

        public ILicensingManager LicensingManager { get; set; }
        public IDashboardClient DashboardClient { get; set; }
        public List<IDeploymentTaskProviderPlugin> DeploymentTaskProviders { get; set; }
        public List<ICertificateManagerProviderPlugin> CertificateManagerProviders { get; set; }
        public List<IDnsProviderProviderPlugin> DnsProviderProviders { get; set; }
        public bool DnsMSDNSFailedToLoad { get; private set; }

        // ISSUE: Difficult to plumb PluginManager all the way through to ChallengeProviders, as that's about 7-8 layers deep on the call stack from where this is accessible.
        // Since there's no injection container, making a cubbyhole for it here.
        public static PluginManager _instance { get; private set; }

        private Models.Providers.ILog _log = null;

        public PluginManager()
        {
            _log = new Models.Loggy(
                    new LoggerConfiguration()
                        .MinimumLevel.Information()
                        .WriteTo.File(Path.Combine(GetAppDataFolder("logs"), "plugins.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
                        .CreateLogger()
                );

            if (_instance == null)
            {
                _instance = this;
            }
        }

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


        private string GetPluginFolderPath(bool usePluginSubfolder = true)
        {
            var executableLocation = Assembly.GetExecutingAssembly().Location;
            if (usePluginSubfolder)
            {
                var path = Path.Combine(Path.GetDirectoryName(executableLocation), "Plugins");
                return path;
            }
            else
            {
                return Path.GetDirectoryName(executableLocation);
            }
        }

        private T LoadPlugin<T>(string dllFileName)
        {
            Type interfaceType = typeof(T);
            try
            {
                var pluginPath = Path.Combine(GetPluginFolderPath(), dllFileName);

                if (!File.Exists(pluginPath))
                {
                    pluginPath = Path.Combine(GetPluginFolderPath(usePluginSubfolder: false), dllFileName);
                }

                if (File.Exists(pluginPath))
                {
                    // https://stackoverflow.com/questions/10732933/can-i-use-activator-createinstance-with-an-interface
                    var pluginAssembly = Assembly.LoadFrom(pluginPath);

                    var exportedTypes = pluginAssembly.GetExportedTypes();

                    var pluginType = pluginAssembly.GetTypes()
                        .Where(type => type.GetInterfaces()
                        .Any(inter => inter.IsAssignableFrom(interfaceType)))
                        .FirstOrDefault();

                    var obj = (T)Activator.CreateInstance(pluginType);

                    return obj;
                }
                else
                {
                    _log?.Warning($"Plugin Load Failed [{interfaceType}] File does not exist: {dllFileName}");
                }
            }
            catch (ReflectionTypeLoadException ex)
            {

                _log?.Warning($"Plugin Load Failed [{interfaceType}] :: {dllFileName} [Reflection or Loader Error]");

                _log.Error(ex.ToString());
                foreach (var loaderEx in ex.LoaderExceptions)
                {
                    _log.Error(loaderEx.ToString());
                }
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
                LicensingManager = LoadPlugin<ILicensingManager>("Plugin.Licensing.dll");

            }

            if (includeSet.Contains("DashboardClient"))
            {
                DashboardClient = LoadPlugin<IDashboardClient>("Plugin.DashboardClient.dll");
            }

            if (includeSet.Contains("DeploymentTasks"))
            {
                var deploymentTaskProviders = new List<IDeploymentTaskProviderPlugin>();
                DeploymentTaskProviders = deploymentTaskProviders;
                var core = LoadPlugin<IDeploymentTaskProviderPlugin>("Plugin.DeploymentTasks.Core.dll");
                var azure = LoadPlugin<IDeploymentTaskProviderPlugin>("Plugin.DeploymentTasks.Azure.dll");
                deploymentTaskProviders.Add(core);
                deploymentTaskProviders.Add(azure);
                var otherAssemblies = new DirectoryInfo(GetPluginFolderPath()).GetFiles("Plugin.DeploymentTasks.*.dll")
                    .Where(f => 
                        f.Name.ToUpperInvariant() != "PLUGIN.DEPLOYMENTTASKS.CORE.DLL" && 
                        f.Name.ToUpperInvariant() != "PLUGIN.DEPLOYMENTTASKS.AZURE.DLL");
                var others = otherAssemblies.Select(assem => LoadPlugin<IDeploymentTaskProviderPlugin>(assem.Name)).ToList();
                deploymentTaskProviders.AddRange(others);
            }

            if (includeSet.Contains("CertificateManagers"))
            {
                var certManagerProviders = LoadPlugin<ICertificateManagerProviderPlugin>("Plugin.CertificateManagers.dll");
                CertificateManagerProviders = new List<ICertificateManagerProviderPlugin>
                {
                    certManagerProviders
                };
            }

            if (includeSet.Contains("ChallengeProviders"))
            {
                var dnsProviderProviders = new List<IDnsProviderProviderPlugin>();
                DnsProviderProviders = dnsProviderProviders;

                // ISSUE: hidden dependency from Shared Core to Core, breaks layering
                var builtInProvider = (IDnsProviderProviderPlugin)Activator.CreateInstance(Type.GetType("Certify.Core.Management.Challenges.ChallengeProviders+BuiltinDnsProviderProvider, Certify.Core"));
                dnsProviderProviders.Add(builtInProvider);
                var poshProvider = (IDnsProviderProviderPlugin)Activator.CreateInstance(Type.GetType("Certify.Core.Management.Challenges.DNS.DnsProviderPoshACME+PoshACMEDnsProviderProvider, Certify.Core"));
                dnsProviderProviders.Add(poshProvider);

                var inBoxProviders = new string[]
                {
                    "Certify.DNS.AcmeDns.dll",
                    "Certify.DNS.Aliyun.dll",
                    "Certify.DNS.AWSRoute53.dll",
                    "Certify.DNS.Azure.dll",
                    "Certify.DNS.Cloudflare.dll",
                    "Certify.DNS.DnsMadeEasy.dll",
                    "Certify.DNS.GoDaddy.dll",
                    // MSDNS intentionally skipped due to special handling characteristics
                    "Certify.DNS.NameCheap.dll",
                    "Certify.DNS.OVH.dll",
                    "Certify.DNS.SimpleDNSPlus.dll",
                    "Certify.DNS.TransIP.dll"
                };
                foreach (var name in inBoxProviders)
                {
                    var plugin = LoadPlugin<IDnsProviderProviderPlugin>(name);
                    dnsProviderProviders.Add(plugin);
                }
                
                try
                {
                    var msdnsProvider = LoadPlugin<IDnsProviderProviderPlugin>("Certify.DNS.MicrosoftDns.dll");
                    dnsProviderProviders.Add(msdnsProvider);
                }
                catch(Exception)
                {
                    // Microsoft.Management.Infrastructure is not available or Windows Management Framework for Powershell is not installed.
                    // Remember that this happened so it can be reported later if needed.
                    DnsMSDNSFailedToLoad = true;
                }

                var otherAssemblies = new DirectoryInfo(GetPluginFolderPath()).GetFiles("Plugin.DNS.*.dll");
                var others = otherAssemblies.Select(assem => LoadPlugin<IDnsProviderProviderPlugin>(assem.Name)).ToList();
                dnsProviderProviders.AddRange(others);
            }

            s.Stop();

            _log?.Debug($"Plugin load took {s.ElapsedMilliseconds}ms");
        }
    }
}
