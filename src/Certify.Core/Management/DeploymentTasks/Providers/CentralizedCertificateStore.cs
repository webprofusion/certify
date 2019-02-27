using System.IO;
using System.Threading.Tasks;
using Certify.Core.Management.DeploymentTasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks
{
    public class CentralizedCertificateStore : DeploymentTaskProviderBase, IDeploymentTaskProvider
    {
        public ProviderParameter StorePath = new ProviderParameter();
        public static new DeploymentProviderDefinition Definition { get; }

        static CentralizedCertificateStore()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.CCS",
                Title= " Deploy to Centralized Certificate Store (CCS) (experimental)",
                Description = "Deploy latest certificate to Windows Centralized Certificate Store",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {

                }
            };
        }

        public override async Task<ActionResult> Execute(ILog log, Models.ManagedCertificate managedCert, bool isPreviewOnly)
        {
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
                    log.Information($"CCS: Storing PFX as {filename}");
                    File.WriteAllBytes(filename, pfxData);
                }
            }

            return await Task.FromResult(new ActionResult { IsSuccess = true });
        }


    }
}
