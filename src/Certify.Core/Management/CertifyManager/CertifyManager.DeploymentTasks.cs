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
using Certify.Models.Utils;
using Certify.Providers.DeploymentTasks;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        /// <summary>
        /// The maximum number of items a single deployment retry pass will re-attempt
        /// </summary>
        private const int MAX_DEPLOYMENT_RETRY_TASKS = 50;

        /// <summary>
        /// The number of items read per query while looking for items which require a deployment retry
        /// </summary>
        private const int DEPLOYMENT_RETRY_SCAN_PAGE_SIZE = 75;

        /// <summary>
        /// Minimum time left between deployment retry attempts while an item is still within its initial attempts, so a
        /// retry does not immediately follow the renewal attempt which has just failed to deploy
        /// </summary>
        private static readonly TimeSpan _minDeploymentRetryInterval = TimeSpan.FromMinutes(5);

        private int _isDeploymentRetryInProgress = 0;

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

            if (!string.IsNullOrWhiteSpace(taskId))
            {
                forceTaskExecution = true;
            }

            if (taskList == null || !taskList.Any())
            {
                if (!string.IsNullOrWhiteSpace(taskId))
                {
                    return new List<ActionStep> { new ActionStep { HasError = true, Description = $"Task Id {taskId} not present so cannot run. Check confgiuration has been saved before running task." } };
                }
                else
                {
                    return new List<ActionStep> { new ActionStep { HasError = false, Description = "No matching tasks to perform." } };
                }
            }

            var msg = "[Multiple Tasks]";

            if (taskList.Count() == 1)
            {
                msg = taskList.First().TaskName;
            }

            LogMessage(managedCert.Id, $"---- Performing Task [On-Demand or Manual Execution] :: {msg} ----");

            // an individually executed task deploys the certificate we currently hold, so it counts as a successful
            // primary request whenever that certificate is usable, even if the last request did not fetch a new one
            var manualTaskPrimaryRequestSucceeded = WasLastCertificatePrimaryRequestSuccessful(managedCert);
            var result = await PerformTaskList(
                log,
                isPreviewOnly,
                skipDeferredTasks,
                new CertificateRequestResult(managedCert, isSuccess: manualTaskPrimaryRequestSucceeded, "")
                {
                    PrimaryRequest = new RequestStageStatus
                    {
                        Status = manualTaskPrimaryRequestSucceeded ? RequestState.Success : RequestState.Error
                    }
                },
                taskList,
                forceTaskExecution,
                evaluateAgainstPrimaryRequestStatus: true
            );

            // when recomputing the overall stored status, the explicitly recorded primary request status is
            // authoritative (a failed renewal must stay failed even if an older usable certificate allowed the
            // manual task run to proceed); the certificate-availability fallback only applies when no status was recorded
            var recordedPrimaryRequestStatus = ResolveRecordedPrimaryRequestStatus(managedCert);

            var primaryRequestResult = new CertificateRequestResult(managedCert, isSuccess: recordedPrimaryRequestStatus == RequestState.Success, string.Empty)
            {
                PrimaryRequest = new RequestStageStatus
                {
                    Status = recordedPrimaryRequestStatus,
                    Message = managedCert.LastPrimaryRequest?.Message
                },
                Message = managedCert.LastPrimaryRequest?.Message ?? string.Empty
            };

            var finalState = ResolveOverallRenewalStatus(managedCert, primaryRequestResult, postRequestTasksRan: managedCert.PostRequestTasks?.Any() == true);
            var finalMessage = ResolveOverallRenewalMessage(managedCert, primaryRequestResult, finalState, postRequestTasksRan: managedCert.PostRequestTasks?.Any() == true);

            var statusChanged = managedCert.LastRenewalStatus != finalState
                || !string.Equals(managedCert.RenewalFailureMessage, finalMessage, StringComparison.Ordinal);

            if (statusChanged)
            {
                await UpdateManagedCertificateStatus(
                    managedCert,
                    finalState,
                    finalMessage,
                    incrementFailureCount: false,
                    updateLastAttempt: false);
            }
            else
            {
                await UpdateManagedCertificate(managedCert);
            }

            return result;
        }

        /// <summary>
        /// Prefer the explicit recorded primary request status, but fall back to the currently available certificate
        /// state for older items or manual deployment reruns where a usable unexpired certificate still exists.
        /// </summary>
        /// <param name="managedCert"></param>
        /// <returns></returns>
        private static bool WasLastCertificatePrimaryRequestSuccessful(ManagedCertificate managedCert)
        {
            if (managedCert.LastPrimaryRequest?.Status == RequestState.Success)
            {
                return true;
            }

            return HasUsableCertificate(managedCert);
        }

        /// <summary>
        /// Determine whether the managed certificate currently has a certificate we could deploy (present and not expired)
        /// </summary>
        /// <param name="managedCert"></param>
        /// <returns></returns>
        internal static bool HasUsableCertificate(ManagedCertificate managedCert)
        {
            return managedCert?.DateExpiry.HasValue == true
                && managedCert.DateExpiry > DateTimeOffset.UtcNow
                && (!string.IsNullOrWhiteSpace(managedCert.CertificateThumbprintHash)
                    || !string.IsNullOrWhiteSpace(managedCert.CertificatePath));
        }

        /// <summary>
        /// Determine whether post-request deployment tasks should be evaluated for the completed request
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="result"></param>
        /// <param name="skipTasks"></param>
        /// <returns></returns>
        internal static bool ShouldPerformPostRequestTasks(ManagedCertificate managedCertificate, CertificateRequestResult result, bool skipTasks)
        {
            if (skipTasks)
            {
                return false;
            }

            if (managedCertificate?.PostRequestTasks?.Any() != true)
            {
                return false;
            }

            if (managedCertificate.Health == ManagedCertificateHealth.AwaitingUser)
            {
                return false;
            }

            // an external subscription check which did not apply an update (none available yet, not due, or deployment
            // deferred) will be retried later, so no deployment or deployment tasks are attempted for this request
            if (result?.IsSubscriptionUpdateDeferred == true)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Evaluate and perform the post-request deployment tasks for a completed certificate request, if applicable,
        /// recording the results against the request result and the managed certificate
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCertificate"></param>
        /// <param name="requestResult"></param>
        /// <param name="skipTasks"></param>
        /// <param name="currentFailureCount"></param>
        /// <param name="persistTaskState">
        /// whether the deployment task run state needs storing here. True for a subscription request, which performs no
        /// final renewal status update of its own, so last executed date, last run status and result would otherwise
        /// stay in memory. False for a standard request, which stores the item as part of resolving its final status
        /// </param>
        /// <returns>whether the task list was evaluated</returns>
        private async Task<bool> PerformPostRequestTasksIfApplicable(ILog log, ManagedCertificate managedCertificate, CertificateRequestResult requestResult, bool skipTasks, int? currentFailureCount, bool persistTaskState)
        {
            if (!ShouldPerformPostRequestTasks(managedCertificate, requestResult, skipTasks))
            {
                return false;
            }

            log ??= ManagedCertificateLog.GetLogger(managedCertificate.Id, _loggingLevelSwitch);

            // run applicable deployment tasks (whether success or failed), powershell
            log.Information($"Performing Post-Request (Deployment) Tasks..");

            var results = await PerformTaskList(log, isPreviewOnly: false, skipDeferredTasks: true, requestResult, managedCertificate.PostRequestTasks, forceTaskExecute: false, evaluateAgainstPrimaryRequestStatus: true);

            var postRequestTasks = new ActionStep
            {
                Category = "Post-Request Tasks",
                Key = "PostRequestTasks",
                Substeps = new List<ActionStep>(),
                HasError = results.Any(r => r.HasError),
                HasWarning = results.Any(r => r.HasWarning),
            };

            foreach (var r in results)
            {
                if (r.HasError || r.HasWarning)
                {
                    log.Error($"{r.Title} :: {r.Description}", true);
                }
                else
                {
                    log.Information($"{r.Title} :: {r.Description}", LogItemType.GeneralInfo);
                }

                r.Category = "Post-Request Tasks";
                postRequestTasks.Substeps.Add(r);
            }

            requestResult.Actions.Add(postRequestTasks);

            // certificate may already be deployed to some extent so this counts a completed with warnings
            if (results.Any(r => r.HasError))
            {
                requestResult.IsSuccess = false;

                var msg = GetDeploymentTaskFailureMessage(managedCertificate);
                requestResult.Message = msg;

                // this stores the item, so the task run state is persisted here whether or not it was asked for
                await RecordDeploymentFailure(managedCertificate, msg, currentFailureCount);

                return true;
            }

            if (persistTaskState)
            {
                requestResult.ManagedItem = await UpdateManagedCertificate(managedCertificate);
            }

            return true;
        }

        /// <summary>
        /// Determine whether an item holds a usable certificate which was not fully deployed, so its deployment can be
        /// re-attempted without ordering a new certificate.
        /// Renewal scheduling is calculated from the date the certificate was obtained, so an item whose certificate
        /// arrived but then failed to store, bind or run its deployment tasks is not due for renewal, and without this
        /// would not be attempted again until its next renewal falls due - most of a certificate lifetime later, with
        /// the deployment target still using the previous certificate
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        internal static bool RequiresDeploymentRetry(ManagedCertificate item)
        {
            if (item?.IncludeInAutoRenew != true)
            {
                return false;
            }

            // the item is waiting on a person (manual DNS etc), so it is not ours to retry
            if (item.Health == ManagedCertificateHealth.AwaitingUser)
            {
                return false;
            }

            // only the deployment of a certificate we actually obtained can be retried here. A failed request needs a
            // new certificate, which is the renewal pass's job - redeploying the previous certificate would not help
            if (item.LastPrimaryRequest?.Status != RequestState.Success)
            {
                return false;
            }

            // an expired or missing certificate cannot usefully be deployed, and renewal is due for it by definition
            if (!HasUsableCertificate(item))
            {
                return false;
            }

            return item.LastBindingDeployment?.Status == RequestState.Error
                || HasFailedDeploymentTasks(item);
        }

        /// <summary>
        /// Determine whether a deployment retry for the given item is due now. This applies the same failure back off
        /// used for renewal attempts, so a deployment target which stays unreachable is not re-attempted on every pass
        /// </summary>
        /// <param name="item"></param>
        /// <param name="checkDate"></param>
        /// <returns></returns>
        internal static bool IsDeploymentRetryDue(ManagedCertificate item, DateTimeOffset? checkDate = null)
        {
            var now = checkDate ?? DateTimeOffset.UtcNow;

            if (item.DateLastRenewalAttempt == null)
            {
                return true;
            }

            // a retry must not immediately follow the attempt whose deployment has just failed
            if (now < item.DateLastRenewalAttempt.Value.Add(_minDeploymentRetryInterval))
            {
                return false;
            }

            if (item.RenewalFailureCount < LifetimeHealthThresholds.FailuresBeforeBackoff)
            {
                return true;
            }

            return now >= ManagedCertificate.CalculateFailureBackoff(item).NextAttemptByDate;
        }

        /// <summary>
        /// Identify the items which currently require a deployment retry, paging through the stored items until the
        /// retry batch is full or every item has been considered
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<List<ManagedCertificate>> GetDeploymentRetryCandidates(CancellationToken cancellationToken)
        {
            var candidates = new List<ManagedCertificate>();

            var filter = new ManagedCertificateFilter
            {
                IncludeOnlyNextAutoRenew = true,
                OrderBy = ManagedCertificateFilter.SortMode.RENEWAL_ASC,
                PageSize = DEPLOYMENT_RETRY_SCAN_PAGE_SIZE,
                PageIndex = 0
            };

            var itemsRemaining = await _itemManager.CountAll(filter);

            while (candidates.Count < MAX_DEPLOYMENT_RETRY_TASKS && itemsRemaining > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var page = await _itemManager.Find(filter);

                if (!page.Any())
                {
                    // fewer items are present than the count indicated (e.g. items removed since it was taken)
                    break;
                }

                itemsRemaining -= page.Count;

                foreach (var item in page)
                {
                    if (candidates.Count >= MAX_DEPLOYMENT_RETRY_TASKS)
                    {
                        break;
                    }

                    if (!RequiresDeploymentRetry(item) || !IsDeploymentRetryDue(item))
                    {
                        continue;
                    }

                    // a subscription which still has an update pending is retried in full (fetch and deploy) by the
                    // subscription pass, which is the only place its retained pending version is cleared
                    if (HasPendingSubscriptionUpdate(item.ExternalSource))
                    {
                        continue;
                    }

                    candidates.Add(item);
                }

                filter.PageIndex++;
            }

            return candidates;
        }

        /// <summary>
        /// Re-attempt deployment for items which hold a usable certificate that was not fully deployed, because the
        /// certificate store/binding deployment failed or because a post-request deployment task failed. Only the
        /// deployment of the certificate already held is repeated - no new certificate is ordered, so this is safe to
        /// run frequently and cannot contribute to certificate authority rate limits
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>the results of the deployments attempted</returns>
        public async Task<List<CertificateRequestResult>> PerformDeploymentRetryTasks(CancellationToken cancellationToken)
        {
            var results = new List<CertificateRequestResult>();

            if (IsInDegradedMode)
            {
                return results;
            }

            if (Interlocked.CompareExchange(ref _isDeploymentRetryInProgress, 1, 0) != 0)
            {
                _serviceLog?.Verbose("Deployment retry pass is already in progress, skipping..");
                return results;
            }

            try
            {
                var candidates = await GetDeploymentRetryCandidates(cancellationToken);

                foreach (var candidate in candidates)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // taking the in-progress slot for the item is what keeps the renewal and subscription passes off an
                    // item while it is being redeployed, and skips an item one of them is already working on
                    if (!_renewalsInProgress.TryAdd(candidate.Id, DateTimeOffset.Now))
                    {
                        _serviceLog?.Verbose("Skipping deployment retry for {name} [{id}], a request is already in progress for it.", candidate.Name, candidate.Id);
                        continue;
                    }

                    var item = candidate;
                    int? currentFailureCount = null;

                    try
                    {
                        // the candidate was selected before the in-progress slot was taken, so it is re-read and
                        // re-checked here. Another process may have renewed or redeployed the item in between, in which
                        // case the copy selected is stale and would deploy the wrong certificate
                        item = await _itemManager.GetById(candidate.Id);

                        if (item == null || !RequiresDeploymentRetry(item))
                        {
                            continue;
                        }

                        // preserved before the attempt, because a deployment which succeeds before a later step throws
                        // will have reset the count, and this attempt still needs to continue the existing back off
                        currentFailureCount = item.RenewalFailureCount;

                        _serviceLog?.Information("Re-attempting deployment for {name} [{id}], it holds a certificate which was not fully deployed.", item.Name, item.Id);
                        LogMessage(item.Id, $"---- Re-attempting Deployment ----{Environment.NewLine}The certificate held by this item was obtained but not fully deployed. Deployment and any deployment tasks will be attempted again. No new certificate is requested.");

                        var result = await DeployCertificate(item, progress: null, isPreviewOnly: false, includeDeploymentTasks: true);

                        results.Add(result);

                        if (result.IsSuccess)
                        {
                            _serviceLog?.Information("Deployment retry completed for {name} [{id}].", item.Name, item.Id);
                        }
                        else
                        {
                            _serviceLog?.Warning("Deployment retry did not complete for {name} [{id}]: {msg}", item.Name, item.Id, result.Message);
                        }
                    }
                    catch (Exception exp)
                    {
                        _tc?.TrackException(exp);
                        _serviceLog?.Error(exp, "Deployment retry failed for {name} [{id}]", candidate.Name, candidate.Id);

                        if (item != null)
                        {
                            // the attempt is still recorded as a failure so the back off applies and the retry does not
                            // repeat on every pass
                            await RecordDeploymentFailure(item, $"Deployment retry failed: {exp.Message}", currentFailureCount);
                        }
                    }
                    finally
                    {
                        _renewalsInProgress.TryRemove(candidate.Id, out _);
                    }
                }
            }
            catch (Exception exp)
            {
                _tc?.TrackException(exp);
                _serviceLog?.Error(exp, "PerformDeploymentRetryTasks: error while re-attempting deployments.");
            }
            finally
            {
                Interlocked.Exchange(ref _isDeploymentRetryInProgress, 0);
            }

            return results;
        }

        /// <summary>
        /// Resolve the primary request status to use when recomputing the stored overall renewal status.
        /// An explicitly recorded status is authoritative; the certificate-availability fallback is only
        /// used for older items where no primary request status has been recorded.
        /// </summary>
        /// <param name="managedCert"></param>
        /// <returns></returns>
        private static RequestState ResolveRecordedPrimaryRequestStatus(ManagedCertificate managedCert)
        {
            if (managedCert.LastPrimaryRequest?.Status != null)
            {
                return managedCert.LastPrimaryRequest.Status.Value;
            }

            return WasLastCertificatePrimaryRequestSuccessful(managedCert) ? RequestState.Success : RequestState.Error;
        }

        private static bool ShouldContinueAfterPreviousTaskFailure(TaskTriggerType taskTrigger, bool primaryRequestSucceeded)
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

        private static bool ShouldSkipTaskBecausePreviousTaskFailed(bool previousTaskFailed, bool runIfLastStepFailed, TaskTriggerType taskTrigger, bool primaryRequestSucceeded)
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
        internal async Task<List<ActionStep>> PerformTaskList(ILog log, bool isPreviewOnly, bool skipDeferredTasks, CertificateRequestResult result, IEnumerable<DeploymentTaskConfig> taskList, bool forceTaskExecute, bool evaluateAgainstPrimaryRequestStatus)
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
                    taskResults = await task.Execute(log, _credentialsManager, result, new DeploymentContext { PowershellExecutionPolicy = _serverConfig.PowershellExecutionPolicy }, isPreviewOnly: isPreviewOnly, cancellationToken: CancellationToken.None);

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
                        else
                        {
                            if (dnsProvider.IsTestModeSupported == false)
                            {
                                return new ActionResult { IsSuccess = false, Message = "This DNS provider does not support credential testing." };
                            }
                            else
                            {
                                return await dnsProvider.Test();
                            }
                        }
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
