using Certify.Config;

namespace Certify.Models.Utils
{
    public class DeploymentTaskValidationInfo
    {
        public ManagedCertificate ManagedCertificate { get; set; }
        public DeploymentTaskConfig TaskConfig { get; set; }
    }
}
