using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.DeploymentTasks
{

    public interface IDeploymentTaskProvider
    {
        // DeploymentProviderDefinition Definition { get; } // if static properties in interfaces were a thing
        Task<ActionResult> Execute(ILog log, Models.ManagedCertificate managedCert, bool isPreviewOnly);
        DeploymentProviderDefinition GetDefinition();
    }

    public class DeploymentTaskProviderBase: IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition { get; }

        public virtual Task<ActionResult> Execute(ILog log, ManagedCertificate managedCert, bool isPreviewOnly)
        {
            throw new NotImplementedException();
        }

        public virtual DeploymentProviderDefinition GetDefinition()
        {
            return Definition;
        }
    }

    public class DeploymentTask
    {
        public DeploymentTask(IDeploymentTaskProvider provider, DeploymentTaskConfig config)
        {
            TaskConfig = config;
            TaskProvider = provider;
        }

        public IDeploymentTaskProvider TaskProvider { get; set; }

        public DeploymentTaskConfig TaskConfig { get; set; }

        public async Task<ActionResult> Execute(ILog log, ManagedCertificate managedCert, bool isPreviewOnly = true)
        {
            if (TaskProvider != null && TaskConfig != null)
            {
                try
                {
                    return await TaskProvider.Execute(log, managedCert, isPreviewOnly);
                }
                catch (Exception exp)
                {
                    return new ActionResult { IsSuccess = false, Message = "Task Failed: " + TaskProvider.GetDefinition().Title + " :: " + exp.ToString() };
                }

            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "Cannot Execute Deployment Task: TaskProvider or Config not set." };
            }
        }
    }

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
