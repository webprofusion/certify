using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks
{
    public class Exchange : CertificateExport, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Exchange()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Exchange",
                Title = "Deploy to Microsoft Exchange (2010 or higher) (experimental)",
                Description = "Deploy latest certificate to MS Exchange Services",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {

                }
            };
        }

    }
}
