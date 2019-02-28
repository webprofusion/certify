
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks
{
    public class Tomcat : CertificateExport, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Tomcat()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Tomcat",
                Title= "Deploy to Tomcat (experimental)",
                Description = "Deploy latest certificate to a local or remote Tomcat server",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {

                }
            };
        }

    }
}
