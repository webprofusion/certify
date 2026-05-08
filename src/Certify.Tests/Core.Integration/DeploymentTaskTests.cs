using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    /// <summary>
    /// Integration tests for Deployment Task processing
    /// </summary>
    public class DeploymentTaskTests : IntegrationTestBase
    {
        private CertifyManager certifyManager;
        private string testSiteDomain = "";

        public DeploymentTaskTests()
        {
            certifyManager = new CertifyManager();
            certifyManager.Init().Wait();
        }

        [TestCleanup]
        public void Cleanup()
        {
            certifyManager?.Dispose();
        }

        private DeploymentTaskConfig GetMockTaskConfig(
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

        [TestMethod, TestCategory("Tasks")]
        public async Task TestRunPreAndPostTasks()
        {

            var managedCertificate = GetMockManagedCertificate("PreDeploymentTask1", testSiteDomain);
            managedCertificate.LastRenewalStatus = RequestState.Success;

            managedCertificate.PreRequestTasks = new ObservableCollection<DeploymentTaskConfig> {
                                                                            GetMockTaskConfig("Pre Task 1"),
                                                                            GetMockTaskConfig("Pre Task 2")
                                                                        };

            managedCertificate.PostRequestTasks = new ObservableCollection<Config.DeploymentTaskConfig> {
                                                                            GetMockTaskConfig("Post Task 1"),
                                                                            GetMockTaskConfig("Post Task 2")
                                                                        };

            try
            {
                var result = await certifyManager.PerformCertificateRequest(_log, managedCertificate, skipRequest: true);

                Assert.AreEqual(4, result.Actions.Sum(s => s.Substeps.Count));
                //ensure process success
                Assert.IsTrue(result.IsSuccess, "Result OK");
            }
            finally
            {
                await certifyManager.DeleteManagedCertificate(managedCertificate.Id);
            }
        }

        [TestMethod, TestCategory("Tasks")]
        public async Task TestRunPreAndPostTasksWithFailures()
        {

            var managedCertificate = GetMockManagedCertificate("PreDeploymentTask2", testSiteDomain);

            managedCertificate.PreRequestTasks = new ObservableCollection<DeploymentTaskConfig> {
                                                                            GetMockTaskConfig("Pre Task 1"),
                                                                            GetMockTaskConfig("Pre Task 2", shouldError:true)
                                                                        };

            managedCertificate.PostRequestTasks = new ObservableCollection<Config.DeploymentTaskConfig> {
                                                                            GetMockTaskConfig("Post Task 1"),
                                                                            GetMockTaskConfig("Post Task 2")
                                                                        };

            try
            {
                var result = await certifyManager.PerformCertificateRequest(_log, managedCertificate, skipRequest: true);

                //ensure 1 step fails
                Assert.AreEqual(1, result.Actions.First(s => s.Key == "PreRequestTasks").Substeps.Count(a => a.HasError), "One pre-request step should fail");
                Assert.AreEqual(2, result.Actions.First(s => s.Key == "PostRequestTasks").Substeps.Count(a => !a.HasError), "Two post-request steps should succeed");
            }
            finally
            {
                await certifyManager.DeleteManagedCertificate(managedCertificate.Id);
            }
        }

        [TestMethod, TestCategory("Tasks")]
        public async Task TestRunPostTasksWithSuccessTrigger()
        {

            var managedCertificate = GetMockManagedCertificate("PostDeploymentTaskSuccess", testSiteDomain);
            managedCertificate.LastRenewalStatus = RequestState.Success;

            managedCertificate.PreRequestTasks = null;

            managedCertificate.PostRequestTasks = new ObservableCollection<DeploymentTaskConfig> {
                                                                            GetMockTaskConfig("Post Task 1 (on success)", triggerType: TaskTriggerType.ON_SUCCESS),
                                                                            GetMockTaskConfig("Post Task 2 (on fail)", triggerType: TaskTriggerType.ON_ERROR),
                                                                            GetMockTaskConfig("Post Task 3 (any status)", triggerType: TaskTriggerType.ANY_STATUS)
                                                                        };

            try
            {
                var result = await certifyManager.PerformCertificateRequest(_log, managedCertificate, skipRequest: true);

                Assert.IsTrue(result.IsSuccess, "Primary request should be successful");

                var postRequestSteps = result
                    .Actions.Find(s => s.Key == "PostRequestTasks")
                    .Substeps;

                var successStep = postRequestSteps.Find(s => s.Key == managedCertificate.PostRequestTasks[0].Id);
                Assert.IsFalse(successStep.HasError, "On-success post-request task should run after primary request succeeds");
                Assert.IsFalse(successStep.HasWarning, "On-success post-request task should not be skipped after primary request succeeds");

                var errorStep = postRequestSteps.Find(s => s.Key == managedCertificate.PostRequestTasks[1].Id);
                Assert.IsTrue(errorStep.HasWarning, "On-error post-request task should be skipped after primary request succeeds");

                var anyStatusStep = postRequestSteps.Find(s => s.Key == managedCertificate.PostRequestTasks[2].Id);
                Assert.IsFalse(anyStatusStep.HasError, "Any-status post-request task should run after primary request succeeds");
                Assert.IsFalse(anyStatusStep.HasWarning, "Any-status post-request task should not be skipped after primary request succeeds");
            }
            finally
            {
                await certifyManager.DeleteManagedCertificate(managedCertificate.Id);
            }
        }

        [TestMethod, TestCategory("Tasks")]
        public async Task TestRunPreAndPostTasksWithFailTrigger()
        {

            var managedCertificate = GetMockManagedCertificate("PreDeploymentTask2", testSiteDomain);

            managedCertificate.PreRequestTasks = null;

            managedCertificate.PostRequestTasks = new ObservableCollection<DeploymentTaskConfig> {
                                                                            GetMockTaskConfig("Post Task 1 (on success)", triggerType: TaskTriggerType.ON_SUCCESS),
                                                                            GetMockTaskConfig("Post Task 2 (on fail)", triggerType: TaskTriggerType.ON_ERROR),
                                                                            GetMockTaskConfig("Post Task 3 (any status)", triggerType: TaskTriggerType.ANY_STATUS)
                                                                        };

            try
            {
                // perform request but skip + fail main request 
                var result = await certifyManager.PerformCertificateRequest(_log, managedCertificate, skipRequest: true, failOnSkip: true);

                //ensure 1 post request step fails
                var expectedSkipStepKey = managedCertificate.PostRequestTasks[0].Id;
                var expectedRunStepKey = managedCertificate.PostRequestTasks[1].Id;

                var ranStep = result
                    .Actions.Find(s => s.Key == "PostRequestTasks")
                    .Substeps.Find(s => s.Key == expectedRunStepKey);

                Assert.IsFalse(ranStep.HasError, "One post-request steps should run");

                var skippedStep = result
                    .Actions.Find(s => s.Key == "PostRequestTasks")
                    .Substeps.Find(s => s.Key == expectedSkipStepKey);

                Assert.IsTrue(skippedStep.HasWarning, "Skipped step should have warning");

                var anyStatusStep = result
                    .Actions.Find(s => s.Key == "PostRequestTasks")
                    .Substeps.Find(s => s.Key == managedCertificate.PostRequestTasks[2].Id);

                Assert.IsFalse(anyStatusStep.HasError, "Any-status post-request task should run after primary request fails");
                Assert.IsFalse(anyStatusStep.HasWarning, "Any-status post-request task should not be skipped after primary request fails");
            }
            finally
            {
                await certifyManager.DeleteManagedCertificate(managedCertificate.Id);
            }
        }

        [TestMethod, TestCategory("Tasks")]
        public async Task TestRunPostTasksWithTaskFailTrigger()
        {
            // task 1 will fail, task 2 will run specifically because a preceeding failed
            var managedCertificate = GetMockManagedCertificate("PostDeploymentTask3", testSiteDomain);

            managedCertificate.PreRequestTasks = null;

            managedCertificate.PostRequestTasks = new ObservableCollection<DeploymentTaskConfig> {
                                                                            GetMockTaskConfig("Post Task 1 (on renewal success)", shouldError:true, triggerType: TaskTriggerType.ON_SUCCESS),
                                                                                 GetMockTaskConfig("Post Task 2 (on renewal success)", continueOnPreviousError:true,  triggerType: TaskTriggerType.ON_SUCCESS),
                                                                            GetMockTaskConfig("Post Task 3 (on task fail)", triggerType: TaskTriggerType.ON_TASK_ERROR)
                                                                        };

            try
            {
                // perform request but skip + fail main request 
                var result = await certifyManager.PerformCertificateRequest(_log, managedCertificate, skipRequest: true, failOnSkip: false);

                //ensure 3rd task runs because task 1 failed
                var expectedFailureTaskStepKey = managedCertificate.PostRequestTasks.First(t => t.TaskName == "Post Task 3 (on task fail)").Id;

                var skippedStep = result
                    .Actions.Find(s => s.Key == "PostRequestTasks")
                    .Substeps.Find(s => s.Key == expectedFailureTaskStepKey);

                Assert.IsTrue(skippedStep.HasWarning, "Skipped step should have warning");
            }
            finally
            {
                await certifyManager.DeleteManagedCertificate(managedCertificate.Id);
            }
        }

        [TestMethod, TestCategory("Tasks")]
        public async Task TestRunPostTasksOnErrorOrAnyStatusWhenPrimaryRequestAborts()
        {
            var managedCertificate = GetMockManagedCertificate("PostDeploymentTask4", testSiteDomain);

            managedCertificate.PreRequestTasks = new ObservableCollection<DeploymentTaskConfig> {
                                                                            GetMockTaskConfig("Pre Task 1", shouldError:true)
                                                                        };

            managedCertificate.PostRequestTasks = new ObservableCollection<DeploymentTaskConfig> {
                                                                            GetMockTaskConfig("Post Task 1 (on success)", triggerType: TaskTriggerType.ON_SUCCESS),
                                                                            GetMockTaskConfig("Post Task 2 (on fail)", triggerType: TaskTriggerType.ON_ERROR),
                                                                            GetMockTaskConfig("Post Task 3 (any status)", triggerType: TaskTriggerType.ANY_STATUS)
                                                                        };

            try
            {
                var result = await certifyManager.PerformCertificateRequest(_log, managedCertificate, skipRequest: true);

                Assert.IsTrue(result.Abort, "Primary request should abort after a failed pre-request task");

                var postRequestSteps = result
                    .Actions.Find(s => s.Key == "PostRequestTasks")
                    .Substeps;

                var successStep = postRequestSteps.Find(s => s.Key == managedCertificate.PostRequestTasks[0].Id);
                Assert.IsTrue(successStep.HasWarning, "On-success post-request task should be skipped after primary request aborts");

                var errorStep = postRequestSteps.Find(s => s.Key == managedCertificate.PostRequestTasks[1].Id);
                Assert.IsFalse(errorStep.HasError, "On-error post-request task should run after primary request aborts");
                Assert.IsFalse(errorStep.HasWarning, "On-error post-request task should not be skipped after primary request aborts");

                var anyStatusStep = postRequestSteps.Find(s => s.Key == managedCertificate.PostRequestTasks[2].Id);
                Assert.IsFalse(anyStatusStep.HasError, "Any-status post-request task should run after primary request aborts");
                Assert.IsFalse(anyStatusStep.HasWarning, "Any-status post-request task should not be skipped after primary request aborts");
            }
            finally
            {
                await certifyManager.DeleteManagedCertificate(managedCertificate.Id);
            }
        }
    }
}
