using System.IO;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.DeploymentTasks
{
    public class CentralizedCertificateStore
    {
        public ProviderParameter StorePath = new ProviderParameter();

        public async Task<ActionResult> Deploy(ILog log, Models.ManagedCertificate managedCert)
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
