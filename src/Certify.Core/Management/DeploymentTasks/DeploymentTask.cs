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
        public static List<DeploymentProviderDefinition> GetDeploymentTaskProviders()
        {
            return new List<DeploymentProviderDefinition>
            {
                Providers.DeploymentTasks.Apache.Definition,
                Providers.DeploymentTasks.CentralizedCertificateStore.Definition,
                Providers.DeploymentTasks.CertificateExport.Definition,
                Providers.DeploymentTasks.Exchange.Definition,
                Providers.DeploymentTasks.Nginx.Definition,
                Providers.DeploymentTasks.Webhook.Definition,
                Providers.DeploymentTasks.Tomcat.Definition,
                Providers.DeploymentTasks.Webhook.Definition
            };
        }

        public static IDeploymentTaskProvider Create(string taskTypeId)
        {
            // TODO: possibly replace with reflected type instances

            if (taskTypeId.ToLower() == Providers.DeploymentTasks.CentralizedCertificateStore.Definition.Id.ToLower())
            {
                return new Providers.DeploymentTasks.CentralizedCertificateStore();
            }

            if (taskTypeId.ToLower() == Providers.DeploymentTasks.CertificateExport.Definition.Id.ToLower())
            {
                return new Providers.DeploymentTasks.CertificateExport();
            }
            throw new ArgumentException("Deploy Task Type Unknown:" + (taskTypeId ?? "<none>"));
        }
    }
}
