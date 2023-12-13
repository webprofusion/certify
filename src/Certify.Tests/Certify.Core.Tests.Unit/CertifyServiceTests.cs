using Certify.Models;
using Certify.Models.Config;
using Certify.Shared;
using Medallion.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Certify.Core.Tests.Unit
{
#if NET462
    [TestClass]
    public class CertifyServiceTests
    {
        private HttpClient _httpClient;
        private string serviceUri;

        public CertifyServiceTests() {
            var serviceConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();
            serviceUri = $"{(serviceConfig.UseHTTPS ? "https" : "http")}://{serviceConfig.Host}:{serviceConfig.Port}";
            var httpHandler = new HttpClientHandler { UseDefaultCredentials = true };
            _httpClient = new HttpClient(httpHandler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Certify/App");
            _httpClient.BaseAddress = new Uri(serviceUri+"/api/");
        }

        private async Task <Command> StartCertifyService(string args = "")
        {
            Command certifyService;
            if (args == "")
            {
                certifyService = Command.Run(".\\Certify.Service.exe");
                await Task.Delay(2000);
            }
            else
            {
                certifyService = Command.Run(".\\Certify.Service.exe", args);
            }

            return certifyService;
        }

        private async Task<CommandResult> StopCertifyService(Command certifyService)
        {
            await certifyService.TrySignalAsync(CommandSignal.ControlC);

            var cmdResult = await certifyService.Task;

            Assert.AreEqual(cmdResult.ExitCode, 0, "Unexpected exit code");

            return cmdResult;
        }

        [TestMethod, Description("Validate that Certify.Service.exe does not start with args from CLI")]
        public async Task TestProgramMainFailsWithArgsCli()
        {
            var certifyService = await StartCertifyService("args");

            var cmdResult = await certifyService.Task;

            Assert.IsTrue(cmdResult.StandardOutput.Contains("Topshelf.HostFactory Error: 0 : An exception occurred creating the host, Topshelf.HostConfigurationException: The service was not properly configured:"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("Topshelf.HostFactory Error: 0 : The service terminated abnormally, Topshelf.HostConfigurationException: The service was not properly configured:"));

            Assert.AreEqual(cmdResult.ExitCode, 1067, "Unexpected exit code");
        }

        [TestMethod, Description("Validate that Certify.Service.exe starts from CLI with no args")]
        public async Task TestProgramMainStartsCli()
        {
            var certifyService = await StartCertifyService();

            var cmdResult = await StopCertifyService(certifyService);

            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] Name Certify.Service"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] DisplayName Certify Certificate Manager Service (Instance: Debug)"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] Description Certify Certificate Manager Service"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] InstanceName Debug"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("[Success] ServiceName Certify.Service$Debug"));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("The Certify.Service$Debug service is now running, press Control+C to exit."));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("Control+C detected, attempting to stop service."));
            Assert.IsTrue(cmdResult.StandardOutput.Contains("The Certify.Service$Debug service has stopped."));
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route /api/system/appversion")]
        public async Task TestCertifyServiceAppVersionRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var versionRes = await _httpClient.GetAsync("system/appversion");
                var versionResStr = await versionRes.Content.ReadAsStringAsync();
                
                Assert.AreEqual(HttpStatusCode.OK, versionRes.StatusCode, $"Unexpected status code from GET {versionRes.RequestMessage.RequestUri.AbsoluteUri}");
                StringAssert.Matches(versionResStr, new Regex(@"^""(\d+\.)?(\d+\.)?(\d+\.)?(\*|\d+)""$"), $"Unexpected response from GET {versionRes.RequestMessage.RequestUri.AbsoluteUri} : {versionResStr}");                
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid respose on route /api/system/updatecheck")]
        public async Task TestCertifyServiceUpdateCheckRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var updatesRawRes = await _httpClient.GetAsync("system/updatecheck");
                var updateRawResStr = await updatesRawRes.Content.ReadAsStringAsync();
                var updateRes = JsonConvert.DeserializeObject<UpdateCheck>(updateRawResStr);
                
                Assert.AreEqual(HttpStatusCode.OK, updatesRawRes.StatusCode, $"Unexpected status code from GET {updatesRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsFalse(updateRes.MustUpdate);
                Assert.IsFalse(updateRes.IsNewerVersion);
                Assert.AreEqual(updateRes.InstalledVersion.ToString(), updateRes.Version.ToString());
                Assert.AreEqual("", updateRes.UpdateFilePath);
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route /api/system/diagnostics")]
        public async Task TestCertifyServiceDiagnosticsRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var diagnosticsRawRes = await _httpClient.GetAsync("system/diagnostics");
                var diagnosticsRawResStr = await diagnosticsRawRes.Content.ReadAsStringAsync();
                var diagnosticsRes = JsonConvert.DeserializeObject<List<ActionResult>>(diagnosticsRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, diagnosticsRawRes.StatusCode, $"Unexpected status code from GET {diagnosticsRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.AreEqual(4, diagnosticsRes.Count);

                Assert.AreEqual("Created test temp file OK.", diagnosticsRes[0].Message);
                Assert.IsTrue(diagnosticsRes[0].IsSuccess);
                Assert.IsFalse(diagnosticsRes[0].IsWarning);
                Assert.AreEqual(null, diagnosticsRes[0].Result);

                Assert.AreEqual($"Drive {Environment.GetEnvironmentVariable("SystemDrive")} has more than 512MB of disk space free.", diagnosticsRes[1].Message);
                Assert.IsTrue(diagnosticsRes[1].IsSuccess);
                Assert.IsFalse(diagnosticsRes[1].IsWarning);
                Assert.AreEqual(null, diagnosticsRes[1].Result);

                Assert.AreEqual("System time is correct.", diagnosticsRes[2].Message);
                Assert.IsTrue(diagnosticsRes[2].IsSuccess);
                Assert.IsFalse(diagnosticsRes[2].IsWarning);
                Assert.AreEqual(null, diagnosticsRes[2].Result);

                Assert.AreEqual("PowerShell 5.0 or higher is available.", diagnosticsRes[3].Message);
                Assert.IsTrue(diagnosticsRes[3].IsSuccess);
                Assert.IsFalse(diagnosticsRes[3].IsWarning);
                Assert.AreEqual(null, diagnosticsRes[3].Result);
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route /api/system/datastores/providers")]
        public async Task TestCertifyServiceDatastoreProvidersRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var datastoreProvidersRawRes = await _httpClient.GetAsync("system/datastores/providers");
                var datastoreProvidersRawResStr = await datastoreProvidersRawRes.Content.ReadAsStringAsync();
                var datastoreProvidersRes = JsonConvert.DeserializeObject<List<ProviderDefinition>>(datastoreProvidersRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreProvidersRawRes.StatusCode, $"Unexpected status code from GET {datastoreProvidersRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route /api/system/datastores/")]
        public async Task TestCertifyServiceDatastoresRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var datastoreRawRes = await _httpClient.GetAsync("system/datastores/");
                var datastoreRawResStr = await datastoreRawRes.Content.ReadAsStringAsync();
                var datastoreRes = JsonConvert.DeserializeObject<List<DataStoreConnection>>(datastoreRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreRawRes.StatusCode, $"Unexpected status code from GET {datastoreRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreRes.Count >= 1);
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }
    }
#endif
}
