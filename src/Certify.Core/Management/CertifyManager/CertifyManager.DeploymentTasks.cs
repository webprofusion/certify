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

            // the stored overall status is recomputed from every recorded stage, so the outcome of this run is judged
            // alongside the recorded primary request and deployment rather than replacing them
            await StoreRecomputedRenewalStatus(managedCert);

            return result;
        }

        /// <summary>
        /// Resolve the overall renewal status an item should show from the stage outcomes recorded against it: the
        /// primary request, the certificate store and binding deployment, and the automated deployment tasks. Used
        /// where one stage has changed outside a full request - a task run on demand, or a subscription check which
        /// found its source answering again - so the stored status describes all of the recorded stages together
        /// rather than only the stage which changed
        /// </summary>
        /// <param name="managedCert"></param>
        /// <returns>the overall status and the message describing it</returns>
        internal static (RequestState Status, string Message) ResolveRecordedRenewalStatus(ManagedCertificate managedCert)
        {
            // the explicitly recorded primary request status is authoritative (a failed renewal must stay failed even if
            // an older usable certificate allowed a manual task run to proceed); the certificate-availability fallback
            // only applies when no status was recorded
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

            var postRequestTasksRan = managedCert.PostRequestTasks?.Any() == true;

            var status = ResolveOverallRenewalStatus(managedCert, primaryRequestResult, postRequestTasksRan);
            var message = ResolveOverallRenewalMessage(managedCert, primaryRequestResult, status, postRequestTasksRan);

            return (status, message);
        }

        /// <summary>
        /// Store the item with its overall renewal status recomputed from the recorded stages. No new attempt has been
        /// made, only what is known about the last one has changed, so the last attempt date is left as it is and the
        /// failure count is only touched by a recomputed success, which clears it as any success does
        /// </summary>
        /// <param name="managedCert"></param>
        private async Task StoreRecomputedRenewalStatus(ManagedCertificate managedCert)
        {
            var (status, message) = ResolveRecordedRenewalStatus(managedCert);

            var statusChanged = managedCert.LastRenewalStatus != status
                || !string.Equals(managedCert.RenewalFailureMessage, message, StringComparison.Ordinal);

            if (statusChanged)
            {
                await UpdateManagedCertificateStatus(managedCert, status, message, incrementFailureCount: false, updateLastAttempt: false);
            }
            else
            {
                await UpdateManagedCertificate(managedCert);
            }
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

            return ManagedCertificate.HasUsableCertificate(managedCert);
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

            // a task which failed leaves the certificate not fully deployed, which is recorded as a failed request
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
            var runner = new DeploymentTaskRunner(
                _pluginManager.DeploymentTaskProviders,
                _credentialsManager,
                _serverConfig.PowershellExecutionPolicy,
                _tc,
                _loggingLevelSwitch);

            return await runner.Run(log, isPreviewOnly, skipDeferredTasks, result, taskList, forceTaskExecute, evaluateAgainstPrimaryRequestStatus);
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
