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
                { "username", "ubuntu" },
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
                StorageKey = ConfigSettings["TestCredentialsKey_UNC"],
                Title = "Test: UNC testuser",
                DateCreated = DateTime.Now,
                ProviderType = "Windows",
                Secret = JsonConvert.SerializeObject(secrets)
            });

        }

        [TestMethod, TestCategory("NetworkFileCopy")]
        public async Task TestWindowsNetworkFileCopy()
        {
            var destPath = ConfigSettings["TestUNCPath"];

            var credentialsManager = new CredentialsManager
            {
                StorageSubfolder = "credentials\\test"
            };

            var storedCred = await credentialsManager.GetUnlockedCredentialsDictionary(ConfigSettings["TestCredentialsKey_UNC"]);

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

            string destPath = ConfigSettings["TestSSHPath"];

            var storedCred = await credentialsManager.GetUnlockedCredentialsDictionary(ConfigSettings["TestCredentialsKey_SSH"]);

            // var credentials = new UserCredentials(storedCred["username"], storedCred["password"]);

            // create a test temp file
            var tmpPath = Path.GetTempFileName();
            File.WriteAllText(tmpPath, "This is a test temp file");

            var files = new Dictionary<string, string>
            {
                { tmpPath, destPath+"/testfilecopy.txt" }
            };

            var client = new SftpClient(new SshConnectionConfig
            {
                Host = ConfigSettings["TestSSHHost"],
                KeyPassphrase = storedCred["password"],
                Port = 22,
                Username = storedCred["username"],
                PrivateKeyPath = ConfigSettings["TestSSHPrivateKeyPath"]
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
            var allProviders = await DeploymentTaskProviderFactory.GetDeploymentTaskProviders();

            Assert.IsTrue(allProviders.Select(p => p.Title).Distinct().Count() == allProviders.Count);
            Assert.IsTrue(allProviders.Select(p => p.Id).Distinct().Count() == allProviders.Count);
            Assert.IsTrue(allProviders.Select(p => p.Description).Distinct().Count() == allProviders.Count);
        }

        [TestMethod, TestCategory("Export")]
        public async Task TestPFxExport()
        {
            var deploymentTasks = new List<DeploymentTask>();

            var outputFile = ConfigSettings["TestLocalPath"] + "\\test_pfx_export_apache.pfx";

            var config = new DeploymentTaskConfig
            {
                TaskTypeId = Providers.DeploymentTasks.CertificateExport.Definition.Id.ToLower(),
                IsDeferred = false,
                TaskName = "A test pfx export task",
                ChallengeProvider = "Certify.StandardChallenges.Local",

                Parameters = new List<Models.Config.ProviderParameterSetting>
                {
                    new Models.Config.ProviderParameterSetting("path", outputFile),
                    new Models.Config.ProviderParameterSetting("type", "pfxfull")
                }
            };

            var provider = DeploymentTaskProviderFactory.Create(Providers.DeploymentTasks.CertificateExport.Definition.Id.ToLower());
            var t = new DeploymentTask(provider, config, null);

            deploymentTasks.Add(t);

            // perform preview deployments
            var managedCert = GetMockManagedCertificate("DeploymentTest", "123", PrimaryTestDomain, PrimaryIISRoot);

            foreach (var task in deploymentTasks)
            {
                var result = await task.Execute(_log, managedCert, isPreviewOnly: false);
            }

            // assert new valid pfx exists in destination
            Assert.IsTrue(File.Exists(outputFile));
            File.Delete(outputFile);
        }

        [TestMethod, TestCategory("Export")]
        public async Task TestPemApacheExport()
        {

            var deploymentTasks = new List<DeploymentTask>();

            var outputPath = ConfigSettings["TestLocalPath"] + "\\test_pfx_export";

            var config = new DeploymentTaskConfig
            {
                TaskTypeId = Providers.DeploymentTasks.Apache.Definition.Id.ToLower(),
                IsDeferred = false,
                TaskName = "A test Apache export task",
                ChallengeProvider = "Certify.StandardChallenges.Local",

                Parameters = new List<Models.Config.ProviderParameterSetting>
                {
                    new Models.Config.ProviderParameterSetting("path_cert", outputPath+".crt"),
                    new Models.Config.ProviderParameterSetting("path_key", outputPath+".key"),
                    new Models.Config.ProviderParameterSetting("path_chain", outputPath+".chain")
                }
            };

            var provider = DeploymentTaskProviderFactory.Create(Providers.DeploymentTasks.Apache.Definition.Id.ToLower());
            var t = new DeploymentTask(provider, config, null);

            deploymentTasks.Add(t);

            // perform preview deployments
            var managedCert = GetMockManagedCertificate("LocalApacheDeploymentTest", "123", PrimaryTestDomain, PrimaryIISRoot);

            foreach (var task in deploymentTasks)
            {
                var result = await task.Execute(_log, managedCert, isPreviewOnly: false);
            }

            // assert output exists in destination
            Assert.IsTrue(File.Exists(outputPath + ".crt"));
            Assert.IsTrue(File.Exists(outputPath + ".key"));
            Assert.IsTrue(File.Exists(outputPath + ".chain"));

            File.Delete(outputPath + ".crt");
            File.Delete(outputPath + ".key");
            File.Delete(outputPath + ".chain");
        }

        [TestMethod, TestCategory("Export")]
        public async Task TestPemNginxExport()
        {

            var deploymentTasks = new List<DeploymentTask>();

            var outputPath = ConfigSettings["TestLocalPath"] + "\\test_pfx_export_nginx";

            var config = new DeploymentTaskConfig
            {
                TaskTypeId = Providers.DeploymentTasks.Nginx.Definition.Id.ToLower(),
                IsDeferred = false,
                TaskName = "A test Nginx export task",
                ChallengeProvider = "Certify.StandardChallenges.Local",

                Parameters = new List<Models.Config.ProviderParameterSetting>
                {
                    new Models.Config.ProviderParameterSetting("path_cert", outputPath+".crt"),
                    new Models.Config.ProviderParameterSetting("path_key", outputPath+".key"),
                    new Models.Config.ProviderParameterSetting("path_chain", outputPath+".chain")
                }
            };

            var provider = DeploymentTaskProviderFactory.Create(Providers.DeploymentTasks.Nginx.Definition.Id.ToLower());
            var t = new DeploymentTask(provider, config, null);

            deploymentTasks.Add(t);

            // perform preview deployments
            var managedCert = GetMockManagedCertificate("LocalNginxDeploymentTest", "123", PrimaryTestDomain, PrimaryIISRoot);

            foreach (var task in deploymentTasks)
            {
                var result = await task.Execute(_log, managedCert, isPreviewOnly: false);
            }

            // assert output exists in destination
            Assert.IsTrue(File.Exists(outputPath + ".crt"));
            Assert.IsTrue(File.Exists(outputPath + ".key"));

            File.Delete(outputPath + ".crt");
            File.Delete(outputPath + ".key");
           
        }
    }
}
