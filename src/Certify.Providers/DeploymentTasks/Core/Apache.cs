
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks
{
    /// <summary>
    /// Apache specific version of certificate export deployment task
    /// </summary>
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
                IsExperimental = false,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="path_cert", Name="Destination for .crt", IsRequired=true, IsCredential=false, Description="e.g. Path, UNC or /somewhere/server.crt" },
                     new ProviderParameter{ Key="path_key", Name="Destination for .key", IsRequired=true, IsCredential=false, Description="e.g. Path, UNC or /somewhere/server.key"  },
                     new ProviderParameter{ Key="path_chain", Name="Optional Destination for chain (ca.crt)", IsRequired=true, IsCredential=false, Description="e.g. Path, UNC or /somewhere/ca.crt"  },
                }
            };

        }

        public override async Task<List<ActionResult>> Execute(
            ILog log,
            Models.ManagedCertificate managedCert,
            DeploymentTaskConfig settings,
            Dictionary<string, string> credentials,
            bool isPreviewOnly,
            DeploymentProviderDefinition definition =null
        )
        {
            definition = GetDefinition(definition);

            // for each item, execute a certificate export
            var results = new List<ActionResult>();

            settings.Parameters.Add(new ProviderParameterSetting("path", null));
            settings.Parameters.Add(new ProviderParameterSetting("type", null));

            var certPath = settings.Parameters.FirstOrDefault(p => p.Key == "path_cert");
            if (certPath!=null)
            {
                settings.Parameters.Find(p => p.Key == "path").Value = certPath.Value;
                settings.Parameters.Find(p => p.Key == "type").Value = "pemcrt";
                results.AddRange(await base.Execute(log, managedCert, settings, credentials, isPreviewOnly, definition));
            }

            var keyPath = settings.Parameters.FirstOrDefault(p => p.Key == "path_key");
            if (keyPath != null && !results.Any(r=>r.IsSuccess==false))
            {
                settings.Parameters.Find(p => p.Key == "path").Value = keyPath.Value;
                settings.Parameters.Find(p => p.Key == "type").Value = "pemkey";
                results.AddRange(await base.Execute(log, managedCert, settings, credentials, isPreviewOnly, definition));
            }

            var chainPath = settings.Parameters.FirstOrDefault(p => p.Key == "path_chain");
            if (chainPath != null && !results.Any(r => r.IsSuccess == false))
            {
                settings.Parameters.Find(p => p.Key == "path").Value = chainPath.Value;
                settings.Parameters.Find(p => p.Key == "type").Value = "pemchain";
                results.AddRange(await base.Execute(log, managedCert, settings, credentials, isPreviewOnly, definition));
            }

            return results;
        }

        public async override Task<List<ActionResult>> Validate(ManagedCertificate managedCert, DeploymentTaskConfig settings, Dictionary<string, string> credentials, DeploymentProviderDefinition definition = null)
        {

            // for each item, execute a certificate export
            var results = new List<ActionResult>();

            settings.Parameters.Add(new ProviderParameterSetting("path", null));
            settings.Parameters.Add(new ProviderParameterSetting("type", null));

            var certPath = settings.Parameters.FirstOrDefault(p => p.Key == "path_cert");
            if (certPath != null)
            {
                settings.Parameters.Find(p => p.Key == "path").Value = certPath.Value;
                settings.Parameters.Find(p => p.Key == "type").Value = "pemcrt";
                results.AddRange(await base.Validate(managedCert, settings, credentials, definition));
            }

            var keyPath = settings.Parameters.FirstOrDefault(p => p.Key == "path_key");
            if (keyPath != null && !results.Any(r => r.IsSuccess == false))
            {
                settings.Parameters.Find(p => p.Key == "path").Value = keyPath.Value;
                settings.Parameters.Find(p => p.Key == "type").Value = "pemkey";
                results.AddRange(await base.Validate(managedCert, settings, credentials, definition));
            }

            var chainPath = settings.Parameters.FirstOrDefault(p => p.Key == "path_chain");
            if (chainPath != null && !results.Any(r => r.IsSuccess == false))
            {
                settings.Parameters.Find(p => p.Key == "path").Value = chainPath.Value;
                settings.Parameters.Find(p => p.Key == "type").Value = "pemchain";
                results.AddRange(await base.Validate(managedCert, settings, credentials, definition));
            }

            return results;
        }

    }
}
