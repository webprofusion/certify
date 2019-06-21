using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.Deployment.Core.Shared;
using SimpleImpersonation;

namespace Certify.Providers.DeploymentTasks
{
    public class CentralizedCertificateStore : DeploymentTaskProviderBase, IDeploymentTaskProvider
    {
        public static new DeploymentProviderDefinition Definition { get; }

        static CentralizedCertificateStore()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.CCS",
                Title = "Deploy to Centralized Certificate Store (CCS)",
                IsExperimental = true,
                Description = "Deploy latest certificate to Windows Centralized Certificate Store",
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                    new ProviderParameter{
                        Key ="path",
                        Name ="Destination Path",
                        IsRequired =true,
                        IsCredential =false,
                        Description="UNC Path or Local Share"
                    },
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

            UserCredentials windowsCredentials = null;

            if (credentials != null && credentials.Count > 0)
            {
                try
                {
                    var username = credentials["username"];
                    var pwd = credentials["password"];
                    credentials.TryGetValue("domain", out var domain);

                    if (domain != null)
                    {
                        windowsCredentials = new UserCredentials(domain, username, pwd);
                    }
                    else
                    {
                        windowsCredentials = new UserCredentials(username, pwd);
                    }
                }
                catch
                {
                    return new List<ActionResult>{
                        new ActionResult { IsSuccess = false, Message = "CCS Export task with Windows Credentials requires username and password." }
                    };
                }
            }

            var windowsFileClient = new WindowsNetworkFileClient(windowsCredentials);

            var domains = managedCert.GetCertificateDomains();

            var fileList = new Dictionary<string, string>();

            var destinationPath = settings.Parameters?.FirstOrDefault(d => d.Key == "path")?.Value;

            foreach (var domain in domains)
            {

                // normalise wildcard domains to _.domain.com for file store
                var targetDomain = domain.Replace('*', '_');

                // attempt save to store, which may be a network UNC path or otherwise authenticated resource

                if (!string.IsNullOrWhiteSpace(destinationPath))
                {
                    var filename = Path.Combine(destinationPath.Trim(), domain + ".pfx");

                    fileList.Add(managedCert.CertificatePath, filename);

                    log.Information($"{Definition.Title}: Storing PFX as {filename}");
                }
            }

            if (fileList.Count == 0)
            {
                return new List<ActionResult>{
                    new ActionResult { IsSuccess = true, Message = $"{Definition.Title}: Nothing to copy." }
                   };
            }
            else
            {
                if (!isPreviewOnly)
                {
                    windowsFileClient.CopyLocalToRemote(fileList);
                }

            }

            return new List<ActionResult>{
                await Task.FromResult(new ActionResult { IsSuccess = true, Message = "File copying completed" })
            };
        }
    }
}

