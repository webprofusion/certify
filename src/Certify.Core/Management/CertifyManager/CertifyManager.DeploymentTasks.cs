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

            var taskList = managedCert.PostRequestTasks.AsEnumerable();


            // if task id provided, determine if task is from pre-request task list or post-request task list
            if (taskId != null)
            {
                if (managedCert.PreRequestTasks?.Any(t => t.Id == taskId) == true)
                {
                    taskList = managedCert.PreRequestTasks.Where(t => t.Id == taskId);
                }
                else
                {
                    taskList = managedCert.PostRequestTasks.Where(t => t.Id == taskId);
                }

            }

            if (taskList == null || !taskList.Any())
            {
                return new List<ActionStep> { new ActionStep { HasError = false, Description = "No matching tasks to perform." } };
            }

            return await PerformTaskList(log, isPreviewOnly, skipDeferredTasks, new CertificateRequestResult { ManagedItem= managedCert, IsSuccess=true }, taskList);
        }

        private async Task<List<ActionStep>> PerformTaskList(ILog log, bool isPreviewOnly, bool skipDeferredTasks, CertificateRequestResult result, IEnumerable<DeploymentTaskConfig> taskList)
        {
            if (taskList == null || !taskList.Any())
            {
                // nothing to do
                return new List<ActionStep>();
            }

            if (log == null)
            {
                log = ManagedCertificateLog.GetLogger(result.ManagedItem.Id, _loggingLevelSwitch);
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
                        steps.Add(new ActionStep { HasError = true, Title = "Task: " + taskConfig.TaskName, Description = "Cannot create task provider for deployment task: " + exp.ToString() });
                    }
                }
            }

            ActionStep previousActionStep = null;
            bool shouldRunCurrentTask = true;
            string taskTriggerReason = "Task will run for any status";

            foreach (var task in deploymentTasks)
            {
                if (task.TaskConfig.TaskTrigger == TaskTriggerType.ANY_STATUS)
                {
                    shouldRunCurrentTask = true;
                    taskTriggerReason = "Task will run for any status";
                }
                else if (task.TaskConfig.TaskTrigger == TaskTriggerType.NOT_ENABLED)
                {
                    shouldRunCurrentTask = false;
                    taskTriggerReason = "Task is not enabled and will be skipped.";
                }
                else if (task.TaskConfig.TaskTrigger == TaskTriggerType.ON_SUCCESS)
                {
                    if (previousActionStep == null || previousActionStep.HasError == false)
                    {
                        shouldRunCurrentTask = true;
                        taskTriggerReason = "Task is enabled and previous step was successful.";
                    }
                    else if (previousActionStep.HasError == true)
                    {
                        shouldRunCurrentTask = false;
                        taskTriggerReason = "Task is enabled but will not run because previous step was unsuccessful.";
                    }

                }
                else if (task.TaskConfig.TaskTrigger == TaskTriggerType.ON_ERROR)
                {
                    if (previousActionStep == null || previousActionStep.HasError == false)
                    {
                        shouldRunCurrentTask = false;
                        taskTriggerReason = "Task is enabled but will not run because previous step was successful.";
                    }
                    else if (previousActionStep.HasError == true)
                    {
                        shouldRunCurrentTask = true;
                        taskTriggerReason = "Task is enabled and will run because previous step was unsuccessful.";
                    }

                }

                if (task.TaskConfig.IsDeferred && skipDeferredTasks)
                {
                    shouldRunCurrentTask = false;
                    taskTriggerReason = "Task is enabled but will not run because execution is deferred.";
                }


                List<ActionResult> taskResults = new List<ActionResult>();

                if (shouldRunCurrentTask)
                {
                    taskResults = await task.Execute(log, result, isPreviewOnly: isPreviewOnly);
                }
                else
                {
                    taskResults.Add(new ActionResult(taskTriggerReason, true));
                }

                var subSteps = new List<ActionStep>();

                var stepIndex = 1;

                foreach (var r in taskResults)
                {
                    subSteps.Add(new ActionStep
                    {
                        HasError = !r.IsSuccess,
                        Description = r.Message,
                        Title = $"Task Step {stepIndex} of {task.TaskConfig.TaskName}",
                        Key = task.TaskConfig.Id + "_" + stepIndex,
                        Category = "Task Step"
                    });


                    if (r.IsSuccess)
                    {
                        log?.Information(r.Message);
                    }
                    else
                    {
                        log?.Error(r.Message);
                    }

                    stepIndex++;
                }

                var overallTaskResult = (taskResults != null && taskResults.Any(t => t.IsSuccess == false) ? taskResults.First(t => t.IsSuccess == false).Message : taskTriggerReason);

                var currentStep = new ActionStep
                {
                    Key = task.TaskConfig.Id,
                    Title = task.TaskConfig.TaskName,
                    Category = "Task",
                    HasError = (taskResults != null && taskResults.Any(t => t.IsSuccess == false) ? true : false),
                    Description = overallTaskResult,
                    HasWarning = !shouldRunCurrentTask,
                    Substeps = subSteps
                };

                steps.Add(currentStep);

                previousActionStep = currentStep;
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

            try
            {
                var validationResult = await provider.Validate(managedCertificate, taskConfig, credentials, provider.GetDefinition());
                return validationResult;
            }
            catch (Exception exp)
            {
                return new List<ActionResult> { new ActionResult("Failed to validate task: " + exp.ToString(), false) };
            }
        }

        /// <summary>
        /// Convert legacy pre/post request scripts, webhooks and deployments to Pre/Post Deployment Tasks
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        public Tuple<ManagedCertificate, bool> MigrateDeploymentTasks(ManagedCertificate managedCertificate)
        {
            bool requiredMigration = false;

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
                    TaskTypeId = Certify.Providers.DeploymentTasks.Core.PowershellScript.Definition.Id,
                    ChallengeProvider = StandardAuthTypes.STANDARD_AUTH_LOCAL,
                    TaskName = "[Pre-Request Script]",
                    IsFatalOnError = true,

                    Parameters = new List<ProviderParameterSetting> {
                            new ProviderParameterSetting("scriptpath", managedCertificate.RequestConfig.PreRequestPowerShellScript),
                            new ProviderParameterSetting("inputresult","true")
                        }
                };

                if (!managedCertificate.PreRequestTasks.Any(t => t.TaskName == "[Pre-Request Script]"))
                {
                    managedCertificate.PreRequestTasks.Insert(0, task);
                    requiredMigration = true;
                }

                managedCertificate.RequestConfig.PreRequestPowerShellScript = null;
            }

            if (!string.IsNullOrEmpty(managedCertificate.RequestConfig.PostRequestPowerShellScript))
            {
                if (managedCertificate.PostRequestTasks == null)
                {
                    managedCertificate.PostRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
                }

                //add post-request script task
                var task = new DeploymentTaskConfig
                {
                    Id = Guid.NewGuid().ToString(),
                    TaskTypeId = Certify.Providers.DeploymentTasks.Core.PowershellScript.Definition.Id,
                    ChallengeProvider = StandardAuthTypes.STANDARD_AUTH_LOCAL,
                    TaskName = "[Post-Request Script]",
                    IsFatalOnError = true,
                    TaskTrigger = TaskTriggerType.ON_SUCCESS,
                    Parameters = new List<ProviderParameterSetting> {
                            new ProviderParameterSetting("scriptpath", managedCertificate.RequestConfig.PostRequestPowerShellScript),
                            new ProviderParameterSetting("inputresult","true")
                        }
                };

                if (!managedCertificate.PostRequestTasks.Any(t => t.TaskName == "[Post-Request Script]"))
                {
                    managedCertificate.PostRequestTasks.Insert(0, task);
                    requiredMigration = true;
                }

                managedCertificate.RequestConfig.PostRequestPowerShellScript = null;
            }

            if (!string.IsNullOrEmpty(managedCertificate.RequestConfig.WebhookUrl))
            {
                if (managedCertificate.PostRequestTasks == null)
                {
                    managedCertificate.PostRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
                }

                //add post-request script task for webhook, migrate trigger type to task trigger type

                var triggerType = TaskTriggerType.ANY_STATUS;

                if (managedCertificate.RequestConfig.WebhookTrigger == Webhook.ON_NONE)
                {
                    triggerType = TaskTriggerType.NOT_ENABLED;
                }
                else if (managedCertificate.RequestConfig.WebhookTrigger == Webhook.ON_SUCCESS)
                {
                    triggerType = TaskTriggerType.ON_SUCCESS;
                }
                else if (managedCertificate.RequestConfig.WebhookTrigger == Webhook.ON_ERROR)
                {
                    triggerType = TaskTriggerType.ON_ERROR;
                }
                else if (managedCertificate.RequestConfig.WebhookTrigger == Webhook.ON_SUCCESS_OR_ERROR)
                {
                    triggerType = TaskTriggerType.ANY_STATUS;
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

                if (!managedCertificate.PostRequestTasks.Any(t => t.TaskName == "[Post-Request Webhook]"))
                {
                    managedCertificate.PostRequestTasks.Insert(0, task);
                    requiredMigration = true;
                }

                managedCertificate.RequestConfig.WebhookUrl = null;
                managedCertificate.RequestConfig.WebhookTrigger = Webhook.ON_NONE;

            }

            return new Tuple<ManagedCertificate, bool>(managedCertificate, requiredMigration);
        }
    }
}
