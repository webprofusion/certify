using System.IO;
using System.Threading.Tasks;
using Medallion.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Certify.Server.Core.Tests.Unit
{

    [TestClass]
    public class CertifyCLITests
    {
        private string _serviceExePath = ".\\Certify.Server.Core.exe";
        private string _cliExePath = ".\\Certify.exe";

        private async Task<Command> StartCertifyService()
        {
            var certifyService = Command.Run(_serviceExePath);
           
            await Task.Delay(3000); // Wait for service to start
            return certifyService;
        }

        private async Task<CommandResult> StopCertifyService(Command certifyService)
        {
            await certifyService.TrySignalAsync(CommandSignal.ControlC);
            var cmdResult = await certifyService.Task;
            return cmdResult;
        }

        [TestMethod, Description("Test CLI version command")]
        public async Task TestCLIVersionCommand()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var dir = Directory.GetCurrentDirectory();

                var cliCommand = Command.Run(_cliExePath, arguments: ["version"], options: o => o.StartInfo(s => s.Verb = "runas"));

                var result = await cliCommand.Task;

                Assert.AreEqual(0, result.ExitCode, "CLI version command should succeed");
                Assert.Contains("Certify Certificate Manager", result.StandardOutput, "Version output should contain product name");
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Test CLI credential list command")]
        public async Task TestCLICredentialListCommand()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var cliCommand = Command.Run(_cliExePath, "credential", "list");
                var result = await cliCommand.Task;

                Assert.AreEqual(0, result.ExitCode, "CLI credential list command should succeed");

                // Try to parse output as JSON
                var output = result.StandardOutput.Trim();
                if (!string.IsNullOrEmpty(output))
                {
                    try
                    {
                        var credentials = JsonConvert.DeserializeObject(output);
                        Assert.IsNotNull(credentials, "Credential list should be valid JSON");
                    }
                    catch (JsonException ex)
                    {
                        Assert.Fail($"Credential list output should be valid JSON: {ex.Message}\nOutput: {output}");
                    }
                }
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Test CLI acmeaccount list command")]
        public async Task TestCLIAcmeAccountListCommand()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var cliCommand = Command.Run(_cliExePath, "acmeaccount", "list");
                var result = await cliCommand.Task;

                Assert.AreEqual(0, result.ExitCode, "CLI acmeaccount list command should succeed");

                // Try to parse output as JSON
                var output = result.StandardOutput.Trim();
                if (!string.IsNullOrEmpty(output))
                {
                    try
                    {
                        var accounts = JsonConvert.DeserializeObject(output);
                        Assert.IsNotNull(accounts, "Account list should be valid JSON");
                    }
                    catch (JsonException ex)
                    {
                        Assert.Fail($"Account list output should be valid JSON: {ex.Message}\nOutput: {output}");
                    }
                }
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Test CLI list managed certificates command")]
        public async Task TestCLIListCommand()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var cliCommand = Command.Run(_cliExePath, "list");
                var result = await cliCommand.Task;

                Assert.AreEqual(0, result.ExitCode, "CLI list command should succeed");

                // Output might be table format, just check it's not empty
                Assert.IsGreaterThan(0, result.StandardOutput.Length, "List command should produce output");
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Test CLI help command")]
        public async Task TestCLIHelpCommand()
        {
            // Help doesn't require service to be running
            var cliCommand = Command.Run(_cliExePath);
            var result = await cliCommand.Task;

            Assert.AreEqual(0, result.ExitCode, "CLI help command should succeed");
            Assert.Contains("Usage: certify", result.StandardOutput, "Help output should contain usage information");
        }

        [TestMethod, Description("Test CLI service not available error")]
        public async Task TestCLIServiceNotAvailable()
        {
            // Don't start service, test error handling
            var cliCommand = Command.Run(_cliExePath, "version");
            var result = await cliCommand.Task;

            Assert.AreEqual(-1, result.ExitCode, "CLI should return error code when service unavailable");
            Assert.Contains("service not started", result.StandardOutput, "Error message should indicate service not started");
        }
    }
}
