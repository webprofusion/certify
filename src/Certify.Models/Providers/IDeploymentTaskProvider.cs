using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DeploymentTasks
{
    public interface IDeploymentTaskProvider
    {

        Task<List<ActionResult>> Execute(
            ILog log,
            ManagedCertificate managedCert,
            DeploymentTaskConfig settings,
            Dictionary<string, string> credentials,
            bool isPreviewOnly,
            DeploymentProviderDefinition definition
            );

        DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition = null);

        Task<List<ActionResult>> Validate(ManagedCertificate managedCert,
            DeploymentTaskConfig settings,
            Dictionary<string, string> credentials,
            DeploymentProviderDefinition definition);
    }
}
