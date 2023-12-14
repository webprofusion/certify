using Medallion.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CertifyServiceCliTests
    {
#if NET462
        [TestMethod, Description("Validate that Certify.Service.exe does not start with args from CLI")]
        public async Task TestProgramMainFailsWithArgsCli()
        {
            var certifyService = Command.Run(".\\Certify.Service.exe", "args");

            var cmdResult = await certifyService.Task;

            Assert.IsTrue(cmdResult.StandardOutput.Contains("Topshelf.HostFactory Error: 0 : An exception occurred creating the host, Topshelf.HostConfigurationException: The service was not properly configured:"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("Topshelf.HostFactory Error: 0 : The service terminated abnormally, Topshelf.HostConfigurationException: The service was not properly configured:"));

            Assert.AreEqual(cmdResult.ExitCode, 1067, "Unexpected exit code");
        }

        [TestMethod, Description("Validate that Certify.Service.exe starts from CLI with no args")]
        public async Task TestProgramMainStartsCli()
        {
            var certifyService = Command.Run(".\\Certify.Service.exe");
            await Task.Delay(2000);

            await certifyService.TrySignalAsync(CommandSignal.ControlC);

            var cmdResult = await certifyService.Task;

            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] Name Certify.Service"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] DisplayName Certify Certificate Manager Service (Instance: Debug)"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] Description Certify Certificate Manager Service"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] InstanceName Debug"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] ServiceName Certify.Service$Debug"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("The Certify.Service$Debug service is now running, press Control+C to exit."));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("Control+C detected, attempting to stop service."));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("The Certify.Service$Debug service has stopped."));

            Assert.AreEqual(cmdResult.ExitCode, 0, "Unexpected exit code");
        }
#endif
    }
}
