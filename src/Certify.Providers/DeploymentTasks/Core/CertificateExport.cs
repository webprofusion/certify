using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.Deployment.Core.Shared;

namespace Certify.Providers.DeploymentTasks
{
    public class CertificateExport : DeploymentTaskProviderBase, IDeploymentTaskProvider
    {
        public ProviderParameter StorePath = new ProviderParameter();

        public static new DeploymentProviderDefinition Definition { get; }

        static CertificateExport()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.CertificateExport",
                Title = "Export Certificate",
                IsExperimental = true,
                Description = "Deploy latest certificate to a file (locally or remote)",
                ProviderParameters =

                    new List<ProviderParameter>{
                         new ProviderParameter{ Key="path", Name="Destination Path", IsRequired=true, IsCredential=false,  },
                        new ProviderParameter{ Key="exportoptions", Name="Export Type", IsRequired=true, IsCredential=false, Value="pfx",OptionsList="pfx,crt,key" },
                        new ProviderParameter{ Key="remotehost", Name="Remote Hostname or IP", IsRequired=false, IsCredential=false},
                        /*
                        new ProviderParameter{ Key="username", Name="User Name", IsRequired=false, IsCredential = true, IsPassword = false },
                        new ProviderParameter{ Key="password", Name="Password", IsRequired = false, IsCredential = true, IsPassword = true},
                        new ProviderParameter{ Key="privatekey", Name="Private Key Path (if SSH)", IsRequired = false, IsCredential = true, IsPassword = false},*/
                    },
            };
        }

        public override async Task<ActionResult> Execute(ILog log, Models.ManagedCertificate managedCert, bool isPreviewOnly)
        {
            // prepare colelction of files in the required formats

            // copy files to the required destination (local, UNC or SFTP)

            // execute remote command?

            var config = new SftpConnectionConfig { };

            //var sftp = new Deployment.Core.Shared.SftpClient(config);
            

            var pfxData = File.ReadAllBytes(managedCert.CertificatePath);

            var domains = managedCert.GetCertificateDomains();

            foreach (var domain in domains)
            {
                // normalise wildcard domains to _.domain.com for file store
                var targetDomain = domain.Replace('*', '_');

                // attempt save to store
                var storePath = StorePath.Value;

                if (!string.IsNullOrWhiteSpace(StorePath.Value))
                {
                    //TODO: check unicode domains
                    var filename = Path.Combine(storePath, domain + ".pfx");
                    if (isPreviewOnly)
                    {
                        // preview copy
                        log.Information($"CCS: (Preview) would store PFX as {filename}");
                        File.WriteAllBytes(filename, pfxData);
                    }
                    else
                    {
                        // perform copy
                        log.Information($"CCS: Storing PFX as {filename}");
                        File.WriteAllBytes(filename, pfxData);
                    }

                }
            }

            // copy via sftp

            return await Task.FromResult(new ActionResult { IsSuccess = true });
        }




    }
}
