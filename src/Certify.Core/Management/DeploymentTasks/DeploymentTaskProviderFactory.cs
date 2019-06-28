using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.DeploymentTasks;

namespace Certify.Core.Management.DeploymentTasks
{
    public static class DeploymentTaskProviderFactory
    {
        public async static Task<List<DeploymentProviderDefinition>> GetDeploymentTaskProviders()
        {
            return await Task.FromResult(new List<DeploymentProviderDefinition>
            {
              // TODO: load assemblies

            });
        }

        public static IDeploymentTaskProvider Create(string taskTypeId)
        {
            // TODO:  created from reflected type instances

            throw new ArgumentException("Deploy Task Type Unknown:" + (taskTypeId ?? "<none>"));
        }
    }
}
