using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Core.Management
{
    public interface IBindingDeploymentManager
    {
        Task<List<ActionStep>> StoreAndDeploy(IBindingDeploymentTarget deploymentTarget, ManagedCertificate managedCertificate, string pfxPath, string pfxPwd, bool isPreviewOnly);
        Task<List<ActionStep>> UpdateWebBinding(IBindingDeploymentTarget deploymentTarget, IBindingDeploymentTargetItem site, List<BindingInfo> existingBindings, string certStoreName, byte[] certificateHash, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null, bool alwaysRecreateBindings = false, bool isPreviewOnly = false);
        Task<List<ActionStep>> UpdateFtpBinding(IBindingDeploymentTarget deploymentTarget, IBindingDeploymentTargetItem site, List<BindingInfo> existingBindings, string certStoreName, string certificateHash, int port, string host, string ipAddress, bool isPreviewOnly = false);
    }
}
