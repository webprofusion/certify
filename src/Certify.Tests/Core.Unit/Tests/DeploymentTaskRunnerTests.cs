using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Core.Management.DeploymentTasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Deployment task orchestration: which tasks in a list run, which are skipped, and what each one reports.
    ///
    /// These were previously in Certify.Tests.Core.Integration/DeploymentTaskTests.cs, where they needed a fully
    /// initialised CertifyManager (plugins loaded, data store connected) despite only ever exercising MockTask.
    ///
    /// Each test names the production call site it stands in for. There are three, and they differ only in
    /// evaluateAgainstPrimaryRequestStatus:
    ///   pre-request tasks       CertifyManager.CertificateRequest.cs  - false, no request has happened yet
    ///   explicit redeployment   CertifyManager.CertificateRequest.cs  - false, there is no request to judge
    ///   post-request tasks      CertifyManager.DeploymentTasks.cs     - true, judged against the primary request
    ///
    /// Not covered here, and still only covered by the integration tests: the wrapper in PerformCertificateRequest
    /// which sets requestResult.Abort from `results.Any(r => r.HasError)` and shapes the returned steps into
    /// Actions entries keyed "PreRequestTasks"/"PostRequestTasks".
    /// </summary>
    [TestClass]
    public class DeploymentTaskRunnerTests
    {
        private ILog _log;

        [TestInitialize]
        public void Setup() => _log = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<DeploymentTaskRunnerTests>());

        /// <summary>
        /// MockTask is resolved from the Certify.Core assembly itself, so no provider plugins are needed. No task here
        /// sets ChallengeCredentialKey, so no credentials manager is needed either
        /// </summary>
        private static DeploymentTaskRunner GetRunner() => new DeploymentTaskRunner(
            deploymentTaskProviders: null,
            credentialsManager: null,
            powershellExecutionPolicy: "Unrestricted");

        private static DeploymentTaskConfig GetMockTaskConfig(
            string name,
            string msg = "Hello World",
            bool shouldError = false,
            bool fatalOnError = true,
            bool continueOnPreviousError = false,
            TaskTriggerType triggerType = TaskTriggerType.ANY_STATUS
            )
        {
            return new DeploymentTaskConfig
            {
                Id = Guid.NewGuid().ToString(),
                TaskTypeId = Providers.DeploymentTasks.Core.MockTask.Definition.Id,
                TaskName = name,
                IsFatalOnError = fatalOnError,
                RunIfLastStepFailed = continueOnPreviousError,
                TaskTrigger = triggerType,
                Parameters = new List<ProviderParameterSetting>
                        {
                            new ProviderParameterSetting("message", msg),
                            new ProviderParameterSetting("throw", shouldError.ToString()),
                        }
            };
        }

        private static CertificateRequestResult GetRequestResult(string name, RequestState? primaryRequestStatus = null)
        {
            var result = new CertificateRequestResult(new ManagedCertificate { Id = Guid.NewGuid().ToString(), Name = name });

            if (primaryRequestStatus != null)
            {
                result.PrimaryRequest = new RequestStageStatus { Status = primaryRequestStatus, Message = $"Primary request {primaryRequestStatus}" };
            }

            return result;
        }

        /// <summary>
        /// Stands in for the pre-request task call site: no certificate request has happened yet, so triggers are not
        /// judged against a primary request status
        /// </summary>
        private Task<List<ActionStep>> RunPreRequestTasks(CertificateRequestResult result, params DeploymentTaskConfig[] tasks)
            => GetRunner().Run(_log, isPreviewOnly: false, skipDeferredTasks: true, result, tasks, forceTaskExecute: false, evaluateAgainstPrimaryRequestStatus: false);

        /// <summary>
        /// Stands in for the post-request task call site: a certificate request took place, and ON_SUCCESS/ON_ERROR
        /// triggers are judged against its recorded status
        /// </summary>
        private Task<List<ActionStep>> RunPostRequestTasks(CertificateRequestResult result, params DeploymentTaskConfig[] tasks)
            => GetRunner().Run(_log, isPreviewOnly: false, skipDeferredTasks: true, result, tasks, forceTaskExecute: false, evaluateAgainstPrimaryRequestStatus: true);

        /// <summary>
        /// Stands in for DeployCertificate's task call site: an explicit redeployment of the certificate already held.
        /// There is no request outcome to judge, so tasks are evaluated as if the request succeeded
        /// </summary>
        private Task<List<ActionStep>> RunRedeploymentTasks(CertificateRequestResult result, params DeploymentTaskConfig[] tasks)
            => GetRunner().Run(_log, isPreviewOnly: true, skipDeferredTasks: true, result, tasks, forceTaskExecute: false, evaluateAgainstPrimaryRequestStatus: false);

        private static ActionStep StepFor(List<ActionStep> steps, DeploymentTaskConfig task)
        {
            var step = steps.Find(s => s.Key == task.Id);
            Assert.IsNotNull(step, $"Expected a reported step for task [{task.TaskName}]");
            return step;
        }

        private static void AssertTaskRan(List<ActionStep> steps, DeploymentTaskConfig task, string because)
        {
            var step = StepFor(steps, task);
            Assert.IsFalse(step.HasWarning, $"[{task.TaskName}] should have run: {because}. Reported: {step.Description}");
            Assert.IsFalse(step.HasError, $"[{task.TaskName}] should have run without error: {because}. Reported: {step.Description}");
            Assert.AreEqual(RequestState.Success, task.LastRunStatus, $"[{task.TaskName}] should record a successful run");
        }

        private static void AssertTaskSkipped(List<ActionStep> steps, DeploymentTaskConfig task, string because)
        {
            var step = StepFor(steps, task);
            Assert.IsTrue(step.HasWarning, $"[{task.TaskName}] should have been skipped: {because}. Reported: {step.Description}");
            Assert.IsFalse(step.HasError, $"[{task.TaskName}] was skipped, so it should not also report an error. Reported: {step.Description}");
            Assert.AreEqual(RequestState.Skipped, task.LastRunStatus, $"[{task.TaskName}] should record that it was skipped");
        }

        private static void AssertTaskFailed(List<ActionStep> steps, DeploymentTaskConfig task, string because)
        {
            var step = StepFor(steps, task);
            Assert.IsTrue(step.HasError, $"[{task.TaskName}] should have failed: {because}. Reported: {step.Description}");
            Assert.AreEqual(RequestState.Error, task.LastRunStatus, $"[{task.TaskName}] should record a failed run");
        }

        [TestMethod, TestCategory("Tasks"), Description("Pre and post request tasks all run when the request succeeds")]
        public async Task TestRunPreAndPostTasks()
        {
            var preTask1 = GetMockTaskConfig("Pre Task 1");
            var preTask2 = GetMockTaskConfig("Pre Task 2");
            var postTask1 = GetMockTaskConfig("Post Task 1");
            var postTask2 = GetMockTaskConfig("Post Task 2");

            var result = GetRequestResult("PreDeploymentTask1");

            var preSteps = await RunPreRequestTasks(result, preTask1, preTask2);

            Assert.HasCount(2, preSteps, "Both pre-request tasks should be reported");
            AssertTaskRan(preSteps, preTask1, "pre-request tasks always apply");
            AssertTaskRan(preSteps, preTask2, "pre-request tasks always apply");

            result.PrimaryRequest = new RequestStageStatus { Status = RequestState.Success };

            var postSteps = await RunPostRequestTasks(result, postTask1, postTask2);

            Assert.HasCount(2, postSteps, "Both post-request tasks should be reported");
            AssertTaskRan(postSteps, postTask1, "the primary request succeeded and the task runs for any status");
            AssertTaskRan(postSteps, postTask2, "the primary request succeeded and the task runs for any status");
        }

        [TestMethod, TestCategory("Tasks"), Description("A failed pre-request task is reported, and any-status post-request tasks still run")]
        public async Task TestRunPreAndPostTasksWithFailures()
        {
            var preTask1 = GetMockTaskConfig("Pre Task 1");
            var preTask2 = GetMockTaskConfig("Pre Task 2", shouldError: true);
            var postTask1 = GetMockTaskConfig("Post Task 1");
            var postTask2 = GetMockTaskConfig("Post Task 2");

            var result = GetRequestResult("PreDeploymentTask2");

            var preSteps = await RunPreRequestTasks(result, preTask1, preTask2);

            Assert.AreEqual(1, preSteps.Count(s => s.HasError), "One pre-request step should fail");
            AssertTaskRan(preSteps, preTask1, "it precedes the failing task");
            AssertTaskFailed(preSteps, preTask2, "the mock task was asked to throw");

            // the failed pre-request task aborts the request, so the post-request tasks are judged against a failed
            // primary request. Both run anyway, because they run for any status
            result.PrimaryRequest = new RequestStageStatus { Status = RequestState.Error, Message = "Request was aborted due to failed Pre-Request Task." };

            var postSteps = await RunPostRequestTasks(result, postTask1, postTask2);

            Assert.AreEqual(2, postSteps.Count(s => !s.HasError), "Two post-request steps should succeed");
            AssertTaskRan(postSteps, postTask1, "the task runs for any status");
            AssertTaskRan(postSteps, postTask2, "the task runs for any status");
        }

        [TestMethod, TestCategory("Tasks"), Description("After a successful request, on-success and any-status tasks run and on-error tasks do not")]
        public async Task TestRunPostTasksWithSuccessTrigger()
        {
            var onSuccess = GetMockTaskConfig("Post Task 1 (on success)", triggerType: TaskTriggerType.ON_SUCCESS);
            var onError = GetMockTaskConfig("Post Task 2 (on fail)", triggerType: TaskTriggerType.ON_ERROR);
            var anyStatus = GetMockTaskConfig("Post Task 3 (any status)", triggerType: TaskTriggerType.ANY_STATUS);

            var result = GetRequestResult("PostDeploymentTaskSuccess", RequestState.Success);

            var steps = await RunPostRequestTasks(result, onSuccess, onError, anyStatus);

            AssertTaskRan(steps, onSuccess, "the primary request succeeded");
            AssertTaskSkipped(steps, onError, "there is no failed request for it to react to");
            AssertTaskRan(steps, anyStatus, "the task runs for any status");
        }

        [TestMethod, TestCategory("Tasks"), Description("An explicit redeployment runs on-success tasks even with no primary request status")]
        public async Task TestDeployCertificateRunsPostTasksWithoutPrimaryRequestStatus()
        {
            var onSuccess = GetMockTaskConfig("Post Task 1 (on success)", triggerType: TaskTriggerType.ON_SUCCESS);

            // no PrimaryRequest is set: a redeployment is not the outcome of a certificate request
            var result = GetRequestResult("DeployCertificateTaskSuccess");

            var steps = await RunRedeploymentTasks(result, onSuccess);

            AssertTaskRan(steps, onSuccess, "the deployment being asked for is the success it reacts to");
            Assert.IsFalse(
                StepFor(steps, onSuccess).Description?.Contains("primary request unsuccessful", StringComparison.OrdinalIgnoreCase) == true,
                "A redeployment has no primary request to be judged against");
        }

        [TestMethod, TestCategory("Tasks"), Description("An explicit redeployment does not run on-error tasks, even when the item last renewed unsuccessfully")]
        public async Task TestDeployCertificateDoesNotRunErrorTasks()
        {
            // an explicit redeployment of the certificate we already hold is not the outcome of a failed request, so
            // there is nothing for an on-error task to react to, even when the item last renewed unsuccessfully
            var onSuccess = GetMockTaskConfig("Post Task 1 (on success)", triggerType: TaskTriggerType.ON_SUCCESS);
            var onError = GetMockTaskConfig("Post Task 2 (on error)", triggerType: TaskTriggerType.ON_ERROR);

            var result = GetRequestResult("DeployCertificateTaskError");
            result.ManagedItem.LastRenewalStatus = RequestState.Error;

            var steps = await RunRedeploymentTasks(result, onSuccess, onError);

            AssertTaskRan(steps, onSuccess, "it is the deployment being requested");

            var errorStep = StepFor(steps, onError);
            Assert.IsTrue(
                errorStep.Description?.Contains("will not run", StringComparison.OrdinalIgnoreCase) == true,
                $"On-error deployment task should not run during an explicit redeployment. Reported: {errorStep.Description}");
        }

        [TestMethod, TestCategory("Tasks"), Description("After a failed request, on-error and any-status tasks run and on-success tasks do not")]
        public async Task TestRunPreAndPostTasksWithFailTrigger()
        {
            var onSuccess = GetMockTaskConfig("Post Task 1 (on success)", triggerType: TaskTriggerType.ON_SUCCESS);
            var onError = GetMockTaskConfig("Post Task 2 (on fail)", triggerType: TaskTriggerType.ON_ERROR);
            var anyStatus = GetMockTaskConfig("Post Task 3 (any status)", triggerType: TaskTriggerType.ANY_STATUS);

            var result = GetRequestResult("PostDeploymentTaskFail", RequestState.Error);

            var steps = await RunPostRequestTasks(result, onSuccess, onError, anyStatus);

            AssertTaskSkipped(steps, onSuccess, "the primary request failed");
            AssertTaskRan(steps, onError, "the primary request failed");
            AssertTaskRan(steps, anyStatus, "the task runs for any status");
        }

        [TestMethod, TestCategory("Tasks"), Description("On-task-error tasks do not run when no preceding task actually ran")]
        public async Task TestRunPostTasksWithTaskFailTriggerWhenNoTaskRan()
        {
            // the primary request was skipped rather than succeeding, so the on-success tasks never run and never fail.
            // An on-task-error task has nothing to react to and is skipped in turn - a task which was itself skipped is
            // not a task failure
            var failingOnSuccess = GetMockTaskConfig("Post Task 1 (on renewal success)", shouldError: true, triggerType: TaskTriggerType.ON_SUCCESS);
            var continuesOnFailure = GetMockTaskConfig("Post Task 2 (on renewal success)", continueOnPreviousError: true, triggerType: TaskTriggerType.ON_SUCCESS);
            var onTaskError = GetMockTaskConfig("Post Task 3 (on task fail)", triggerType: TaskTriggerType.ON_TASK_ERROR);

            var result = GetRequestResult("PostDeploymentTask3", RequestState.Skipped);

            var steps = await RunPostRequestTasks(result, failingOnSuccess, continuesOnFailure, onTaskError);

            AssertTaskSkipped(steps, failingOnSuccess, "the primary request did not succeed, so it never runs and never throws");
            AssertTaskSkipped(steps, continuesOnFailure, "the primary request did not succeed");
            AssertTaskSkipped(steps, onTaskError, "no preceding task failed, because none of them ran");
        }

        [TestMethod, TestCategory("Tasks"), Description("An on-task-error task runs when a preceding task actually failed")]
        public async Task TestRunPostTasksWithTaskFailTrigger()
        {
            // the scenario the previous test's name suggests but does not create: the primary request succeeded, so the
            // first task runs and genuinely fails, and the on-task-error task fires because of it
            var failingOnSuccess = GetMockTaskConfig("Post Task 1 (on renewal success)", shouldError: true, triggerType: TaskTriggerType.ON_SUCCESS);
            var continuesOnFailure = GetMockTaskConfig("Post Task 2 (on renewal success)", continueOnPreviousError: true, triggerType: TaskTriggerType.ON_SUCCESS);
            var onTaskError = GetMockTaskConfig("Post Task 3 (on task fail)", triggerType: TaskTriggerType.ON_TASK_ERROR);

            var result = GetRequestResult("PostDeploymentTask3Success", RequestState.Success);

            var steps = await RunPostRequestTasks(result, failingOnSuccess, continuesOnFailure, onTaskError);

            AssertTaskFailed(steps, failingOnSuccess, "the mock task was asked to throw");
            AssertTaskRan(steps, continuesOnFailure, "it is configured to run even though the previous task failed");
            AssertTaskRan(steps, onTaskError, "a preceding task failed");
        }

        [TestMethod, TestCategory("Tasks"), Description("A task which does not opt in to running after a failure is skipped")]
        public async Task TestTaskWithoutContinueOnPreviousErrorIsSkippedAfterAFailure()
        {
            // the counterpart to RunIfLastStepFailed above: this is what stops a deployment chain after its first
            // failure, rather than continuing to deploy against a half-finished state
            var failingOnSuccess = GetMockTaskConfig("Post Task 1 (on renewal success)", shouldError: true, triggerType: TaskTriggerType.ON_SUCCESS);
            var stopsOnFailure = GetMockTaskConfig("Post Task 2 (on renewal success)", triggerType: TaskTriggerType.ON_SUCCESS);

            var result = GetRequestResult("PostDeploymentTaskChainStops", RequestState.Success);

            var steps = await RunPostRequestTasks(result, failingOnSuccess, stopsOnFailure);

            AssertTaskFailed(steps, failingOnSuccess, "the mock task was asked to throw");
            AssertTaskSkipped(steps, stopsOnFailure, "the preceding task failed and it did not opt in to running anyway");
        }

        [TestMethod, TestCategory("Tasks"), Description("After an aborted request, on-error and any-status tasks run and on-success tasks do not")]
        public async Task TestRunPostTasksOnErrorOrAnyStatusWhenPrimaryRequestAborts()
        {
            var preTask = GetMockTaskConfig("Pre Task 1", shouldError: true);
            var onSuccess = GetMockTaskConfig("Post Task 1 (on success)", triggerType: TaskTriggerType.ON_SUCCESS);
            var onError = GetMockTaskConfig("Post Task 2 (on fail)", triggerType: TaskTriggerType.ON_ERROR);
            var anyStatus = GetMockTaskConfig("Post Task 3 (any status)", triggerType: TaskTriggerType.ANY_STATUS);

            var result = GetRequestResult("PostDeploymentTask4");

            var preSteps = await RunPreRequestTasks(result, preTask);

            AssertTaskFailed(preSteps, preTask, "the mock task was asked to throw");

            // PerformCertificateRequest aborts the request when a pre-request task fails, and records the primary
            // request as an error. That wrapper is not covered here, so the resulting state is set up directly
            Assert.IsTrue(preSteps.Any(s => s.HasError), "The failed pre-request task is what causes the request to abort");
            result.Abort = true;
            result.PrimaryRequest = new RequestStageStatus { Status = RequestState.Error, Message = "Request was aborted due to failed Pre-Request Task." };

            var postSteps = await RunPostRequestTasks(result, onSuccess, onError, anyStatus);

            AssertTaskSkipped(postSteps, onSuccess, "the primary request aborted");
            AssertTaskRan(postSteps, onError, "the primary request aborted");
            AssertTaskRan(postSteps, anyStatus, "the task runs for any status");
        }

        [TestMethod, TestCategory("Tasks"), Description("Deferred/manual tasks are excluded while deferred tasks are being skipped")]
        public async Task TestManualTasksAreExcludedWhenDeferredTasksAreSkipped()
        {
            // skipDeferredTasks is true at all three production call sites, so a manual task is not merely reported as
            // skipped - it is left out of the run entirely
            var manualTask = GetMockTaskConfig("Manual Task", triggerType: TaskTriggerType.MANUAL);
            var anyStatus = GetMockTaskConfig("Post Task (any status)", triggerType: TaskTriggerType.ANY_STATUS);

            var result = GetRequestResult("PostDeploymentTaskManual", RequestState.Success);

            var skippedDeferred = await RunPostRequestTasks(result, manualTask, anyStatus);

            Assert.HasCount(1, skippedDeferred, "A manual task should not be reported at all while deferred tasks are skipped");
            AssertTaskRan(skippedDeferred, anyStatus, "the task runs for any status");

            var includedDeferred = await GetRunner().Run(
                _log, isPreviewOnly: false, skipDeferredTasks: false, result, [manualTask, anyStatus],
                forceTaskExecute: false, evaluateAgainstPrimaryRequestStatus: true);

            Assert.HasCount(2, includedDeferred, "A manual task should be included when deferred tasks are not being skipped");
            AssertTaskRan(includedDeferred, manualTask, "deferred tasks are not being skipped");
        }

        [TestMethod, TestCategory("Tasks"), Description("Forcing execution runs a task which its trigger would otherwise skip")]
        public async Task TestForceTaskExecuteRunsATaskItsTriggerWouldSkip()
        {
            // this is the path behind running a single task on demand from the UI, where the user has asked for it
            // regardless of what the last request did
            var onError = GetMockTaskConfig("Post Task (on error)", triggerType: TaskTriggerType.ON_ERROR);

            var result = GetRequestResult("PostDeploymentTaskForced", RequestState.Success);

            var notForced = await RunPostRequestTasks(result, onError);
            AssertTaskSkipped(notForced, onError, "the primary request succeeded");

            var forced = await GetRunner().Run(
                _log, isPreviewOnly: false, skipDeferredTasks: true, result, [onError],
                forceTaskExecute: true, evaluateAgainstPrimaryRequestStatus: true);

            AssertTaskRan(forced, onError, "execution was forced");
        }

        [TestMethod, TestCategory("Tasks"), Description("A task which is not enabled is skipped")]
        public async Task TestNotEnabledTaskIsSkipped()
        {
            var disabled = GetMockTaskConfig("Disabled Task", triggerType: TaskTriggerType.NOT_ENABLED);
            var anyStatus = GetMockTaskConfig("Post Task (any status)", triggerType: TaskTriggerType.ANY_STATUS);

            var result = GetRequestResult("PostDeploymentTaskDisabled", RequestState.Success);

            var steps = await RunPostRequestTasks(result, disabled, anyStatus);

            AssertTaskSkipped(steps, disabled, "the task is not enabled");
            AssertTaskRan(steps, anyStatus, "the task runs for any status");
        }

        [TestMethod, TestCategory("Tasks"), Description("An unknown task provider is reported as a failed task rather than being dropped")]
        public async Task TestUnknownTaskProviderIsReportedAsAFailedTask()
        {
            // a task whose provider cannot be created must not vanish from the results: its stored run status is what
            // lets the deployment retry pass identify the item as still needing attention.
            // With no provider plugins the factory returns null rather than throwing, so the failure surfaces from the
            // task itself; with plugins loaded it throws and is recorded during setup instead. Both must report a
            // failed task, so both are covered here
            var unknown = GetMockTaskConfig("Unknown Provider Task");
            unknown.TaskTypeId = "Certify.Providers.DeploymentTasks.NoSuchProvider";

            var result = GetRequestResult("PostDeploymentTaskUnknownProvider", RequestState.Success);

            var withoutPlugins = await RunPostRequestTasks(result, unknown);

            Assert.HasCount(1, withoutPlugins, "A task with an unresolvable provider should still be reported");
            AssertTaskFailed(withoutPlugins, unknown, "its provider could not be created");

            var withPlugins = await new DeploymentTaskRunner(
                deploymentTaskProviders: [],
                credentialsManager: null,
                powershellExecutionPolicy: "Unrestricted")
                .Run(_log, isPreviewOnly: false, skipDeferredTasks: true, result, [unknown], forceTaskExecute: false, evaluateAgainstPrimaryRequestStatus: true);

            Assert.HasCount(1, withPlugins, "A task with an unresolvable provider should still be reported");
            AssertTaskFailed(withPlugins, unknown, "no loaded plugin provides its task type");
            StringAssert.Contains(withPlugins[0].Description, "Cannot create task provider", "The setup failure should say the provider could not be created");
        }

        [TestMethod, TestCategory("Tasks"), Description("An empty or null task list does nothing")]
        public async Task TestEmptyTaskListDoesNothing()
        {
            var result = GetRequestResult("PostDeploymentTaskNone", RequestState.Success);

            Assert.IsEmpty(await RunPostRequestTasks(result), "An empty task list should produce no steps");

            var nullList = await GetRunner().Run(
                _log, isPreviewOnly: false, skipDeferredTasks: true, result, null,
                forceTaskExecute: false, evaluateAgainstPrimaryRequestStatus: true);

            Assert.IsEmpty(nullList, "A null task list should produce no steps");
        }
    }
}
