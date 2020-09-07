using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks.Core
{
    public class IIS : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition { get; }
        public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition) => (currentDefinition ?? Definition);

        public Task<List<ActionResult>> Execute(DeploymentTaskExecutionParams execParams)
        {
            throw new System.NotImplementedException();
        }

        public Task<List<ActionResult>> Validate(DeploymentTaskExecutionParams execParams)
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
                UsageType = DeploymentProviderUsage.Disabled,
                IsExperimental = true,
                SupportedContexts = DeploymentContextType.LocalAsService | DeploymentContextType.LocalAsUser,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {

                }
            };
        }
    }
}
