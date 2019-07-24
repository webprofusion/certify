using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
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
            ManagedCertificate managedCert,
            bool isPreviewOnly = true
            )
        {
            if (TaskProvider != null && TaskConfig != null)
            {
                if (string.IsNullOrEmpty(managedCert.CertificatePath))
                {
                    return new List<ActionResult>{
                        new ActionResult { IsSuccess = false, Message = $"Task Cannot Execute: {TaskProvider.GetDefinition()?.Title } :: there is no current certificate." }
                    };
                }

                if (!System.IO.File.Exists(managedCert.CertificatePath))
                {
                    return new List<ActionResult>{
                        new ActionResult { IsSuccess = false, Message = $"Task Cannot Execute: {TaskProvider.GetDefinition()?.Title } :: source certificate file does not exist." }
                    };
                }

                try
                {
                    return await TaskProvider.Execute(log, managedCert, TaskConfig, _credentials, isPreviewOnly, null);
                }
                catch (Exception exp)
                {
                    return new List<ActionResult>{
                        new ActionResult { IsSuccess = false, Message = $"Task Failed: {TaskProvider.GetDefinition()?.Title } :: {exp?.ToString()}" }
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
