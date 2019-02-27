using Certify.Core.Management.DeploymentTasks;
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks
{
    public class Apache : CertificateExport, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Apache()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Apache",
                Title = "Deploy to Apache (experimental)",
                Description = "Deploy latest certificate to Local or Remote Apache Server",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {

                }
            };
        }

    }
}
