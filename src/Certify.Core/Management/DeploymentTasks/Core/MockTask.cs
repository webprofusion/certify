using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks.Core
{
    public class MockTask : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition { get; }
        public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition = null) => (currentDefinition ?? Definition);

        static MockTask()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Mock",
                Title = "Mock Task",
                IsExperimental = true,
                UsageType = DeploymentProviderUsage.Any,
                SupportedContexts = DeploymentContextType.LocalAsService,
                Description = "Used to test task execution success, failure and logging",
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter{ Key="message", Name="Message", IsRequired=true, IsCredential=false, Description="Test message"  },
                    new ProviderParameter{ Key="throw", Name="Throw on demand", IsRequired=true, IsCredential=false, Description="If true, throw exception during task", Type= OptionType.Boolean  }
                }
            };
        }

        /// <summary>
        /// Execute a local powershell script
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCert"></param>
        /// <param name="settings"></param>
        /// <param name="credentials"></param>
        /// <param name="isPreviewOnly"></param>
        /// <returns></returns>
        public async Task<List<ActionResult>> Execute(DeploymentTaskExecutionParams execParams)
        {

            var msg = execParams.Settings.Parameters.FirstOrDefault(c => c.Key == "message")?.Value;

            bool.TryParse(execParams.Settings.Parameters.FirstOrDefault(c => c.Key == "throw")?.Value, out var shouldThrow);

            if (string.IsNullOrEmpty(msg))
            {
                // fail task
                execParams.Log?.Warning($"Mock Task says: <msg not supplied, task will fail>");

                return await Task.FromResult(new List<ActionResult> { new ActionResult("Mock Task message not supplied.", false) });
            }
            else
            {
                if (shouldThrow)
                {
                    throw new System.Exception($"Mock task should throw: {msg}");
                }
                else
                {
                    execParams.Log?.Information($"Mock Task says: {msg}");
                    return await Task.FromResult(new List<ActionResult> {
                        new ActionResult($"{msg}.", true),
                        new ActionResult($"MockTaskWorkCompleted.", true)
                    });
                }
            }
        }

        public async Task<List<ActionResult>> Validate(DeploymentTaskExecutionParams execParams)
        {
            var results = new List<ActionResult> { };
            foreach (var p in execParams.Definition.ProviderParameters)
            {

                if (!execParams.Settings.Parameters.Exists(s => s.Key == p.Key) && p.IsRequired)
                {
                    results.Add(new ActionResult($"Required parameter not supplied: { p.Name}", false));
                }
            }

            return await Task.FromResult(results);
        }
    }
}
