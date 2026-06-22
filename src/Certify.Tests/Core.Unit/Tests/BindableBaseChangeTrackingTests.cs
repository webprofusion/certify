using System.Collections.ObjectModel;
using Certify.Config;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class BindableBaseChangeTrackingTests
    {
        [TestMethod]
        public void ResetIsChanged_AttachesExistingNestedBindableProperties()
        {
            var managedCertificate = new ManagedCertificate();
            managedCertificate.ResetIsChanged(false);

            managedCertificate.RequestConfig.PrimaryDomain = "example.com";

            Assert.IsTrue(managedCertificate.IsChanged);
        }

        [TestMethod]
        public void ResetIsChanged_AttachesExistingDeploymentTaskItems()
        {
            var managedCertificate = new ManagedCertificate
            {
                PreRequestTasks = new ObservableCollection<DeploymentTaskConfig>
                {
                    new DeploymentTaskConfig
                    {
                        Id = "task-1",
                        TaskName = "Original Task"
                    }
                }
            };

            managedCertificate.ResetIsChanged(false);

            managedCertificate.PreRequestTasks[0].TaskName = "Updated Task";

            Assert.IsTrue(managedCertificate.IsChanged);
        }
    }
}
