using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks.Core
{
    public class IIS : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition { get; }
        public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition) => (currentDefinition ?? Definition);

        public Task<List<ActionResult>> Execute(ILog log, object subject, DeploymentTaskConfig settings, Dictionary<string, string> credentials, bool isPreviewOnly, DeploymentProviderDefinition definition)
        {
            throw new System.NotImplementedException();
        }

        public Task<List<ActionResult>> Validate(object subject, DeploymentTaskConfig settings, Dictionary<string, string> credentials, DeploymentProviderDefinition definition)
        {
            throw new System.NotImplementedException();
        }

        static IIS()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.IIS",
                Title = "Deploy to IIS (Local Machine)",
                Description = "Deploy certificate to one or more local IIS sites",
                UsageType = DeploymentProviderUsage.PostRequest,
                IsExperimental = true,
                EnableRemoteOptions = false,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {

                }
            };
        }
    }
}
