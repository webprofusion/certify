using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks
{
    public class IIS : DeploymentTaskProviderBase, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static IIS()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.IIS",
                Title = "Deploy to IIS",
                Description = "Deploy certificate to one or more local IIS sites",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {

                }
            };
        }

        
    }
}
