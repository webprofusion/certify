﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Certify.Management
{
    public class PluginLoadResult : ActionResult
    {
        public string PluginName { get; set; }

        public PluginLoadResult(string name, string msg, bool isSuccess)
        {
            PluginName = name;
            Message = msg;
            IsSuccess = isSuccess;
        }
    }
    public class PluginManager
    {
        public const string PLUGINS_LICENSING = "Licensing";
        public const string PLUGINS_DASHBOARD = "DashboardClient";
        public const string PLUGINS_DEPLOYMENT_TASKS = "DeploymentTasks";
        public const string PLUGINS_CERTIFICATE_MANAGERS = "CertificateManagers";
        public const string PLUGINS_DNS_PROVIDERS = "DnsProviders";
        public const string PLUGINS_SERVER_PROVIDERS = "ServerProviders";
        public const string PLUGINS_DATASTORE_PROVIDERS = "DataStores";

        public ILicensingManager LicensingManager { get; set; }
        public IDashboardClient DashboardClient { get; set; }
        public List<IDeploymentTaskProviderPlugin> DeploymentTaskProviders { get; set; }
        public List<ICertificateManagerProviderPlugin> CertificateManagerProviders { get; set; }
        public List<IServerProviderPlugin> ServerProviders { get; set; }
        public List<IDnsProviderProviderPlugin> DnsProviderProviders { get; set; }
        public List<IManagedItemProviderPlugin> ManagedItemStoreProviders { get; set; }
        public List<ICredentialStoreProviderPlugin> CredentialStoreProviders { get; set; }
        public List<PluginLoadResult> PluginLoadResults { get; private set; } = new List<PluginLoadResult>();

        /// <summary>
        /// If enabled, external plugins will load from AppData plugins folders
        /// </summary>
        public bool EnableExternalPlugins { get; set; } = false;
        public static PluginManager CurrentInstance { get; private set; }

        private Models.Providers.ILog _log = null;

        public PluginManager()
        {
            var serilogLogger = new LoggerConfiguration()
                        .Enrich.FromLogContext()
                        .MinimumLevel.Information()
                        .WriteTo.File(Path.Combine(EnvironmentUtil.CreateAppDataPath("logs"), "plugins.log"), shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
                        .CreateLogger();

           var msLogger = new SerilogLoggerFactory(serilogLogger).CreateLogger<PluginManager>(); 

            _log = new Models.Loggy(msLogger);

            if (CurrentInstance == null)
            {
                CurrentInstance = this;
            }
        }

        private string GetPluginFolderPath(bool usePluginSubfolder = true, bool useAppData = false)
        {
            if (!useAppData)
            {
                var executableLocation = AppContext.BaseDirectory;
                if (string.IsNullOrEmpty(executableLocation))
                {
                    throw new ArgumentNullException("GetPluginFolderPath: Executing assembly location is null");
                }

                if (usePluginSubfolder)
                {
                    var path = Path.Combine(executableLocation, "plugins");
                    return path;
                }
                else
                {
                    return Path.GetDirectoryName(executableLocation);
                }
            }
            else
            {
                return EnvironmentUtil.CreateAppDataPath("plugins");
            }
        }

        private T LoadPlugin<T>(string dllFileName, string pluginFolder = null)
        {
            var interfaceType = typeof(T);
            var pluginPath = string.Empty;

            try
            {
                pluginPath = pluginFolder != null ? Path.Combine(pluginFolder, dllFileName) : Path.Combine(GetPluginFolderPath(), dllFileName);

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

                    if (pluginType != null)
                    {
                        var obj = (T)Activator.CreateInstance(pluginType);

                        return obj;
                    }
                    else
                    {
                        _log?.Debug($"Plugin Load Skipped [{interfaceType}] File does not contain a matching interface: {dllFileName} in {pluginPath}");
                    }
                }
                else
                {
                    _log?.Warning($"Plugin Load Failed [{interfaceType}] File does not exist: {dllFileName} in {pluginPath}");
                }
            }
            catch (ReflectionTypeLoadException ex)
            {

                _log?.Warning($"Plugin Load Failed [{interfaceType}] :: {dllFileName} [Reflection or Loader Error] in {pluginPath}");

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

            return default;
        }

        public void LoadPlugins(List<string> includeSet, bool usePluginSubfolder = true)
        {
            var s = Stopwatch.StartNew();

            if (includeSet.Contains(PLUGINS_LICENSING))
            {
                LicensingManager = LoadPlugin<ILicensingManager>("Plugin.Licensing.dll");
            }

            if (includeSet.Contains(PLUGINS_DASHBOARD))
            {
                DashboardClient = LoadPlugin<IDashboardClient>("Plugin.DashboardClient.dll");
            }

            if (includeSet.Contains(PLUGINS_DEPLOYMENT_TASKS))
            {
                DeploymentTaskProviders = new List<IDeploymentTaskProviderPlugin>();

                var taskPlugins = LoadPlugins<IDeploymentTaskProviderPlugin>("Plugin.DeploymentTasks.*.dll", usePluginSubfolder: usePluginSubfolder);
                DeploymentTaskProviders.AddRange(taskPlugins);

                // load custom
                if (EnableExternalPlugins)
                {
                    var customPlugins = LoadPlugins<IDeploymentTaskProviderPlugin>("*.dll", loadFromAppData: true);
                    DeploymentTaskProviders.AddRange(customPlugins);
                }
            }

            if (includeSet.Contains(PLUGINS_CERTIFICATE_MANAGERS))
            {
                CertificateManagerProviders = new List<ICertificateManagerProviderPlugin>();

                var certManagerProviders = LoadPlugins<ICertificateManagerProviderPlugin>("Plugin.CertificateManagers.*.dll", usePluginSubfolder: usePluginSubfolder);
                CertificateManagerProviders.AddRange(certManagerProviders);

                // load custom
                if (EnableExternalPlugins)
                {
                    var customPlugins = LoadPlugins<ICertificateManagerProviderPlugin>("*.dll", loadFromAppData: true);
                    CertificateManagerProviders.AddRange(customPlugins);
                }
            }

            if (includeSet.Contains(PLUGINS_DNS_PROVIDERS))
            {
                DnsProviderProviders = new List<IDnsProviderProviderPlugin>();

                // load core providers as plugins
                var builtInProvider = (IDnsProviderProviderPlugin)Activator.CreateInstance(Type.GetType("Certify.Core.Management.Challenges.ChallengeProviders+BuiltinDnsProviderProvider, Certify.Core"));
                DnsProviderProviders.Add(builtInProvider);

                var poshAcmeProvider = (IDnsProviderProviderPlugin)Activator.CreateInstance(Type.GetType("Certify.Core.Management.Challenges.DNS.DnsProviderPoshACME+PoshACMEDnsProviderProvider, Certify.Shared.Extensions"));
                DnsProviderProviders.Add(poshAcmeProvider);

                var otherProviders = LoadPlugins<IDnsProviderProviderPlugin>("Plugin.DNS.*.dll", usePluginSubfolder: usePluginSubfolder);
                DnsProviderProviders.AddRange(otherProviders);

                if (EnableExternalPlugins)
                {
                    var customPlugins = LoadPlugins<IDnsProviderProviderPlugin>("*.dll", loadFromAppData: true);
                    if (customPlugins.Any())
                    {
                        DnsProviderProviders.AddRange(customPlugins);
                    }
                }
            }

            if (includeSet.Contains(PLUGINS_SERVER_PROVIDERS))
            {
                ServerProviders = new List<IServerProviderPlugin>();

                var providers = LoadPlugins<IServerProviderPlugin>("Plugin.ServerProviders.*.dll", usePluginSubfolder: usePluginSubfolder);
                ServerProviders.AddRange(providers);

                // load custom
                if (EnableExternalPlugins)
                {
                    var customPlugins = LoadPlugins<IServerProviderPlugin>("*.dll", loadFromAppData: true);
                    ServerProviders.AddRange(customPlugins);
                }
            }

            if (includeSet.Contains(PLUGINS_DATASTORE_PROVIDERS))
            {
                ManagedItemStoreProviders = new List<IManagedItemProviderPlugin>();
                CredentialStoreProviders = new List<ICredentialStoreProviderPlugin>();

                var providers = LoadPlugins<IManagedItemProviderPlugin>("Plugin.Datastore.*.dll", usePluginSubfolder: usePluginSubfolder);
                ManagedItemStoreProviders.AddRange(providers);

                var credProviders = LoadPlugins<ICredentialStoreProviderPlugin>("Plugin.Datastore.*.dll", usePluginSubfolder: usePluginSubfolder);
                CredentialStoreProviders.AddRange(credProviders);

            }

            s.Stop();

            _log?.Debug($"Plugin load took {s.ElapsedMilliseconds}ms");
        }

        private List<T> LoadPlugins<T>(string fileMatch, bool loadFromAppData = false, bool usePluginSubfolder = true)
        {
            var plugins = new List<T>();

            var pluginDir = new DirectoryInfo(GetPluginFolderPath(usePluginSubfolder));

            if (loadFromAppData)
            {
                pluginDir = new DirectoryInfo(GetPluginFolderPath(useAppData: true));
            }

            if (!pluginDir.Exists)
            {
                return new List<T> { };
            }

            var pluginAssemblyFiles = pluginDir.GetFiles(fileMatch);

            var discoveredPlugins = pluginAssemblyFiles.Select(assem =>
            {

                try
                {
                    var result = LoadPlugin<T>(assem.Name, pluginDir.FullName);

                    if (result != null)
                    {
                        PluginLoadResults.Add(new PluginLoadResult(assem.Name, $"Loaded plugin: {assem.Name}", true));
                    }
                    else
                    {
                        PluginLoadResults.Add(new PluginLoadResult(assem.Name, $"Failed to load plugin: {assem.Name}", false));
                    }

                    return result;
                }
                catch (Exception exp)
                {
                    // failed to load plugin
                    PluginLoadResults.Add(new PluginLoadResult(assem.Name, $"Failed to load plugin: {assem.Name} {exp}", false));
                    return default;
                }
            })
            .Where(p => p != null)
            .ToList();

            plugins.AddRange(discoveredPlugins);

            return plugins;
        }
    }
}
