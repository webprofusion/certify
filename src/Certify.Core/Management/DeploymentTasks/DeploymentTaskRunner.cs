using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Providers.DeploymentTasks;
using Microsoft.Extensions.Logging;

namespace Certify.Core.Management.DeploymentTasks
{
    /// <summary>
    /// Runs a list of deployment tasks for a certificate request result, deciding for each task whether it should run
    /// from its trigger, the recorded primary request status and whether a preceding task failed.
    ///
    /// Separate from CertifyManager because the decisions it makes are the ones which determine whether a customer's
    /// deployment actually happened, and they are worth testing without a configured service behind them
    /// </summary>
    internal class DeploymentTaskRunner
    {
        private readonly List<IDeploymentTaskProviderPlugin> _deploymentTaskProviders;
        private readonly ICredentialsManager _credentialsManager;
        private readonly string _powershellExecutionPolicy;
        private readonly TelemetryManager _telemetry;
        private readonly LogLevel _logLevel;

        /// <param name="deploymentTaskProviders">
        /// task provider plugins. Providers built into Certify.Core (including the mock task) are resolved from this
        /// assembly first, so null here still runs those
        /// </param>
        /// <param name="credentialsManager">
        /// used to unlock stored credentials for tasks which reference one. Only required when a task in the list sets
        /// ChallengeCredentialKey
        /// </param>
        /// <param name="powershellExecutionPolicy">execution policy passed to tasks which run powershell</param>
        /// <param name="telemetry">optional, for task completion/failure events</param>
        /// <param name="logLevel">level for the per-item logger created when the caller does not supply a log</param>
        public DeploymentTaskRunner(
            List<IDeploymentTaskProviderPlugin> deploymentTaskProviders,
            ICredentialsManager credentialsManager,
            string powershellExecutionPolicy,
            TelemetryManager telemetry = null,
            LogLevel logLevel = LogLevel.Information)
        {
            _deploymentTaskProviders = deploymentTaskProviders;
            _credentialsManager = credentialsManager;
            _powershellExecutionPolicy = powershellExecutionPolicy;
            _telemetry = telemetry;
            _logLevel = logLevel;
        }

        /// <summary>
        /// Record a task which could not be prepared for execution, so it is reported as a failed task rather than
        /// silently missing from the results. Storing the failure against the task itself is what allows the overall
        /// request status to reflect it, and what lets the deployment retry pass identify the item as needing another
        /// attempt - a task which never ran leaves its previous run status behind and would otherwise look successful
        /// </summary>
        /// <param name="steps"></param>
        /// <param name="taskConfig"></param>
        /// <param name="message"></param>
        /// <param name="log"></param>
        internal static void RecordTaskSetupFailure(List<ActionStep> steps, DeploymentTaskConfig taskConfig, string message, ILog log)
        {
            taskConfig.LastRunStatus = RequestState.Error;
            taskConfig.LastResult = message;

            log?.Error($"Task [{taskConfig.TaskName}] :: {message}");

            steps.Add(new ActionStep
            {
                Key = taskConfig.Id,
                Title = "Task: " + taskConfig.TaskName,
                Category = "Task",
                HasError = true,
                Description = message
            });
        }

        internal static bool ShouldContinueAfterPreviousTaskFailure(TaskTriggerType taskTrigger, bool primaryRequestSucceeded)
        {
            if (taskTrigger == TaskTriggerType.ON_TASK_ERROR)
            {
                return true;
            }

            if (!primaryRequestSucceeded)
            {
                return taskTrigger == TaskTriggerType.ANY_STATUS || taskTrigger == TaskTriggerType.ON_ERROR;
            }

            return false;
        }

