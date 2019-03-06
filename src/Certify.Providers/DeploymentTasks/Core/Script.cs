
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks
{
    public class Script : DeploymentTaskProviderBase, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Script()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Script",
                Title = "Run...",
                IsExperimental = true,
                Description = "Run a program, batch file or custom script",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                    new ProviderParameter{ Key="path", Name="Path to Program/Script", IsRequired=true, IsCredential=false  },
                    new ProviderParameter{ Key="args", Name="Arguments (optional)", IsRequired=false, IsCredential=false  },
                }
            };
        }

        
    }
}
