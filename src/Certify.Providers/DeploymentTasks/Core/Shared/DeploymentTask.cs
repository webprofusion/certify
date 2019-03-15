using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks
{
    public interface IDeploymentTaskProvider
    {
        Task<ActionResult> Execute(
            ILog log,
            ManagedCertificate managedCert,
            DeploymentTaskConfig settings, 
            Dictionary<string, string> credentials, 
            bool isPreviewOnly
            );

        DeploymentProviderDefinition GetDefinition();
    }

    public class DeploymentTaskProviderBase : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition
        {
            get
            {
                return new DeploymentProviderDefinition { Title = "Deployment Task Definition Not Implemented in Provider" };
            }
        }

        public virtual Task<ActionResult> Execute(
            ILog log, ManagedCertificate managedCert,
            DeploymentTaskConfig settings, 
            Dictionary<string, string> credentials, 
            bool isPreviewOnly) => throw new NotImplementedException();

        public virtual DeploymentProviderDefinition GetDefinition() => Definition;
    }

    public class DeploymentTask
    {
        public DeploymentTask(IDeploymentTaskProvider provider, DeploymentTaskConfig config, Dictionary<string, string> credentials)
        {
            TaskConfig = config;
            TaskProvider = provider;

            _credentials = credentials;
        }

        public IDeploymentTaskProvider TaskProvider { get; set; }

        public DeploymentTaskConfig TaskConfig { get; set; }

        private Dictionary<string, string> _credentials;

        public async Task<ActionResult> Execute(
            ILog log,
            ManagedCertificate managedCert,
            bool isPreviewOnly = true
            )
        {
            if (TaskProvider != null && TaskConfig != null)
            {
                try
                {
                    return await TaskProvider.Execute(log, managedCert, TaskConfig, _credentials, isPreviewOnly);
                }
                catch (Exception exp)
                {
                    return new ActionResult { IsSuccess = false, Message = $"Task Failed: {TaskProvider.GetDefinition()?.Title } :: {exp?.ToString()}"};
                }

            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "Cannot Execute Deployment Task: TaskProvider or Config not set." };
            }
        }
    }
}
