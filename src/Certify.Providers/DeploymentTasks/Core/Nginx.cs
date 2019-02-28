
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks
{
    public class Nginx : CertificateExport, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Nginx()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Nginx",
                Title = "Deploy to nginx (experimental)",
                Description = "Deploy latest certificate to a local or remote nginx server",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {

                }
            };
        }

        
    }
}
