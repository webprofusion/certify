using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Management.Servers;
using Certify.Models;
using Certify.Models.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;
using Certify.Models.Config;
using Certify.Config;

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
            var log = new LoggerConfiguration()
                     .WriteTo.Debug()
                     .CreateLogger();

            _log = new Loggy(log);

            certifyManager = new CertifyManager();

            // see integrationtestbase for environment variable replacement
            PrimaryTestDomain = ConfigSettings["Cloudflare_TestDomain"];
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

                Assert.AreEqual(result.Actions.Sum(s => s.Substeps.Count), 4);
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
                Assert.IsTrue(result.Actions.First(s => s.Key == "PreRequestTasks").Substeps.Count(a => a.HasError) == 1, "One pre-request step should fail");
                Assert.IsTrue(result.Actions.First(s => s.Key == "PostRequestTasks").Substeps.Count(a => !a.HasError) == 2, "Two post-request steps should succeed");
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
                                                                            GetMockTaskConfig("Post Task 2 (on fail)", triggerType: TaskTriggerType.ON_ERROR)
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
            }
            finally
            {
                await certifyManager.DeleteManagedCertificate(managedCertificate.Id);
            }
        }
    }
}
