using System;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class PowerShellManagerTests
    {

        [TestMethod, Description("Test Script runs OK")]
        public async Task TestLoadManagedCertificates()
        {
            var path = AppContext.BaseDirectory;

            await PowerShellManager.RunScript("Unrestricted", new CertificateRequestResult(new ManagedCertificate()), System.IO.Path.Combine(path, "Assets\\Powershell\\Simple.ps1"));

            var outputExists = System.IO.File.Exists(@"C:\Temp\Certify\TestOutput\TestPSOutput.txt");
            Assert.IsTrue(outputExists, "Powershell output file should exist");

            try
            {
                System.IO.File.Delete(@"C:\Temp\Certify\TestOutput\TestPSOutput.txt");
            }
            catch { }
        }
    }
}
