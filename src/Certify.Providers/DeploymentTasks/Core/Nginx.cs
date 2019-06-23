using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks
{
    /// <summary>
    /// nginx specific version of certificate export deployment task
    /// </summary>
    public class Nginx : CertificateExport, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static Nginx()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Nginx",
                Title = "Deploy to nginx",
                IsExperimental = true,
                Description = "Deploy latest certificate to a local or remote nginx server",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                    new ProviderParameter{ Key="path_cert", Name="Destination for .crt", IsRequired=true, IsCredential=false, Description="e.g. Path, UNC or /somewhere/server.crt" },
                    new ProviderParameter{ Key="path_key", Name="Destination for .key", IsRequired=true, IsCredential=false, Description="e.g. Path, UNC or /somewhere/server.key"  },
                }
            };
        }

        public override async Task<List<ActionResult>> Execute(
         ILog log,
         Models.ManagedCertificate managedCert,
         DeploymentTaskConfig settings,
         Dictionary<string, string> credentials,
         bool isPreviewOnly,
         DeploymentProviderDefinition definition = null
     )
        {
            definition = GetDefinition(definition);

            // for each item, execute a certificate export
            var results = new List<ActionResult>();

            settings.Parameters.Add(new ProviderParameterSetting("path", null));
            settings.Parameters.Add(new ProviderParameterSetting("type", null));

            var certPath = settings.Parameters.FirstOrDefault(p => p.Key == "path_cert");
            if (certPath != null)
            {
                settings.Parameters.Find(p => p.Key == "path").Value = certPath.Value;
                settings.Parameters.Find(p => p.Key == "type").Value = "pemcrtpartialchain";
                results.AddRange(await base.Execute(log, managedCert, settings, credentials, isPreviewOnly, definition));
            }

            var keyPath = settings.Parameters.FirstOrDefault(p => p.Key == "path_key");
            if (keyPath != null && !results.Any(r => r.IsSuccess == false))
            {
                settings.Parameters.Find(p => p.Key == "path").Value = keyPath.Value;
                settings.Parameters.Find(p => p.Key == "type").Value = "pemkey";
                results.AddRange(await base.Execute(log, managedCert, settings, credentials, isPreviewOnly, definition));
            }

            return results;
        }
    }
}
