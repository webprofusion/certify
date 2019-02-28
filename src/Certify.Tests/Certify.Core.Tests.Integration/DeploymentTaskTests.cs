using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Core.Management.DeploymentTasks;
using Certify.Management;
using Certify.Models;
using Certify.Providers.Deployment.Core.Shared;
using Certify.Providers.DeploymentTasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Serilog;
using SimpleImpersonation;

namespace Certify.Core.Tests.Integration
{
    [TestClass]
    public class DeploymentTaskTests : IntegrationTestBase
    {
        [Ignore]
        [TestMethod, TestCategory("TestCredentials")]
        public async Task CreateTestCredentials()
        {
            var credentialsManager = new CredentialsManager
            {
                StorageSubfolder = "credentials\\test"
            };

            var secrets = new Dictionary<string, string>
            {
                { "username", "testuser" },
                { "password", "testuser" }
            };

            var storedCred = await credentialsManager.UpdateCredential(new Models.Config.StoredCredential
            {
                StorageKey = "atestsshuser",
                Title = "Test: SSH",
                DateCreated = DateTime.Now,
                ProviderType = "SSH",
                Secret = JsonConvert.SerializeObject(secrets)
            });

            secrets = new Dictionary<string, string>
            {
                { "username", "testuser" },
                { "password", "testuser" }
            };

            storedCred = await credentialsManager.UpdateCredential(new Models.Config.StoredCredential
            {
                StorageKey = "atestwindowsuser",
                Title = "Test: UNC testuser",
                DateCreated = DateTime.Now,
                ProviderType = "Windows",
                Secret = JsonConvert.SerializeObject(secrets)
            });

        }

        [TestMethod, TestCategory("NetworkFileCopy")]
        public async Task TestWindowsNetworkFileCopy()
        {
            var destPath = @"\\localhost\ccs";
            
            var credentialsManager = new CredentialsManager
            {
                StorageSubfolder = "credentials\\test"
            };

            var storedCred = await credentialsManager.GetUnlockedCredentialsDictionary("atestwindowsuser");
          
            // create a test temp file
            var tmpPath = Path.GetTempFileName();
            File.WriteAllText(tmpPath, "This is a test temp file");
            var files = new Dictionary<string, string>
            {
                { tmpPath, destPath+ @"\test-copy.txt" }
            };

            var credentials = new UserCredentials(storedCred["username"], storedCred["password"]);

            var client = new WindowsNetworkFileClient(credentials);

            // test file list
            var fileList = client.ListFiles(destPath);
            Assert.IsTrue(fileList.Count > 0);

            // test file copy
            var copiedOK = client.CopyLocalToRemote(files);

            File.Delete(tmpPath);

            Assert.IsTrue(copiedOK);

        }

        [TestMethod, TestCategory("NetworkFileCopy")]
        public async Task TestSftpFileCopy()
        {
            var credentialsManager = new CredentialsManager
            {
                StorageSubfolder = "credentials\\test"
            };

            string destPath = "/home/bitnami";

            var storedCred = await credentialsManager.GetUnlockedCredentialsDictionary("atestsshuser");

            var credentials = new UserCredentials(storedCred["username"], storedCred["password"]);

            // create a test temp file
            var tmpPath = Path.GetTempFileName();
            File.WriteAllText(tmpPath, "This is a test temp file");

            var files = new Dictionary<string, string>
            {
                { tmpPath, destPath+"/testfilecopy.txt" }
            };

            var client = new SftpClient(new SftpConnectionConfig
            {
                Host = "34.215.2.160",
                Passphrase = storedCred["password"],
                Port = 22,
                Username = storedCred["username"],
                PrivateKeyPath = @"C:\Temp\Certify\ssh\private.key"
            });

            // test file list
            var fileList = client.ListFiles(destPath);
            Assert.IsTrue(fileList.Count > 0);

            // test file copy
            var copiedOK = client.CopyLocalToRemote(files);

            Assert.IsTrue(copiedOK);

            File.Delete(tmpPath);

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
