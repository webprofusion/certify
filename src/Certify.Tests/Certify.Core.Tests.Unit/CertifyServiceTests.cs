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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/system/appversion")]
        public async Task TestCertifyServiceAppVersionRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var versionRawRes = await _httpClient.GetAsync("system/appversion");
                var versionResStr = await versionRawRes.Content.ReadAsStringAsync();
                var versionRes = JsonConvert.DeserializeObject<string>(versionResStr);

                Assert.AreEqual(HttpStatusCode.OK, versionRawRes.StatusCode, $"Unexpected status code from GET {versionRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                StringAssert.Matches(versionRes, new Regex(@"^(\d+\.)?(\d+\.)?(\d+\.)?(\*|\d+)$"), $"Unexpected response from GET {versionRawRes.RequestMessage.RequestUri.AbsoluteUri} : {versionResStr}");                
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid respose on route GET /api/system/updatecheck")]
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/system/diagnostics")]
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/system/datastores/providers")]
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/system/datastores/")]
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/test")]
        public async Task TestCertifyServiceDatastoresTestRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var datastoreRawRes = await _httpClient.GetAsync("system/datastores/");
                var datastoreRawResStr = await datastoreRawRes.Content.ReadAsStringAsync();
                var datastoreRes = JsonConvert.DeserializeObject<List<DataStoreConnection>>(datastoreRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreRawRes.StatusCode, $"Unexpected status code from GET {datastoreRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreRes.Count >= 1);

                var datastoreTestRawRes = await _httpClient.PostAsJsonAsync("system/datastores/test", datastoreRes[0]);
                var datastoreTestRawResStr = await datastoreTestRawRes.Content.ReadAsStringAsync();
                var datastoreTestRes = JsonConvert.DeserializeObject<List<ActionStep>>(datastoreTestRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreTestRawRes.StatusCode, $"Unexpected status code from POST {datastoreTestRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreTestRes.Count >= 1);
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/update")]
        public async Task TestCertifyServiceDatastoresUpdateRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var datastoreRawRes = await _httpClient.GetAsync("system/datastores/");
                var datastoreRawResStr = await datastoreRawRes.Content.ReadAsStringAsync();
                var datastoreRes = JsonConvert.DeserializeObject<List<DataStoreConnection>>(datastoreRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreRawRes.StatusCode, $"Unexpected status code from GET {datastoreRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreRes.Count >= 1);

                var datastoreUpdateRawRes = await _httpClient.PostAsJsonAsync("system/datastores/update", datastoreRes[0]);
                var datastoreUpdateRawResStr = await datastoreUpdateRawRes.Content.ReadAsStringAsync();
                var datastoreUpdateRes = JsonConvert.DeserializeObject<List<ActionStep>>(datastoreUpdateRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreUpdateRawRes.StatusCode, $"Unexpected status code from POST {datastoreUpdateRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreUpdateRes.Count >= 1);
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/setdefault/{dataStoreId}")]
        public async Task TestCertifyServiceDatastoresSetDefaultRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var datastoreRawRes = await _httpClient.GetAsync("system/datastores/");
                var datastoreRawResStr = await datastoreRawRes.Content.ReadAsStringAsync();
                var datastoreRes = JsonConvert.DeserializeObject<List<DataStoreConnection>>(datastoreRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreRawRes.StatusCode, $"Unexpected status code from GET {datastoreRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreRes.Count >= 1);

                var datastoreSetDefaultRawRes = await _httpClient.PostAsync($"system/datastores/setdefault/{datastoreRes[0].Id}", new StringContent(""));
                var datastoreSetDefaultRawResStr = await datastoreSetDefaultRawRes.Content.ReadAsStringAsync();
                var datastoreSetDefaultRes = JsonConvert.DeserializeObject<List<ActionStep>>(datastoreSetDefaultRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreSetDefaultRawRes.StatusCode, $"Unexpected status code from POST {datastoreSetDefaultRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreSetDefaultRes.Count >= 1);
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/delete")]
        [Ignore]
        public async Task TestCertifyServiceDatastoresDeleteRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var datastoreRawRes = await _httpClient.GetAsync("system/datastores/");
                var datastoreRawResStr = await datastoreRawRes.Content.ReadAsStringAsync();
                var datastoreRes = JsonConvert.DeserializeObject<List<DataStoreConnection>>(datastoreRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreRawRes.StatusCode, $"Unexpected status code from GET {datastoreRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreRes.Count >= 1);

                var datastoreDeleteRawRes = await _httpClient.PostAsync("system/datastores/delete", new StringContent(datastoreRes[0].Id));
                var datastoreDeleteRawResStr = await datastoreDeleteRawRes.Content.ReadAsStringAsync();
                var datastoreDeleteRes = JsonConvert.DeserializeObject<List<ActionStep>>(datastoreDeleteRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreDeleteRawRes.StatusCode, $"Unexpected status code from POST {datastoreDeleteRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreDeleteRes.Count >= 1);

                var datastoreUpdateRawRes = await _httpClient.PostAsJsonAsync("system/datastores/update", datastoreRes[0]);
                var datastoreUpdateRawResStr = await datastoreUpdateRawRes.Content.ReadAsStringAsync();
                var datastoreUpdateRes = JsonConvert.DeserializeObject<List<ActionStep>>(datastoreUpdateRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreUpdateRawRes.StatusCode, $"Unexpected status code from POST {datastoreUpdateRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreUpdateRes.Count >= 1);
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/copy/{sourceId}/{destId}")]
        [Ignore]
        public async Task TestCertifyServiceDatastoresCopyRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var datastoreRawRes = await _httpClient.GetAsync("system/datastores/");
                var datastoreRawResStr = await datastoreRawRes.Content.ReadAsStringAsync();
                var datastoreRes = JsonConvert.DeserializeObject<List<DataStoreConnection>>(datastoreRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreRawRes.StatusCode, $"Unexpected status code from GET {datastoreRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreRes.Count >= 1);

                var newDataStoreId = "default-copy";
                var datastoreCopyRawRes = await _httpClient.PostAsync($"system/datastores/copy/{datastoreRes[0].Id}/{newDataStoreId}", new StringContent(""));
                var datastoreCopyRawResStr = await datastoreCopyRawRes.Content.ReadAsStringAsync();
                var datastoreCopyRes = JsonConvert.DeserializeObject<List<ActionStep>>(datastoreCopyRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreCopyRawRes.StatusCode, $"Unexpected status code from POST {datastoreCopyRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreCopyRes.Count >= 1);

                datastoreRawRes = await _httpClient.GetAsync("system/datastores/");
                datastoreRawResStr = await datastoreRawRes.Content.ReadAsStringAsync();
                datastoreRes = JsonConvert.DeserializeObject<List<DataStoreConnection>>(datastoreRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreRawRes.StatusCode, $"Unexpected status code from GET {datastoreRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreRes.Count >= 2);

                var datastoreDeleteRawRes = await _httpClient.PostAsJsonAsync<string>("system/datastores/delete", newDataStoreId);
                var datastoreDeleteRawResStr = await datastoreDeleteRawRes.Content.ReadAsStringAsync();
                var datastoreDeleteRes = JsonConvert.DeserializeObject<List<ActionStep>>(datastoreDeleteRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, datastoreDeleteRawRes.StatusCode, $"Unexpected status code from POST {datastoreDeleteRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(datastoreDeleteRes.Count >= 1);
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/server/isavailable/{serverType}")]
        public async Task TestCertifyServiceServerIsavailableRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var isAvailableRawRes = await _httpClient.GetAsync($"server/isavailable/{StandardServerTypes.IIS}");
                var isAvailableRawResStr = await isAvailableRawRes.Content.ReadAsStringAsync();
                var isAvailableRes = JsonConvert.DeserializeObject<bool>(isAvailableRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, isAvailableRawRes.StatusCode, $"Unexpected status code from GET {isAvailableRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                Assert.IsTrue(isAvailableRes);
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/server/sitelist/{serverType}")]
        public async Task TestCertifyServiceServerSitelistRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var sitelistRawRes = await _httpClient.GetAsync($"server/sitelist/{StandardServerTypes.IIS}");
                var sitelistRawResStr = await sitelistRawRes.Content.ReadAsStringAsync();
                var sitelistRes = JsonConvert.DeserializeObject<List<SiteInfo>>(sitelistRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, sitelistRawRes.StatusCode, $"Unexpected status code from GET {sitelistRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/server/version/{serverType}")]
        public async Task TestCertifyServiceServerVersionRoute()
        {
            var certifyService = await StartCertifyService();

            try
            {
                var versionRawRes = await _httpClient.GetAsync($"server/version/{StandardServerTypes.IIS}");
                var versionRawResStr = await versionRawRes.Content.ReadAsStringAsync();
                var versionRes = JsonConvert.DeserializeObject<string>(versionRawResStr);

                Assert.AreEqual(HttpStatusCode.OK, versionRawRes.StatusCode, $"Unexpected status code from GET {versionRawRes.RequestMessage.RequestUri.AbsoluteUri}");
                StringAssert.Matches(versionRes, new Regex(@"^(\d+\.)?(\*|\d+)$"), $"Unexpected response from GET {versionRawRes.RequestMessage.RequestUri.AbsoluteUri} : {versionRawResStr}");
            }
            finally
            {
                await StopCertifyService(certifyService);
            }
        }
    }
#endif
}
