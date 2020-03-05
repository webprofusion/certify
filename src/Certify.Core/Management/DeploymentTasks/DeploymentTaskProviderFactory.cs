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
