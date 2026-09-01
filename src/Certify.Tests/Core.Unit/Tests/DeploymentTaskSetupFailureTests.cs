using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how a deployment task which cannot be prepared for execution is recorded. A task whose credentials
    /// cannot be unlocked, or whose provider cannot be created, must fail on its own rather than taking the rest of the
    /// task list with it, and must leave a stored failure so the overall request reflects it
    /// </summary>
    [TestClass]
    public class DeploymentTaskSetupFailureTests
    {
        private const string CredentialFailureMessage = "Failed to decrypt selected credentials for this task.";

        private static void InvokeRecordTaskSetupFailure(List<ActionStep> steps, DeploymentTaskConfig taskConfig, string message)
        {
            var method = typeof(CertifyManager).GetMethod("RecordTaskSetupFailure", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);

            method.Invoke(null, new object[] { steps, taskConfig, message, null });
        }

        private static RequestState InvokeResolveOverallRenewalStatus(ManagedCertificate managedCertificate, CertificateRequestResult requestResult, bool postRequestTasksRan)
        {
            var method = typeof(CertifyManager).GetMethod("ResolveOverallRenewalStatus", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);

            return (RequestState)method.Invoke(null, new object[] { managedCertificate, requestResult, postRequestTasksRan });
        }

        private static ManagedCertificate CreateItemWithTasks(params DeploymentTaskConfig[] tasks)
        {
            var now = DateTimeOffset.UtcNow;

            return new ManagedCertificate
            {
                Id = "test-item",
                Name = "Test Item",
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ACME,
                DateStart = now.AddHours(-1),
                DateRenewed = now.AddHours(-1),
                DateExpiry = now.AddDays(90),
                DateLastRenewalAttempt = now.AddHours(-1),
                CertificateThumbprintHash = "ABC123",
                LastRenewalStatus = RequestState.Success,
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "New certificate received OK." },
                LastBindingDeployment = new RequestStageStatus { Status = RequestState.Success, Message = "Deployed OK." },
                PostRequestTasks = new ObservableCollection<DeploymentTaskConfig>(tasks),
                RequestConfig = new CertRequestConfig { PrimaryDomain = "test.example.com" }
            };
        }

        [TestMethod, Description("A task which cannot be prepared is recorded as a failed task, not left with its previous run status")]
        public void SetupFailureIsRecordedAgainstTheTask()
        {
            var task = new DeploymentTaskConfig
            {
                Id = "task-1",
                TaskName = "Upload",
                LastRunStatus = RequestState.Success,
                LastResult = "Task Completed OK"
            };

            var steps = new List<ActionStep>();

            InvokeRecordTaskSetupFailure(steps, task, CredentialFailureMessage);

            Assert.AreEqual(RequestState.Error, task.LastRunStatus, "The task must not keep the run status of a previous, successful run");
            Assert.AreEqual(CredentialFailureMessage, task.LastResult);

            Assert.HasCount(1, steps);
            Assert.IsTrue(steps[0].HasError);
            Assert.AreEqual("task-1", steps[0].Key, "The step must identify the task it belongs to");
            Assert.AreEqual(CredentialFailureMessage, steps[0].Description);
        }

        [TestMethod, Description("A task which cannot be prepared makes the overall request status a failure")]
        public void SetupFailureMakesTheRequestFail()
        {
            var failedTask = new DeploymentTaskConfig { Id = "task-1", TaskName = "Upload", LastRunStatus = RequestState.Success };
            var item = CreateItemWithTasks(failedTask);

            var requestResult = new CertificateRequestResult(item, isSuccess: true, "Certificate issued.")
            {
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "Certificate issued." }
            };

            Assert.AreEqual(RequestState.Success, InvokeResolveOverallRenewalStatus(item, requestResult, postRequestTasksRan: true),
                "Before the failure is recorded the request looks successful, which is the state a task that never ran would have left behind");

            InvokeRecordTaskSetupFailure(new List<ActionStep>(), failedTask, CredentialFailureMessage);

            Assert.AreEqual(RequestState.Error, InvokeResolveOverallRenewalStatus(item, requestResult, postRequestTasksRan: true),
                "A task which could not be prepared must make the request fail");
        }

        [TestMethod, Description("A task which cannot be prepared leaves the item due for a deployment retry")]
        public void SetupFailureLeavesItemDueForDeploymentRetry()
        {
            var failedTask = new DeploymentTaskConfig { Id = "task-1", TaskName = "Upload", LastRunStatus = RequestState.Success };
            var item = CreateItemWithTasks(failedTask);

            Assert.IsFalse(CertifyManager.RequiresDeploymentRetry(item), "Nothing has failed yet");

            InvokeRecordTaskSetupFailure(new List<ActionStep>(), failedTask, CredentialFailureMessage);

            // an unreadable credential may be a temporary problem, so the deployment retry pass has to be able to see it
            Assert.IsTrue(CertifyManager.RequiresDeploymentRetry(item), "The item should be re-attempted by the deployment retry pass");
        }

        [TestMethod, Description("Only the task which could not be prepared is marked failed, the rest of the list is untouched")]
        public void SetupFailureDoesNotAffectOtherTasks()
        {
            var failedTask = new DeploymentTaskConfig { Id = "task-1", TaskName = "Upload", LastRunStatus = RequestState.Success };
            var laterTask = new DeploymentTaskConfig { Id = "task-2", TaskName = "Notify", LastRunStatus = RequestState.Success, LastResult = "Task Completed OK" };

            CreateItemWithTasks(failedTask, laterTask);

            InvokeRecordTaskSetupFailure(new List<ActionStep>(), failedTask, CredentialFailureMessage);

            Assert.AreEqual(RequestState.Error, failedTask.LastRunStatus);
            Assert.AreEqual(RequestState.Success, laterTask.LastRunStatus, "Tasks after the failed one are still executed and keep their own outcome");
            Assert.AreEqual("Task Completed OK", laterTask.LastResult);
        }
    }
}
