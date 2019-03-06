
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
                Title= "Deploy to Tomcat",
                IsExperimental = true,
                Description = "Deploy latest certificate to a local or remote Tomcat server",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="path_cert", Name="Destination for .crt", IsRequired=true, IsCredential=false  },
                     new ProviderParameter{ Key="path_key", Name="Destination for .key", IsRequired=true, IsCredential=false  },
                }
            };
        }

    }
}
