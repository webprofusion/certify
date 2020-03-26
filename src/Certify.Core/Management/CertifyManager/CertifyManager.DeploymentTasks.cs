using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Core.Management.DeploymentTasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.DeploymentTasks;
using Certify.Shared.Utils;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        public async Task<List<DeploymentProviderDefinition>> GetDeploymentProviders()
        {
            return await Core.Management.DeploymentTasks.DeploymentTaskProviderFactory.GetDeploymentTaskProviders(_pluginManager.DeploymentTaskProviders);
        }

        public async Task<List<ActionStep>> PerformDeploymentTask(ILog log, string managedCertificateId, string taskId, bool isPreviewOnly, bool skipDeferredTasks)
        {
            var managedCert = await GetManagedCertificate(managedCertificateId);

            if (managedCert == null)
            {
                var steps = new List<ActionStep>();
                steps.Add(new ActionStep { HasError = true, Title = "Deployment", Description = "Managed certificate not found. Could not deploy." });
                return steps;
            }

            var taskList = managedCert.DeploymentTasks?.Where(t => string.IsNullOrEmpty(taskId) || taskId == t.Id);

            if (taskList == null || !taskList.Any())
            {
                return new List<ActionStep> { new ActionStep { HasError = false, Description = "No matching tasks to perform." } };
            }

            return await PerformTaskList(log, isPreviewOnly, skipDeferredTasks, managedCert, taskList);
        }

        private async Task<List<ActionStep>> PerformTaskList(ILog log, bool isPreviewOnly, bool skipDeferredTasks, ManagedCertificate managedCert, IEnumerable<DeploymentTaskConfig> taskList)
        {

            if (log == null)
            {
                log = ManagedCertificateLog.GetLogger(managedCert.Id, _loggingLevelSwitch);
            }

            // perform or preview each task

            var deploymentTasks = new List<DeploymentTask>();
            var steps = new List<ActionStep>();

            foreach (var taskConfig in taskList)
            {
                // add task to execution list unless the task is deferred and we are currently skipping deferred tasks

                if (!taskConfig.IsDeferred || (taskConfig.IsDeferred && !skipDeferredTasks))
                {
                    try
                    {

                        var provider = DeploymentTaskProviderFactory.Create(taskConfig.TaskTypeId.ToLower(), _pluginManager.DeploymentTaskProviders);

                        Dictionary<string, string> credentials = null;

                        if (!string.IsNullOrEmpty(taskConfig.ChallengeCredentialKey))
                        {
                            credentials = await _credentialsManager.GetUnlockedCredentialsDictionary(taskConfig.ChallengeCredentialKey);
                        }

                        var deploymentTask = new DeploymentTask(provider, taskConfig, credentials);

                        deploymentTasks.Add(deploymentTask);
                    }
                    catch (Exception exp)
                    {
                        steps.Add(new ActionStep { HasError = true, Title = "Deployment Task: " + taskConfig.TaskName, Description = "Cannot create task provider for deployment task: " + exp.ToString() });
                    }
                }
            }

            foreach (var task in deploymentTasks)
            {
                var results = await task.Execute(log, managedCert, isPreviewOnly: isPreviewOnly);

                foreach (var r in results)
                {
                    steps.Add(new ActionStep { HasError = !r.IsSuccess, Description = r.Message });
                }

            }

            return steps;
        }

        public async Task<List<ActionResult>> ValidateDeploymentTask(ManagedCertificate managedCertificate, DeploymentTaskConfig taskConfig)
        {
            var credentialsManager = new CredentialsManager();
            var provider = DeploymentTaskProviderFactory.Create(taskConfig.TaskTypeId.ToLower(), _pluginManager.DeploymentTaskProviders);

            Dictionary<string, string> credentials = null;

            if (!string.IsNullOrEmpty(taskConfig.ChallengeCredentialKey))
            {
                credentials = await credentialsManager.GetUnlockedCredentialsDictionary(taskConfig.ChallengeCredentialKey);
            }

            return await provider.Validate(managedCertificate, taskConfig, credentials, provider.GetDefinition());
        }

        /// <summary>
        /// Convert legacy pre/post request scripts, webhooks and deployments to Pre/Post Deployment Tasks
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        public ManagedCertificate MigrateDeploymentTasks(ManagedCertificate managedCertificate)
        {
            if (!string.IsNullOrEmpty(managedCertificate.RequestConfig.PreRequestPowerShellScript))
            {
                if (managedCertificate.PreRequestTasks == null)
                {
                    managedCertificate.PreRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
                }

                //add pre-request script task
                var task = new DeploymentTaskConfig
                {
                    Id = Guid.NewGuid().ToString(),
                    ChallengeProvider = Certify.Providers.DeploymentTasks.Core.PowershellScript.Definition.Id,
                    TaskName = "[Pre-Request Script]",
                    IsFatalOnError = true,
                    TaskTypeId = Certify.Providers.DeploymentTasks.Core.PowershellScript.Definition.Id,
                    Parameters = new List<ProviderParameterSetting> {
                            new ProviderParameterSetting("path", managedCertificate.RequestConfig.PreRequestPowerShellScript),
                            new ProviderParameterSetting("inputresult","true")
                        }
                };

                if (!managedCertificate.PreRequestTasks.Any(t => t.TaskName == "[Pre-Request Script]"))
                {
                    managedCertificate.PreRequestTasks.Insert(0, task);
                }

                managedCertificate.RequestConfig.PreRequestPowerShellScript = null;
            }

            if (!string.IsNullOrEmpty(managedCertificate.RequestConfig.PostRequestPowerShellScript))
            {
                if (managedCertificate.DeploymentTasks == null)
                {
                    managedCertificate.DeploymentTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
                }

                //add post-request script task
                var task = new DeploymentTaskConfig
                {
                    Id = Guid.NewGuid().ToString(),
                    ChallengeProvider = Certify.Providers.DeploymentTasks.Core.PowershellScript.Definition.Id,
                    TaskName = "[Post-Request Script]",
                    IsFatalOnError = true,
                    TaskTrigger = TaskTriggerType.ON_SUCCESS,
                    TaskTypeId = Certify.Providers.DeploymentTasks.Core.PowershellScript.Definition.Id,
                    Parameters = new List<ProviderParameterSetting> {
                            new ProviderParameterSetting("scriptpath", managedCertificate.RequestConfig.PreRequestPowerShellScript),
                            new ProviderParameterSetting("inputresult","true")
                        }
                };

                if (!managedCertificate.DeploymentTasks.Any(t => t.TaskName == "[Post-Request Script]"))
                {
                    managedCertificate.DeploymentTasks.Insert(0, task);
                }

                managedCertificate.RequestConfig.PostRequestPowerShellScript = null;
            }

            if (!string.IsNullOrEmpty(managedCertificate.RequestConfig.WebhookUrl))
            {
                if (managedCertificate.DeploymentTasks == null)
                {
                    managedCertificate.DeploymentTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
                }

                //add post-request script task for webhook, migrate trigger type to task trigger type

                var triggerType = TaskTriggerType.ALL;
                if (managedCertificate.RequestConfig.WebhookTrigger == Webhook.ON_SUCCESS)
                {
                    triggerType = TaskTriggerType.ON_SUCCESS;
                }
                else if (managedCertificate.RequestConfig.WebhookTrigger == Webhook.ON_ERROR)
                {
                    triggerType = TaskTriggerType.ON_ERROR;
                }

                var task = new DeploymentTaskConfig
                {
                    Id = Guid.NewGuid().ToString(),
                    ChallengeProvider = Certify.Providers.DeploymentTasks.Core.Webhook.Definition.Id,
                    TaskName = "[Post-Request Webhook]",
                    IsFatalOnError = false,
                    TaskTrigger = triggerType,
                    TaskTypeId = Certify.Providers.DeploymentTasks.Core.Webhook.Definition.Id,
                    Parameters = new List<ProviderParameterSetting> {
                            new ProviderParameterSetting("url", managedCertificate.RequestConfig.WebhookUrl),
                            new ProviderParameterSetting("method", managedCertificate.RequestConfig.WebhookMethod),
                            new ProviderParameterSetting("contenttype", managedCertificate.RequestConfig.WebhookContentType),
                            new ProviderParameterSetting("contentbody", managedCertificate.RequestConfig.WebhookContentBody)
                        }
                };

                if (!managedCertificate.DeploymentTasks.Any(t => t.TaskName == "[Post-Request Webhook]"))
                {
                    managedCertificate.DeploymentTasks.Insert(0, task);
                }

                managedCertificate.RequestConfig.WebhookUrl = null;
            }

            return managedCertificate;
        }
    }
}
