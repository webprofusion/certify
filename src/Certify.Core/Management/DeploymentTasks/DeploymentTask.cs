using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Management;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks
{

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

        public async Task<List<ActionResult>> Execute(
            ILog log,
            ICredentialsManager credentialsManager,
            object subject,
            CancellationToken cancellationToken,
            DeploymentContext deploymentContext,
            bool isPreviewOnly = true
            )
        {
            if (TaskProvider != null && TaskConfig != null)
            {
                try
                {
                    var execParams = new DeploymentTaskExecutionParams(log, credentialsManager, subject, TaskConfig, _credentials, isPreviewOnly, null, cancellationToken, deploymentContext);

                    return await TaskProvider.Execute(execParams);
                }
                catch (Exception exp)
                {
                    return new List<ActionResult>{
                        new ActionResult { IsSuccess = false, Message = $"{TaskConfig.TaskName} ({TaskProvider.GetDefinition()?.Title }) :: Task Failed with Exception :: {exp?.ToString()}" }
                    };
                }

            }
            else
            {
                return new List<ActionResult>{
                    new ActionResult { IsSuccess = false, Message = "Cannot Execute Deployment Task: TaskProvider or Config not set." }
                    };
            }
        }
    }
}
