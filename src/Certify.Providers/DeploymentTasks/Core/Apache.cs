
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks
{
    public class Apache : CertificateExport, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Apache()
        {
            // https://httpd.apache.org/docs/2.4/mod/mod_ssl.html#sslcertificatefile

            // SSLCertificateFile : e.g. server.crt - pem encoded certificate(s). At a minimum, the file must include an end-entity (leaf) certificate. Can include intermediates sorted from leaf to root (apache 2.4.8 and higher)
            // SSLCertificateChainFile: e.g. ca.crt - (not required if intermediates etc included in SSLCertificateFile) crt concatentated PEM format, intermediate to root CA certificate
            // SSLCertificateKeyFile : e.g. server.key - pem encoded private key

            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Apache",
                Title = "Deploy to Apache",
                Description = "Deploy latest certificate to Local or Remote Apache Server",
                IsExperimental=true,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="path_cert", Name="Destination for .crt", IsRequired=true, IsCredential=false, Description="e.g. Path, UNC or /somewhere/server.crt" },
                     new ProviderParameter{ Key="path_key", Name="Destination for .key", IsRequired=true, IsCredential=false, Description="e.g. Path, UNC or /somewhere/server.key"  },
                }
            };

        }

    }
}
