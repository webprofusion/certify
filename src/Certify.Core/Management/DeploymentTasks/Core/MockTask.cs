using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models.Config;
using Certify.Models.Providers;

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
        public async Task<List<ActionResult>> Execute(
          ILog log,
          object subject,
          DeploymentTaskConfig settings,
          Dictionary<string, string> credentials,
          bool isPreviewOnly,
          DeploymentProviderDefinition definition,
          CancellationToken cancellationToken
          )
        {

            var msg = settings.Parameters.FirstOrDefault(c => c.Key == "message")?.Value;

            bool.TryParse(settings.Parameters.FirstOrDefault(c => c.Key == "throw")?.Value, out var shouldThrow);

            if (string.IsNullOrEmpty(msg))
            {
                // fail task
                log?.Warning($"Mock Task says: <msg not supplied, task will fail>");

                return new List<ActionResult> { new ActionResult("Mock Task message not supplied.", false) };
            }
            else
            {
                if (shouldThrow)
                {
                    throw new System.Exception($"Mock task should throw: {msg}");
                }
                else
                {
                    log?.Information($"Mock Task says: {msg}");
                    return new List<ActionResult> { 
                        new ActionResult($"{msg}.", true),
                        new ActionResult($"MockTaskWorkCompleted.", true)
                    };
                }
            }
        }

        public async Task<List<ActionResult>> Validate(object subject, DeploymentTaskConfig settings, Dictionary<string, string> credentials, DeploymentProviderDefinition definition)
        {
            var results = new List<ActionResult> { };
            foreach (var p in definition.ProviderParameters)
            {

                if (!settings.Parameters.Exists(s => s.Key == p.Key) && p.IsRequired)
                {
                    results.Add(new ActionResult($"Required parameter not supplied: { p.Name}", false));
                }
            }
            return results;
        }
    }
}
