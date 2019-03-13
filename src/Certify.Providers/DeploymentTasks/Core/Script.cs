
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.Deployment.Core.Shared;

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

        /// <summary>
        /// Execute a script or program either locally or remotely, windows or ssh
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCert"></param>
        /// <param name="settings"></param>
        /// <param name="credentials"></param>
        /// <param name="isPreviewOnly"></param>
        /// <returns></returns>
        public override async Task<ActionResult> Execute(
          ILog log,
          Models.ManagedCertificate managedCert,
          DeploymentTaskConfig settings,
          Dictionary<string, string> credentials,
          bool isPreviewOnly
          )
        {
            return new ActionResult { IsSuccess = true, Message = "Nothing to do" };
        }

    }
}
