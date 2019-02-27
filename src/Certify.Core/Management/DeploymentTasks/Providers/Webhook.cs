using Certify.Core.Management.DeploymentTasks;
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks
{
    public class Webhook : DeploymentTaskProviderBase, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Webhook()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Webhook",
                Title = "Call Webhook (experimental)",
                Description = "Call a custom webhook on renewal sucess or failure",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {

                }
            };
        }

        
    }
}
