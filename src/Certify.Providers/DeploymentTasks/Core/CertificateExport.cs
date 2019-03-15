using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config;
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
                        new ProviderParameter{ Key="exportoptions", Name="Export As", IsRequired=true, IsCredential=false, Value="pfx", OptionsList="pfx=PFX (PKCX#12);pem=PEM, Primary + Intermediates + Private Key; crtpem=PEM, Primary + Intermediates" },

                    },
            };
        }

        public override async Task<ActionResult> Execute(
                ILog log,
                Models.ManagedCertificate managedCert,
                DeploymentTaskConfig settings,
                Dictionary<string, string> credentials,
                bool isPreviewOnly
            )
        {
            var definition = GetDefinition();
            // prepare collection of files in the required formats

            // copy files to the required destination (local, UNC or SFTP)

            //var sftp = new Deployment.Core.Shared.SftpClient(config);

            var pfxData = File.ReadAllBytes(managedCert.CertificatePath);

            // sftp
            var sshConfig = SshClient.GetConnectionConfig(settings, credentials);

            var sftp = new SftpClient(sshConfig);

            var remotePath = settings.Parameters.FirstOrDefault(c => c.Key == "path")?.Value.Trim();

            if (isPreviewOnly)
            {
                log.Information($"{definition.Title}: (Preview) would copy file via sftp to {remotePath}");
            }
            else
            {
                // copy via sftp
                var copiedOK = sftp.CopyLocalToRemote(new Dictionary<string, string>
                {
                    {managedCert.CertificatePath, remotePath }
                });

                log.Information($"{definition.Title}: copied file via sftp to {remotePath}");
            }

            return await Task.FromResult(new ActionResult { IsSuccess = true });
        }

    }
}
