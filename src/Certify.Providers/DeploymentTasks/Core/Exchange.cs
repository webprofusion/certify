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
                Title = "Deploy to Microsoft Exchange (2013 or higher)",
                IsExperimental = true,
                Description = "Deploy latest certificate to MS Exchange Services",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                      new ProviderParameter{ Key="services", Name="Services", IsRequired=true, IsCredential=false},
                      new ProviderParameter{ Key="restart", Name="Restart Exchange Services", IsRequired=true, IsCredential=false, Type= OptionType.Boolean},
                }
            };
        }

    }
}
