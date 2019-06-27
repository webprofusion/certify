
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
                Title = "Webhook",
                IsExperimental = true,
                Description = "Call a custom webhook on renewal success or failure",
                EnableRemoteOptions = false,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="url", Name="Webhook URL", IsRequired=true, IsCredential=false  },
                }
            };
        }
    }
}
