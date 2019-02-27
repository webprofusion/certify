using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Core.Management.DeploymentTasks;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Certify.Core.Tests.Integration
{
    [TestClass]
    public class DeploymentTaskTests : IntegrationTestBase
    {
        [TestMethod, TestCategory("NetworkCredentials")]
        public void Test()
        {
            //TODO get test credential
            AppDomain.CurrentDomain.SetPrincipalPolicy(PrincipalPolicy.WindowsPrincipal);

            var idnt = new WindowsIdentity(@"testuser", "testuser");

            var context = idnt.Impersonate();

            // attempt read of files in destination
            // attempt file copy
            File.Copy(@"\\localhost\ccs\test.txt", @"\\localhost\ccs\test-copy.txt", true);

            context.Undo();
        }

        [TestMethod, TestCategory("Export")]
        public async Task TestGetAllDeploymentTaskProviders()
        {
            var allProviders = DeploymentTaskProviderFactory.GetDeploymentTaskProviders();

            Assert.IsTrue(allProviders.Select(p => p.Title).Distinct().Count() == allProviders.Count);
            Assert.IsTrue(allProviders.Select(p => p.Id).Distinct().Count() == allProviders.Count);
            Assert.IsTrue(allProviders.Select(p => p.Description).Distinct().Count() == allProviders.Count);


        }

        [TestMethod, TestCategory("Export")]
        public async Task TestPFxExport()
        {

            var logImp = new LoggerConfiguration()
              .WriteTo.Debug()
              .CreateLogger();

            var log = new Loggy(logImp);

            var deploymentTasks = new List<DeploymentTask>();

            var config = new DeploymentTaskConfig
            {
                TaskType = Providers.DeploymentTasks.CertificateExport.Definition.Id.ToLower(),
                IsDeferred = false,
                TaskName = "A test task"
            };

            var provider = DeploymentTaskProviderFactory.Create(Providers.DeploymentTasks.CertificateExport.Definition.Id.ToLower());
            var t = new DeploymentTask(provider, config);

            deploymentTasks.Add(t);

            // perform preview deployments
            var managedCert = GetMockManagedCertificate("DeploymentTest", "123", PrimaryTestDomain, PrimaryIISRoot);
            foreach (var task in deploymentTasks)
            {
                var result = await task.Execute(log, managedCert, true);

            }

        }
    }
}
