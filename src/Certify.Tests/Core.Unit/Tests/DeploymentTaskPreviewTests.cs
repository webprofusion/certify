using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Providers.DeploymentTasks;
using Certify.Providers.DeploymentTasks.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Previewing a task only validates it, and returns what the provider reports from Validate
    /// </summary>
    [TestClass]
    public class DeploymentTaskPreviewTests
    {
        private class FakeProvider : IDeploymentTaskProvider
        {
            public bool Executed { get; private set; }
            public List<ActionResult> ValidationResults { get; set; } = new List<ActionResult>();

            public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition = null) => currentDefinition ?? new DeploymentProviderDefinition { Id = "Test.Fake", Title = "Fake" };

            public Task<List<ActionResult>> Execute(DeploymentTaskExecutionParams execParams)
            {
                Executed = true;
                return Task.FromResult(new List<ActionResult> { new ActionResult("Executed", true) });
            }

            public Task<List<ActionResult>> Validate(DeploymentTaskExecutionParams execParams) => Task.FromResult(ValidationResults);
        }

        private static Task<List<ActionResult>> Preview(IDeploymentTaskProvider provider, DeploymentTaskConfig config = null)
        {
            var task = new DeploymentTask(provider, config ?? new DeploymentTaskConfig { TaskName = "Test", Parameters = new List<ProviderParameterSetting>() }, null);
            return task.Execute(null, null, new ManagedCertificate(), new DeploymentContext(), isPreviewOnly: true, CancellationToken.None);
        }

        [TestMethod, Description("A provider with nothing to report previews as valid, and is not executed")]
        public async Task TestPreviewWithNoValidationResults()
        {
            var provider = new FakeProvider();

            var results = await Preview(provider);

            Assert.IsFalse(provider.Executed);
            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].IsSuccess);
            Assert.AreEqual("Task is valid and ready to execute.", results[0].Message);
        }

        [TestMethod, Description("Successful preview messages from Validate are returned rather than replaced")]
        public async Task TestPreviewReturnsProviderPreviewMessages()
        {
            var provider = new FakeProvider { ValidationResults = new List<ActionResult> { new ActionResult("Would deploy to device", true) } };

            var results = await Preview(provider);

            Assert.IsFalse(provider.Executed);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Would deploy to device", results[0].Message);
        }

        [TestMethod, Description("Validation failures are returned when previewing")]
        public async Task TestPreviewReturnsValidationFailures()
        {
            var provider = new FakeProvider { ValidationResults = new List<ActionResult> { new ActionResult("Bad setting", false) } };

            var results = await Preview(provider);

            Assert.IsFalse(provider.Executed);
            Assert.AreEqual(1, results.Count);
            Assert.IsFalse(results[0].IsSuccess);
        }

        [TestMethod, Description("The Certificate Store task describes where it would store the certificate when previewed")]
        public async Task TestCertificateStorePreview()
        {
            var config = new DeploymentTaskConfig
            {
                TaskName = "Store",
                Parameters = new List<ProviderParameterSetting> { new("storetype", "WebHosting") }
            };

            var results = await Preview(new CertificateStore(), config);

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].IsSuccess);
            Assert.AreEqual("Would store certificate in Local Certificate Store [webhosting]", results[0].Message);

            config.Parameters = new List<ProviderParameterSetting> { new("storetype", "other") };
            results = await Preview(new CertificateStore(), config);

            Assert.AreEqual(1, results.Count);
            Assert.IsFalse(results[0].IsSuccess);
        }

        [TestMethod, Description("The Certificate Store task only offers the local service context, as it does not use credentials")]
        public void TestCertificateStoreContexts()
        {
            Assert.AreEqual(DeploymentContextType.LocalAsService, CertificateStore.Definition.SupportedContexts);
        }
    }
}
