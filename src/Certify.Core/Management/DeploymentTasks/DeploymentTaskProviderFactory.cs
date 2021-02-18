using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Providers.DeploymentTasks;

namespace Certify.Core.Management.DeploymentTasks
{
    public static class DeploymentTaskProviderFactory
    {
        public async static Task<List<DeploymentProviderDefinition>> GetDeploymentTaskProviders(List<IDeploymentTaskProviderPlugin> providerPlugins)
        {
            var list = new List<DeploymentProviderDefinition>();

            var baseAssembly = typeof(DeploymentTaskProviderFactory).Assembly;

            // we filter the defined classes according to the interfaces they implement
            var typeList = baseAssembly.GetTypes().Where(type => type.GetInterfaces().Any(inter => inter == typeof(IDeploymentTaskProvider))).ToList();

            foreach (var t in typeList)
            {

                var def = (DeploymentProviderDefinition)t.GetProperty("Definition").GetValue(null);

                // conditionally include mock task
                if (def.Id == Providers.DeploymentTasks.Core.MockTask.Definition.Id)
                {

#if DEBUG
                    list.Add(def);
#endif
                }
                else
                {
                    list.Add(def);
                }

            }

            // add providers from plugins (if any)
            if (providerPlugins == null)
            {
                return list;
            }

            foreach (var p in providerPlugins)
            {
                if (p != null)
                {
                    list.AddRange(p.GetProviders(p.GetType()));
                }
            }

            return await Task.FromResult(list);
        }

        public static IDeploymentTaskProvider Create(string taskTypeId, List<IDeploymentTaskProviderPlugin> providerPlugins)
        {
            if (taskTypeId == null)
            {
                return null;
            }

            taskTypeId = taskTypeId.ToLower();

            // find the provider in our current assembly

            var baseAssembly = typeof(DeploymentTaskProviderFactory).Assembly;
            var typeList = baseAssembly.GetTypes().Where(type => type.GetInterfaces().Any(inter => inter == typeof(IDeploymentTaskProvider))).ToList();

            foreach (var t in typeList)
            {
                var def = (DeploymentProviderDefinition)t.GetProperty("Definition").GetValue(null);
                if (def.Id.ToLower() == taskTypeId)
                {
                    return (IDeploymentTaskProvider)Activator.CreateInstance(t);
                }
            }

            if (providerPlugins == null)
            {
                return null;
            }

            // Find provider in our plugins for this type id
            foreach (var p in providerPlugins)
            {
                var pluginType = p.GetType();
                var provider = p.GetProvider(pluginType, taskTypeId);
                if (provider != null)
                {
                    return provider;
                }
            }

            throw new ArgumentException("Deploy Task Type Unknown:" + (taskTypeId ?? "<none>"));
        }
    }
}
