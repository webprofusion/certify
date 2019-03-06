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
                Apache.Definition,
                CentralizedCertificateStore.Definition,
                CertificateExport.Definition,
                Exchange.Definition,
                Nginx.Definition,
                Script.Definition,
                Tomcat.Definition,
                Webhook.Definition,

            });
        }

        public static IDeploymentTaskProvider Create(string taskTypeId)
        {
            // TODO: possibly replace with reflected type instances

            if (taskTypeId.ToLower() == Apache.Definition.Id.ToLower())
            {
                return new Apache();
            }

            if (taskTypeId.ToLower() == CentralizedCertificateStore.Definition.Id.ToLower())
            {
                return new CentralizedCertificateStore();
            }

            if (taskTypeId.ToLower() == CertificateExport.Definition.Id.ToLower())
            {
                return new CertificateExport();
            }

            if (taskTypeId.ToLower() == Exchange.Definition.Id.ToLower())
            {
                return new Exchange();
            }

            if (taskTypeId.ToLower() == Nginx.Definition.Id.ToLower())
            {
                return new Nginx();
            }

            if (taskTypeId.ToLower() == Script.Definition.Id.ToLower())
            {
                return new Script();
            }

            if (taskTypeId.ToLower() == Tomcat.Definition.Id.ToLower())
            {
                return new Tomcat();
            }

            if (taskTypeId.ToLower() == Webhook.Definition.Id.ToLower())
            {
                return new Webhook();
            }

            throw new ArgumentException("Deploy Task Type Unknown:" + (taskTypeId ?? "<none>"));
        }
    }
}
