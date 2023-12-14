using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Reporting;
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
    [TestClass]
    public class CertifyServiceApiTests
    {
#if NET462
        private static HttpClient _httpClient;
        private static Command certifyService;

        [ClassInitialize]
        public static async Task ClassInit(TestContext context)
        {
            var serviceConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();
            var serviceUri = $"{(serviceConfig.UseHTTPS ? "https" : "http")}://{serviceConfig.Host}:{serviceConfig.Port}";
            
            var httpHandler = new HttpClientHandler { UseDefaultCredentials = true };
            _httpClient = new HttpClient(httpHandler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Certify/App");
            _httpClient.BaseAddress = new Uri(serviceUri + "/api/");

            certifyService = Command.Run(".\\Certify.Service.exe");
            await Task.Delay(2000);
        }

        [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
        public static async Task ClassCleanup()
        {
            await certifyService.TrySignalAsync(CommandSignal.ControlC);

            var cmdResult = await certifyService.Task;

            Assert.AreEqual(cmdResult.ExitCode, 0, "Unexpected exit code");
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/system/appversion")]
        public async Task TestCertifyServiceAppVersionRoute()
        {
            var versionRawRes = await _httpClient.GetAsync("system/appversion");
            var versionResStr = await versionRawRes.Content.ReadAsStringAsync();
            var versionRes = JsonConvert.DeserializeObject<string>(versionResStr);

            Assert.AreEqual(HttpStatusCode.OK, versionRawRes.StatusCode, $"Unexpected status code from GET {versionRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            StringAssert.Matches(versionRes, new Regex(@"^(\d+\.)?(\d+\.)?(\d+\.)?(\*|\d+)$"), $"Unexpected response from GET {versionRawRes.RequestMessage.RequestUri.AbsoluteUri} : {versionResStr}");                
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid respose on route GET /api/system/updatecheck")]
        public async Task TestCertifyServiceUpdateCheckRoute()
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/system/diagnostics")]
        public async Task TestCertifyServiceDiagnosticsRoute()
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/system/datastores/providers")]
        public async Task TestCertifyServiceDatastoreProvidersRoute()
        {
            var datastoreProvidersRawRes = await _httpClient.GetAsync("system/datastores/providers");
            var datastoreProvidersRawResStr = await datastoreProvidersRawRes.Content.ReadAsStringAsync();
            var datastoreProvidersRes = JsonConvert.DeserializeObject<List<ProviderDefinition>>(datastoreProvidersRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, datastoreProvidersRawRes.StatusCode, $"Unexpected status code from GET {datastoreProvidersRawRes.RequestMessage.RequestUri.AbsoluteUri}");
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/system/datastores/")]
        public async Task TestCertifyServiceDatastoresRoute()
        {
            var datastoreRawRes = await _httpClient.GetAsync("system/datastores/");
            var datastoreRawResStr = await datastoreRawRes.Content.ReadAsStringAsync();
            var datastoreRes = JsonConvert.DeserializeObject<List<DataStoreConnection>>(datastoreRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, datastoreRawRes.StatusCode, $"Unexpected status code from GET {datastoreRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            Assert.IsTrue(datastoreRes.Count >= 1);
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/test")]
        public async Task TestCertifyServiceDatastoresTestRoute()
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/update")]
        public async Task TestCertifyServiceDatastoresUpdateRoute()
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/setdefault/{dataStoreId}")]
        public async Task TestCertifyServiceDatastoresSetDefaultRoute()
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/delete")]
        [Ignore]
        public async Task TestCertifyServiceDatastoresDeleteRoute()
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/system/datastores/copy/{sourceId}/{destId}")]
        [Ignore]
        public async Task TestCertifyServiceDatastoresCopyRoute()
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

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/server/isavailable/{serverType}")]
        public async Task TestCertifyServiceServerIsavailableRoute()
        {
            var isAvailableRawRes = await _httpClient.GetAsync($"server/isavailable/{StandardServerTypes.IIS}");
            var isAvailableRawResStr = await isAvailableRawRes.Content.ReadAsStringAsync();
            var isAvailableRes = JsonConvert.DeserializeObject<bool>(isAvailableRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, isAvailableRawRes.StatusCode, $"Unexpected status code from GET {isAvailableRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            Assert.IsTrue(isAvailableRes);
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/server/sitelist/{serverType}")]
        public async Task TestCertifyServiceServerSitelistRoute()
        {
            var sitelistRawRes = await _httpClient.GetAsync($"server/sitelist/{StandardServerTypes.IIS}");
            var sitelistRawResStr = await sitelistRawRes.Content.ReadAsStringAsync();
            var sitelistRes = JsonConvert.DeserializeObject<List<SiteInfo>>(sitelistRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, sitelistRawRes.StatusCode, $"Unexpected status code from GET {sitelistRawRes.RequestMessage.RequestUri.AbsoluteUri}");
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/server/version/{serverType}")]
        public async Task TestCertifyServiceServerVersionRoute()
        {
            var versionRawRes = await _httpClient.GetAsync($"server/version/{StandardServerTypes.IIS}");
            var versionRawResStr = await versionRawRes.Content.ReadAsStringAsync();
            var versionRes = JsonConvert.DeserializeObject<string>(versionRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, versionRawRes.StatusCode, $"Unexpected status code from GET {versionRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            StringAssert.Matches(versionRes, new Regex(@"^(\d+\.)?(\*|\d+)$"), $"Unexpected response from GET {versionRawRes.RequestMessage.RequestUri.AbsoluteUri} : {versionRawResStr}");
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/preferences")]
        public async Task TestCertifyServiceGetPreferencesRoute()
        {
            var preferencesRawRes = await _httpClient.GetAsync($"preferences");
            var preferencesRawResStr = await preferencesRawRes.Content.ReadAsStringAsync();
            var preferencesRes = JsonConvert.DeserializeObject<Preferences>(preferencesRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, preferencesRawRes.StatusCode, $"Unexpected status code from GET {preferencesRawRes.RequestMessage.RequestUri.AbsoluteUri}");
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/preferences")]
        public async Task TestCertifyServiceSetPreferencesRoute()
        {
            var preferencesRawRes = await _httpClient.GetAsync($"preferences");
            var preferencesRawResStr = await preferencesRawRes.Content.ReadAsStringAsync();
            var preferencesRes = JsonConvert.DeserializeObject<Preferences>(preferencesRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, preferencesRawRes.StatusCode, $"Unexpected status code from GET {preferencesRawRes.RequestMessage.RequestUri.AbsoluteUri}");

            preferencesRes.EnableAppTelematics = false;

            var setPreferencesRawRes = await _httpClient.PostAsJsonAsync<Preferences>($"preferences", preferencesRes);
            var setPreferencesRawResStr = await setPreferencesRawRes.Content.ReadAsStringAsync();
            var setPreferencesRes = JsonConvert.DeserializeObject<bool>(setPreferencesRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, setPreferencesRawRes.StatusCode, $"Unexpected status code from POST {setPreferencesRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            Assert.IsTrue(setPreferencesRes);

            preferencesRawRes = await _httpClient.GetAsync($"preferences");
            preferencesRawResStr = await preferencesRawRes.Content.ReadAsStringAsync();
            var newPreferencesRes = JsonConvert.DeserializeObject<Preferences>(preferencesRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, preferencesRawRes.StatusCode, $"Unexpected status code from GET {preferencesRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            Assert.IsFalse(newPreferencesRes.EnableAppTelematics);

            preferencesRes.EnableAppTelematics = true;

            setPreferencesRawRes = await _httpClient.PostAsJsonAsync<Preferences>($"preferences", preferencesRes);
            setPreferencesRawResStr = await setPreferencesRawRes.Content.ReadAsStringAsync();
            setPreferencesRes = JsonConvert.DeserializeObject<bool>(setPreferencesRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, setPreferencesRawRes.StatusCode, $"Unexpected status code from POST {setPreferencesRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            Assert.IsTrue(setPreferencesRes);

            preferencesRawRes = await _httpClient.GetAsync($"preferences");
            preferencesRawResStr = await preferencesRawRes.Content.ReadAsStringAsync();
            var resetPreferencesRes = JsonConvert.DeserializeObject<Preferences>(preferencesRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, preferencesRawRes.StatusCode, $"Unexpected status code from GET {preferencesRawRes.RequestMessage.RequestUri.AbsoluteUri}");
            Assert.IsTrue(resetPreferencesRes.EnableAppTelematics);
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/managedcertificates/search")]
        public async Task TestCertifyServiceManagedCertificatesSearchRoute()
        {
            var searchRawRes = await _httpClient.PostAsJsonAsync<ManagedCertificateFilter>($"managedcertificates/search", new ManagedCertificateFilter { Name = "" });
            var searchRawResStr = await searchRawRes.Content.ReadAsStringAsync();
            var searchRes = JsonConvert.DeserializeObject<List<ManagedCertificate>>(searchRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, searchRawRes.StatusCode, $"Unexpected status code from POST {searchRawRes.RequestMessage.RequestUri.AbsoluteUri}");
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route POST /api/managedcertificates/results")]
        public async Task TestCertifyServiceManagedCertificatesResultsRoute()
        {
            var resultsRawRes = await _httpClient.PostAsJsonAsync<ManagedCertificateFilter>($"managedcertificates/results", new ManagedCertificateFilter { Name = "" });
            var resultsRawResStr = await resultsRawRes.Content.ReadAsStringAsync();
            var resultsRes = JsonConvert.DeserializeObject<ManagedCertificateSearchResult>(resultsRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, resultsRawRes.StatusCode, $"Unexpected status code from POST {resultsRawRes.RequestMessage.RequestUri.AbsoluteUri}");
        }

        [TestMethod, Description("Validate that Certify.Service.exe returns a valid response on route GET /api/managedcertificates/summary")]
        public async Task TestCertifyServiceManagedCertificatesSummaryRoute()
        {
            var summaryRawRes = await _httpClient.GetAsync($"managedcertificates/summary");
            var summaryRawResStr = await summaryRawRes.Content.ReadAsStringAsync();
            var summaryRes = JsonConvert.DeserializeObject<Summary>(summaryRawResStr);

            Assert.AreEqual(HttpStatusCode.OK, summaryRawRes.StatusCode, $"Unexpected status code from GET {summaryRawRes.RequestMessage.RequestUri.AbsoluteUri}");
        }

#endif
    }
}