        internal static bool ShouldSkipTaskBecausePreviousTaskFailed(bool previousTaskFailed, bool runIfLastStepFailed, TaskTriggerType taskTrigger, bool primaryRequestSucceeded)
        {
            if (!previousTaskFailed || runIfLastStepFailed)
            {
                return false;
            }

            return !ShouldContinueAfterPreviousTaskFailure(taskTrigger, primaryRequestSucceeded);
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
        /// <param name="evaluateAgainstPrimaryRequestStatus">
        /// whether ON_SUCCESS/ON_ERROR triggers are judged against the recorded primary request status. False where no
        /// certificate request took place and the tasks always apply (pre-request tasks, explicit redeployment).
        /// Required, because an inappropriate default would silently run (or skip) the wrong tasks
        /// </param>
        /// <returns></returns>
        internal async Task<List<ActionStep>> Run(ILog log, bool isPreviewOnly, bool skipDeferredTasks, CertificateRequestResult result, IEnumerable<DeploymentTaskConfig> taskList, bool forceTaskExecute, bool evaluateAgainstPrimaryRequestStatus)
        {
            if (taskList == null || !taskList.Any())
            {
                // nothing to do
                return new List<ActionStep>();
            }

            if (log == null)
            {
                log = ManagedCertificateLog.GetLogger(result.ManagedItem.Id, _logLevel);
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

                        var provider = DeploymentTaskProviderFactory.Create(taskConfig.TaskTypeId.ToLower(), _deploymentTaskProviders);

                        Dictionary<string, string> credentials = null;

                        if (!string.IsNullOrEmpty(taskConfig.ChallengeCredentialKey))
                        {
                            credentials = await _credentialsManager.GetUnlockedCredentialsDictionary(taskConfig.ChallengeCredentialKey);

                            if (credentials == null)
                            {
                                // only this task can't run. The credential may be unreadable for a temporary reason
                                // (the credential store being briefly unavailable) as well as a permanent one, so
                                // failing the whole task list here would skip deployment steps which are fine, and would
                                // leave their stored run status describing a previous run
                                RecordTaskSetupFailure(steps, taskConfig, "Failed to decrypt selected credentials for this task.", log);
                                continue;
                            }
                        }

                        var deploymentTask = new DeploymentTask(provider, taskConfig, credentials);

                        deploymentTasks.Add(deploymentTask);
                    }
                    catch (Exception exp)
                    {
                        RecordTaskSetupFailure(steps, taskConfig, "Cannot create task provider for deployment task: " + exp.ToString(), log);
                    }
                }
            }

            ActionStep previousActionStep = null;
            var shouldRunCurrentTask = true;
            var taskTriggerReason = "Task will run for any status";
            var primaryRequestSucceeded = !evaluateAgainstPrimaryRequestStatus || result?.PrimaryRequest?.Status == RequestState.Success;

            foreach (var task in deploymentTasks)
            {
                if (ShouldSkipTaskBecausePreviousTaskFailed(previousActionStep?.HasError == true, task.TaskConfig.RunIfLastStepFailed, task.TaskConfig.TaskTrigger, primaryRequestSucceeded))
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
                        shouldRunCurrentTask = primaryRequestSucceeded;
                        taskTriggerReason = primaryRequestSucceeded
                            ? "Task is enabled and primary request was successful."
                            : "Task is enabled but will not run because primary request unsuccessful.";
                    }
                    else if (task.TaskConfig.TaskTrigger == TaskTriggerType.ON_ERROR)
                    {
                        shouldRunCurrentTask = !primaryRequestSucceeded;
                        taskTriggerReason = primaryRequestSucceeded
                            ? "Task is enabled but will not run because primary request was successful."
                            : "Task is enabled and will run because primary request was unsuccessful.";
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
                        taskTriggerReason = $"Task has been forced to run. Normal status would be [{taskTriggerReason}]";
                    }
                }

                var taskResults = new List<ActionResult>();
                var wasTaskExecuted = false;
                if (shouldRunCurrentTask)
                {
                    log?.Information($"Task [{task.TaskConfig.TaskName}] :: {taskTriggerReason}");
                    task.TaskConfig.DateLastExecuted = DateTimeOffset.UtcNow;

                    wasTaskExecuted = true;
                    taskResults = await task.Execute(log, _credentialsManager, result, new DeploymentContext { PowershellExecutionPolicy = _powershellExecutionPolicy }, isPreviewOnly: isPreviewOnly, cancellationToken: CancellationToken.None);

                    if (!isPreviewOnly)
                    {
                        if (taskResults?.All(t => t.IsSuccess) == true)
                        {
                            _telemetry?.TrackEvent("TaskCompleted", new Dictionary<string, string> {
                            { "TaskType", task.TaskConfig.TaskTypeId  }
                        });
                        }
                        else
                        {
                            failedTasks.Add(task);

                            if (!forceTaskExecute)
                            {
                                _telemetry?.TrackEvent("TaskFailed", new Dictionary<string, string> {
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

    }
}
