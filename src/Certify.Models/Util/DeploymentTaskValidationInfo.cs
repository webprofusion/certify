using Certify.Config;

namespace Certify.Models.Utils
{
    public class DeploymentTaskValidationInfo
    {
        public ManagedCertificate ManagedCertificate { get; set; }
        public DeploymentTaskConfig TaskConfig { get; set; }

        public DeploymentTaskValidationInfo(ManagedCertificate managedCertificate, DeploymentTaskConfig taskConfig)
        {
            ManagedCertificate = managedCertificate;
            TaskConfig = taskConfig;
        }
    }
}
