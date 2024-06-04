using System;
using System.Collections.Generic;
using System.Linq;
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
            DeploymentContext deploymentContext,
            bool isPreviewOnly,
            CancellationToken cancellationToken
            )
        {
            if (TaskProvider != null && TaskConfig != null)
            {
                try
                {
                    var execParams = new DeploymentTaskExecutionParams(log, credentialsManager, subject, TaskConfig, _credentials, isPreviewOnly, null, deploymentContext, cancellationToken);
                    if (!isPreviewOnly)
                    {
                        return await TaskProvider.Execute(execParams);
                    }
                    else
                    {
                        var validation = await TaskProvider.Validate(execParams);
                        if (validation == null || !validation.Any(r => r.IsSuccess == false))
                        {
                            return new List<ActionResult> { new ActionResult { IsSuccess = true, Message = "Task is valid and ready to execute." } };
                        }
                        else
                        {
                            return validation;
                        }
                    }
                }
                catch (Exception exp)
                {
                    return new List<ActionResult>{
                        new ActionResult { IsSuccess = false, Message = $"{TaskConfig.TaskName} ({TaskProvider.GetDefinition()?.Title }) :: Task Failed with Exception :: {exp}" }
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
