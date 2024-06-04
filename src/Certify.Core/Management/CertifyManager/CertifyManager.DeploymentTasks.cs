using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        /// <summary>
        /// Get list of deployment task providers (from plugins)
        /// </summary>
        /// <returns></returns>
        public async Task<List<DeploymentProviderDefinition>> GetDeploymentProviders()
        {
            return await Core.Management.DeploymentTasks.DeploymentTaskProviderFactory.GetDeploymentTaskProviders(_pluginManager.DeploymentTaskProviders);
        }

        /// <summary>
        /// Get the current definition for a provider including dynamic elements affected by the given config
        /// </summary>
        /// <param name="id"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public async Task<DeploymentProviderDefinition> GetDeploymentProviderDefinition(string id, DeploymentTaskConfig config = null)
        {
            var provider = DeploymentTaskProviderFactory.Create(id, _pluginManager.DeploymentTaskProviders);
            return await Task.FromResult(provider?.GetDefinition());
        }

        /// <summary>
        /// Perform a specific deployment task for the given managed certificate
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCertificateId"></param>
        /// <param name="taskId"></param>
        /// <param name="isPreviewOnly"></param>
        /// <param name="skipDeferredTasks"></param>
        /// <param name="forceTaskExecution"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> PerformDeploymentTask(ILog log, string managedCertificateId, string taskId, bool isPreviewOnly, bool skipDeferredTasks, bool forceTaskExecution)
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
                else if (managedCert.PostRequestTasks?.Any(t => t.Id == taskId) == true)
                {
                    taskList = managedCert.PostRequestTasks.Where(t => t.Id == taskId);
                }
            }

            if (taskList == null || !taskList.Any())
            {
                return new List<ActionStep> { new ActionStep { HasError = false, Description = "No matching tasks to perform." } };
            }

            var msg = "[Multiple Tasks]";

            if (taskList.Count() == 1)
            {
                msg = taskList.First().TaskName;
            }

            LogMessage(managedCert.Id, $"---- Performing Task [On-Demand or Manual Execution] :: {msg} ----");

            var result = await PerformTaskList(log, isPreviewOnly, skipDeferredTasks, new CertificateRequestResult(managedCert, isSuccess: managedCert.LastRenewalStatus == RequestState.Success ? true : false, ""), taskList, forceTaskExecution);

            await UpdateManagedCertificate(managedCert);

            return result;
        }

        /// <summary>
        /// Perform a set of deployment tasks based on the given certificate request result (managed certificate + status information)
        /// </summary>
        /// <param name="log"></param>
        /// <param name="isPreviewOnly"></param>
        /// <param name="skipDeferredTasks"></param>
        /// <param name="result"></param>
        /// <param name="taskList"></param>
        /// <param name="forceTaskExecute"></param>
        /// <returns></returns>
        private async Task<List<ActionStep>> PerformTaskList(ILog log, bool isPreviewOnly, bool skipDeferredTasks, CertificateRequestResult result, IEnumerable<DeploymentTaskConfig> taskList, bool forceTaskExecute = false)
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

            var failedTasks = new List<DeploymentTask>();

            foreach (var taskConfig in taskList)
            {
                // add task to execution list unless the task is deferred/manual and we are currently skipping deferred tasks

                if (taskConfig.TaskTrigger != TaskTriggerType.MANUAL || (taskConfig.TaskTrigger == TaskTriggerType.MANUAL && !skipDeferredTasks))
                {
                    try
                    {

                        var provider = DeploymentTaskProviderFactory.Create(taskConfig.TaskTypeId.ToLower(), _pluginManager.DeploymentTaskProviders);

                        Dictionary<string, string> credentials = null;

                        if (!string.IsNullOrEmpty(taskConfig.ChallengeCredentialKey))
                        {
                            credentials = await _credentialsManager.GetUnlockedCredentialsDictionary(taskConfig.ChallengeCredentialKey);

                            if (credentials == null)
                            {
                                return new List<ActionStep> { new ActionStep { HasError = true, Title = taskConfig.TaskName, Description = "Failed to decrypt selected credentials for this task." } };
                            }
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
            var shouldRunCurrentTask = true;
            var taskTriggerReason = "Task will run for any status";

            foreach (var task in deploymentTasks)
            {
                if (previousActionStep != null && (previousActionStep.HasError && !task.TaskConfig.RunIfLastStepFailed))
                {
                    shouldRunCurrentTask = false;
                    taskTriggerReason = "Task will not run because previous task failed.";
                }
                else
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
                        if (result != null && (!result.Abort && result.IsSuccess))
                        {
                            shouldRunCurrentTask = true;
                            taskTriggerReason = "Task is enabled and primary request was successful.";
                        }
                        else
                        {
                            shouldRunCurrentTask = false;
                            taskTriggerReason = "Task is enabled but will not run because primary request unsuccessful.";
                        }
                    }
                    else if (task.TaskConfig.TaskTrigger == TaskTriggerType.ON_ERROR)
                    {
                        if (result != null && (!result.Abort && result.IsSuccess))
                        {
                            shouldRunCurrentTask = false;
                            taskTriggerReason = "Task is enabled but will not run because primary request was successful.";
                        }
                        else
                        {
                            shouldRunCurrentTask = true;
                            taskTriggerReason = "Task is enabled and will run because primary request was unsuccessful.";
                        }
                    }
                    else if (task.TaskConfig.TaskTrigger == TaskTriggerType.ON_TASK_ERROR)
                    {
                        if (!failedTasks.Any())
                        {
                            shouldRunCurrentTask = false;
                            taskTriggerReason = "Task is enabled but will not run because preceding tasks were successful.";
                        }
                        else
                        {
                            shouldRunCurrentTask = true;
                            taskTriggerReason = "Task is enabled and will run because a preceding task was unsuccessful.";
                        }
                    }
                    else if (task.TaskConfig.TaskTrigger == TaskTriggerType.MANUAL)
                    {
                        if (skipDeferredTasks)
                        {
                            shouldRunCurrentTask = false;
                            taskTriggerReason = "Task is enabled but will not run because execution is deferred/manual.";
                        }
                        else
                        {
                            shouldRunCurrentTask = true;
                            taskTriggerReason = "Task is enabled and will run because deferred/manual tasks are not being skipped.";
                        }
                    }
                }

                if (forceTaskExecute == true)
                {
                    if (!shouldRunCurrentTask)
                    {
                        shouldRunCurrentTask = true;
                        taskTriggerReason = $"Task is being has been forced to run. Normal status would be [{taskTriggerReason}]";
                    }
                }

                var taskResults = new List<ActionResult>();
                var wasTaskExecuted = false;
                if (shouldRunCurrentTask)
                {
                    log?.Information($"Task [{task.TaskConfig.TaskName}] :: {taskTriggerReason}");
                    task.TaskConfig.DateLastExecuted = DateTimeOffset.UtcNow;

                    wasTaskExecuted = true;
                    taskResults = await task.Execute(log, _credentialsManager, result, cancellationToken: CancellationToken.None, new DeploymentContext { PowershellExecutionPolicy = _serverConfig.PowershellExecutionPolicy }, isPreviewOnly: isPreviewOnly);

                    if (!isPreviewOnly)
                    {
                        if (taskResults?.All(t => t.IsSuccess) == true)
                        {
                            _tc?.TrackEvent("TaskCompleted", new Dictionary<string, string> {
                            { "TaskType", task.TaskConfig.TaskTypeId  }
                        });
                        }
                        else
                        {
                            failedTasks.Add(task);

                            if (!forceTaskExecute)
                            {
                                _tc?.TrackEvent("TaskFailed", new Dictionary<string, string> {
                                { "TaskType", task.TaskConfig.TaskTypeId  }
                             });
                            }
                        }
                    }
                }
                else
                {
                    taskResults.Add(new ActionResult($"Task [{task.TaskConfig.TaskName}] :: {taskTriggerReason}", true));

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

                var overallTaskResult = "Unknown";

                if (taskResults != null && taskResults.Any(t => t.IsSuccess == false))
                {
                    overallTaskResult = taskResults.First(t => t.IsSuccess == false).Message;
                }
                else
                {
                    if (isPreviewOnly)
                    {
                        overallTaskResult = taskTriggerReason;
                    }
                    else
                    {
                        if (shouldRunCurrentTask)
                        {
                            overallTaskResult = "Task Completed OK";
                        }
                        else
                        {
                            overallTaskResult = taskTriggerReason;
                        }
                    }
                }

                var hasError = (taskResults != null && taskResults.Any(t => t.IsSuccess == false) ? true : false);

                var currentStep = new ActionStep
                {
                    Key = task.TaskConfig.Id,
                    Title = task.TaskConfig.TaskName,
                    Category = "Task",
                    HasError = hasError,
                    Description = overallTaskResult,
                    HasWarning = !shouldRunCurrentTask,
                    Substeps = subSteps
                };

                // task either has an error, was successful or was skipped
                if (hasError)
                {
                    task.TaskConfig.LastRunStatus = RequestState.Error;
                }
                else if (wasTaskExecuted)
                {
                    task.TaskConfig.LastRunStatus = RequestState.Success;
                }
                else
                {
                    task.TaskConfig.LastRunStatus = RequestState.Skipped;
                }

                task.TaskConfig.LastResult = overallTaskResult;

                steps.Add(currentStep);

                previousActionStep = currentStep;
            }

            return steps;
        }

        /// <summary>
        /// Perform validation for a specific deployment task configuration
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="taskConfig"></param>
        /// <returns></returns>
        public async Task<List<ActionResult>> ValidateDeploymentTask(ManagedCertificate managedCertificate, DeploymentTaskConfig taskConfig)
        {

            var provider = DeploymentTaskProviderFactory.Create(taskConfig.TaskTypeId.ToLower(), _pluginManager.DeploymentTaskProviders);

            Dictionary<string, string> credentials = null;

            if (!string.IsNullOrEmpty(taskConfig.ChallengeCredentialKey))
            {
                credentials = await _credentialsManager.GetUnlockedCredentialsDictionary(taskConfig.ChallengeCredentialKey);
            }

            try
            {
                var execParams = new DeploymentTaskExecutionParams(null, _credentialsManager, managedCertificate, taskConfig, credentials, true, provider?.GetDefinition(), new DeploymentContext { PowershellExecutionPolicy = _serverConfig.PowershellExecutionPolicy }, CancellationToken.None);
                var validationResult = await provider.Validate(execParams);
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
            var requiredMigration = false;

            if (managedCertificate.PreRequestTasks == null)
            {
                managedCertificate.PreRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
            }

            if (managedCertificate.PostRequestTasks == null)
            {
                managedCertificate.PostRequestTasks = new System.Collections.ObjectModel.ObservableCollection<DeploymentTaskConfig>();
            }

            if (!string.IsNullOrEmpty(managedCertificate.RequestConfig.PreRequestPowerShellScript))
            {

                //add pre-request script task
                var task = new DeploymentTaskConfig
                {
                    Id = Guid.NewGuid().ToString(),
                    TaskTypeId = StandardTaskTypes.POWERSHELL,
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

                //add post-request script task
                var task = new DeploymentTaskConfig
                {
                    Id = Guid.NewGuid().ToString(),
                    TaskTypeId = StandardTaskTypes.POWERSHELL,
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
                    ChallengeProvider = StandardAuthTypes.STANDARD_AUTH_LOCAL,
                    TaskName = "[Post-Request Webhook]",
                    IsFatalOnError = false,
                    TaskTrigger = triggerType,
                    TaskTypeId = StandardTaskTypes.WEBHOOK,
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

            // #516 check for any post-request webhooks incorrectly set to be powershell

            if (managedCertificate.PostRequestTasks?.Any(t => t.TaskTypeId == StandardTaskTypes.POWERSHELL && t.Parameters?.Any(p => p.Key == "url") == true) == true)
            {
                var webhookTask = managedCertificate.PostRequestTasks.First(t => t.TaskTypeId == StandardTaskTypes.POWERSHELL && t.Parameters?.Any(p => p.Key == "url") == true);
                if (webhookTask != null)
                {
                    webhookTask.TaskTypeId = StandardTaskTypes.WEBHOOK;
                    requiredMigration = true;
                }
            }

            return new Tuple<ManagedCertificate, bool>(managedCertificate, requiredMigration);
        }

        public async Task<ActionResult> TestCredentials(string storageKey)
        {
            // create instance of provider type then test credentials
            try
            {
                var storedCredential = await _credentialsManager.GetCredential(storageKey);
                if (storedCredential == null)
                {
                    return new ActionResult { IsSuccess = false, Message = "No credentials found." };
                }

                var credentials = await _credentialsManager.GetUnlockedCredentialsDictionary(storedCredential.StorageKey);

                if (credentials == null)
                {
                    return new ActionResult { IsSuccess = false, Message = "Failed to retrieve decrypted credentials." };
                }

                if (storedCredential.ProviderType.StartsWith("DNS"))
                {
                    try
                    {
                        var dnsProvider = await Core.Management.Challenges.ChallengeProviders.GetDnsProvider(storedCredential.ProviderType, credentials, new Dictionary<string, string> { });

                        if (dnsProvider == null)
                        {
                            return new ActionResult { IsSuccess = false, Message = "Could not create DNS provider API. Invalid or unrecognised." };
                        }

                        return await dnsProvider.Test();
                    }
                    catch (Exception exp)
                    {
                        return new ActionResult { IsSuccess = false, Message = "Failed to init DNS Provider " + storedCredential.ProviderType + " :: " + exp.Message };
                    }
                }

                return new ActionResult { IsSuccess = true, Message = "No test available." };
            }
            catch (Exception ex)
            {
                return new ActionResult($"Failed to test credential: {ex.Message}", false);
            }
        }
    }
}
