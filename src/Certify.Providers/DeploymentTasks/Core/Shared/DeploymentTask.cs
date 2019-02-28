using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks
{
    public interface IDeploymentTaskProvider
    {
        // DeploymentProviderDefinition Definition { get; } // if static properties in interfaces were a thing
        Task<ActionResult> Execute(ILog log, Models.ManagedCertificate managedCert, bool isPreviewOnly);
        DeploymentProviderDefinition GetDefinition();
    }

    public class DeploymentTaskProviderBase : IDeploymentTaskProvider
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
}
