using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Core.Management.DeploymentTasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Providers.DeploymentTasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tasks which require a Management Hub only run on the hub or an instance joined to one
    /// </summary>
    [TestClass]
    public class DeploymentTaskHubRequirementTests
    {
        private const string HubOnlyTaskId = "Certify.Tests.DeploymentTasks.HubOnly";

        private sealed class HubOnlyTask : IDeploymentTaskProvider
        {
            public int ExecuteCount { get; private set; }

            public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition = null) => new DeploymentProviderDefinition
            {
                Id = HubOnlyTaskId,
                Title = "Hub Only",
                RequiresManagementHub = true
            };

            public Task<List<ActionResult>> Execute(DeploymentTaskExecutionParams execParams)
            {
                ExecuteCount++;
                return Task.FromResult(new List<ActionResult> { new ActionResult("Deployed", true) });
            }

            public Task<List<ActionResult>> Validate(DeploymentTaskExecutionParams execParams) => Task.FromResult(new List<ActionResult>());
        }

        private sealed class HubOnlyPlugin : IDeploymentTaskProviderPlugin
        {
            public HubOnlyTask Task { get; } = new HubOnlyTask();

            public List<DeploymentProviderDefinition> GetProviders(Type pluginType) => new List<DeploymentProviderDefinition> { Task.GetDefinition() };

            public IDeploymentTaskProvider GetProvider(Type pluginType, string id) => id.Equals(HubOnlyTaskId, StringComparison.OrdinalIgnoreCase) ? Task : null;
        }

        private static async Task<(List<ActionStep> Steps, DeploymentTaskConfig Config, HubOnlyTask Task)> Run(bool isManagementHubAvailable)
        {
            var plugin = new HubOnlyPlugin();
            var runner = new DeploymentTaskRunner(
                deploymentTaskProviders: new List<IDeploymentTaskProviderPlugin> { plugin },
                credentialsManager: null,
                context: new DeploymentContext { IsManagementHubAvailable = isManagementHubAvailable });

            var config = new DeploymentTaskConfig
            {
                Id = Guid.NewGuid().ToString(),
                TaskTypeId = HubOnlyTaskId,
                TaskName = "Deploy to device",
                TaskTrigger = TaskTriggerType.ANY_STATUS,
                Parameters = new List<ProviderParameterSetting>()
            };

            var log = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<DeploymentTaskHubRequirementTests>());
            var result = new CertificateRequestResult(new ManagedCertificate { Id = Guid.NewGuid().ToString(), Name = "test" });

            var steps = await runner.Run(log, isPreviewOnly: false, skipDeferredTasks: true, result, new[] { config }, forceTaskExecute: false, evaluateAgainstPrimaryRequestStatus: false);

            return (steps, config, plugin.Task);
        }

        [TestMethod, Description("A hub-only task fails without running on an instance which is not the hub or joined to one")]
        public async Task HubOnlyTaskFailsWithoutHub()
        {
            var (steps, config, task) = await Run(isManagementHubAvailable: false);

            Assert.AreEqual(0, task.ExecuteCount, "The task must not run");
            var step = steps.Find(s => s.Key == config.Id);
            Assert.IsNotNull(step);
            Assert.IsTrue(step.HasError);
            Assert.AreEqual(DeploymentProviderDefinition.RequiresManagementHubMessage, step.Description);
            Assert.AreEqual(RequestState.Error, config.LastRunStatus);
        }

        [TestMethod, Description("A hub-only task runs on the hub or an instance joined to one")]
        public async Task HubOnlyTaskRunsWithHub()
        {
            var (steps, config, task) = await Run(isManagementHubAvailable: true);

            Assert.AreEqual(1, task.ExecuteCount);
            var step = steps.Find(s => s.Key == config.Id);
            Assert.IsNotNull(step);
            Assert.IsFalse(step.HasError, step.Description);
        }
    }
}
