using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Providers.DeploymentTasks;

namespace Certify.Core.Management.DeploymentTasks
{
    public static class DeploymentTaskProviderFactory
    {
        public async static Task<List<DeploymentProviderDefinition>> GetDeploymentTaskProviders(List<IDeploymentTaskProviderPlugin> providerPlugins)
        {

            var definitions = new List<DeploymentProviderDefinition>();

            // add core providers
            definitions.Add(Providers.DeploymentTasks.Core.Webhook.Definition);
            definitions.Add(Providers.DeploymentTasks.Core.IIS.Definition);
            definitions.Add(Providers.DeploymentTasks.Core.CertificateStore.Definition);
            definitions.Add(Providers.DeploymentTasks.Core.PowershellScript.Definition);

#if DEBUG
            definitions.Add(Providers.DeploymentTasks.Core.MockTask.Definition);
#endif

            // add providers from plugins
            if (providerPlugins == null)
            {
                return definitions;
            }

            foreach (var p in providerPlugins)
            {
                if (p != null)
                {
                    definitions.AddRange(p.GetProviders());
                }
            }

            return await Task.FromResult(definitions);
        }

        public static IDeploymentTaskProvider Create(string taskTypeId, List<IDeploymentTaskProviderPlugin> providerPlugins)
        {
            if (taskTypeId == null)
            {
                return null;
            }

            taskTypeId = taskTypeId.ToLower();

            if (taskTypeId == Providers.DeploymentTasks.Core.Webhook.Definition.Id.ToLower())
            {
                return new Certify.Providers.DeploymentTasks.Core.Webhook();
            }
            else if (taskTypeId == Providers.DeploymentTasks.Core.IIS.Definition.Id.ToLower())
            {
                return new Certify.Providers.DeploymentTasks.Core.IIS();
            }
            else if (taskTypeId == Providers.DeploymentTasks.Core.CertificateStore.Definition.Id.ToLower())
            {
                return new Certify.Providers.DeploymentTasks.Core.CertificateStore();
            }
            else if (taskTypeId == Providers.DeploymentTasks.Core.PowershellScript.Definition.Id.ToLower())
            {
                return new Certify.Providers.DeploymentTasks.Core.PowershellScript();
            }
#if DEBUG
            else if (taskTypeId == Providers.DeploymentTasks.Core.MockTask.Definition.Id.ToLower())
            {
                return new Certify.Providers.DeploymentTasks.Core.MockTask();
            }
#endif
            if (providerPlugins == null)
            {
                return null;
            }

            // Find provider for this type id
            foreach (var p in providerPlugins)
            {
                var provider = p.GetProvider(taskTypeId);
                if (provider != null)
                {
                    return provider;
                }
            }

            throw new ArgumentException("Deploy Task Type Unknown:" + (taskTypeId ?? "<none>"));
        }
    }
}
