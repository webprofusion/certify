
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks
{
    public class Tomcat : CertificateExport, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Tomcat()
        {

            /*
             * https://tomcat.apache.org/tomcat-8.5-doc/ssl-howto.html
             * Most instructions refer to generating a CSR and using a keystore, however tomcat can consume the normal PFX
             * From Tomcat installation directory, edit server.xml
             * Add or Edit connector on port 443 pointing to .pfx

                <Connector port="443" ... scheme="https" secure="true"
                    SSLEnabled="true"
                    sslProtocol="TLS"
                    keystoreFile="your_certificate.pfx"
                    keystorePass="" keystoreType="PKCS12"/>
            */
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Tomcat",
                Title= "Deploy to Tomcat",
                IsExperimental = true,
                Description = "Deploy latest certificate to a local or remote Tomcat server",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="path_pfx", Name="Destination Path", IsRequired=true, IsCredential=false , Description="Local/remote path to copy PFX file to e.g /usr/local/ssl/server.pfx"},
                }
            };
        }

    }
}
