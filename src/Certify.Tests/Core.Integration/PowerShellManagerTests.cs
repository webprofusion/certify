using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class PowerShellManagerTests
    {
        private const string TestOutputPath = @"C:\Temp\Certify\TestOutput\TestPSOutput.txt";

        [TestMethod, Description("Test Script runs OK")]
        public async Task TestLoadManagedCertificates()
        {
            var path = AppContext.BaseDirectory;

            await PowerShellManager.RunScript("Unrestricted", new CertificateRequestResult(new ManagedCertificate()), System.IO.Path.Combine(path, "Assets\\Powershell\\Simple.ps1"));

            var outputExists = File.Exists(TestOutputPath);
            Assert.IsTrue(outputExists, "Powershell output file should exist");

            CleanupOutput();
        }

        [TestMethod, Description("Test Script runs OK in a new process with secure payload parameters")]
        public async Task TestLoadManagedCertificatesLaunchNewProcessSecurePayload()
        {
            var path = AppContext.BaseDirectory;
            var scriptPath = Path.Combine(path, "Assets\\Powershell\\Simple.ps1");

            var result = await PowerShellManager.RunScript(
                "Unrestricted",
                new CertificateRequestResult(new ManagedCertificate()),
                scriptPath,
                new Dictionary<string, object>
                {
                    ["message"] = "secret-from-payload",
                    ["flag"] = "true"
                },
                launchNewProcess: true);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsTrue(File.Exists(TestOutputPath), "Powershell output file should exist");

            var output = File.ReadAllText(TestOutputPath);
            StringAssert.Contains(output, "Message: secret-from-payload");
            StringAssert.Contains(output, "Flag: True");

            CleanupOutput();
        }

        [TestMethod, Description("Incomplete credentials fail fast")]
        public async Task TestIncompleteCredentialsFailFast()
        {
            var path = AppContext.BaseDirectory;
            var scriptPath = Path.Combine(path, "Assets\\Powershell\\Simple.ps1");

            var result = await PowerShellManager.RunScript(
                "Unrestricted",
                scriptFile: scriptPath,
                credentials: new Dictionary<string, string>
                {
                    ["username"] = "test-user"
                });

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "requires username and password");
        }

        private static void CleanupOutput()
        {
            try
            {
                File.Delete(TestOutputPath);
            }
            catch { }
        }
    }
}
