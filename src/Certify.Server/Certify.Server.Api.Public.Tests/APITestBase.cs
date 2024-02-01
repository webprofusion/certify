using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models.API;
using Certify.Server.Api.Public.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.CoreUtilities.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Api.Tests
{
    [TestClass]
    public class APITestBase
    {
        internal static Certify.API.Public.Client _clientWithAnonymousAccess;
        internal static HttpClient _httpClientWithAnonymousAccess;

        internal static Certify.API.Public.Client _clientWithAuthorizedAccess;
        internal static HttpClient _httpClientWithAuthorizedAccess;

        internal static TestServer _apiServer;

        internal static System.Text.Json.JsonSerializerOptions _defaultJsonSerializerOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        internal static string _apiBaseUri = "/api/v1";

        internal string _refreshToken;

        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            // setup public API service and backend service

            // tell backend service to uses specific host/ports if not already set
            if (Environment.GetEnvironmentVariable("CERTIFY_SERVICE_HOST") == null)
            {
                Environment.SetEnvironmentVariable("CERTIFY_SERVICE_HOST", "127.0.0.1");
            }

            if (Environment.GetEnvironmentVariable("CERTIFY_SERVICE_PORT") == null)
            {
                Environment.SetEnvironmentVariable("CERTIFY_SERVICE_PORT", "5000");
            }

            // create a test server for the public API, setup authorized and unauthorized clients

            _apiServer = new TestServer(
                new WebHostBuilder()
                 .ConfigureAppConfiguration((context, builder) =>
                 {
                     builder.AddJsonFile("appsettings.api.public.test.json");
                     
                 })
                .UseStartup<Server.API.Startup>()
                );
            
            _httpClientWithAnonymousAccess = _apiServer.CreateClient();
            _clientWithAnonymousAccess = new API.Public.Client(_apiServer.BaseAddress.ToString(), _httpClientWithAnonymousAccess);

            _httpClientWithAuthorizedAccess = _apiServer.CreateClient();
            _clientWithAuthorizedAccess = new API.Public.Client(_apiServer.BaseAddress.ToString(), _httpClientWithAuthorizedAccess);

            CreateCoreServer();
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            _serverProcess.CloseMainWindow();
            _serverProcess.Close();
            _serverProcess.Dispose();
        }

        static Process? _serverProcess = null;
        private static void CreateCoreServer()
        {
            if (_serverProcess == null)
            {
                var serverProcessInfo = new ProcessStartInfo()
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = "Certify.Server.Core.exe"
                };

                _serverProcess = Process.Start(serverProcessInfo);
            }
        }

        public async Task PerformAuth()
        {
            if (!_httpClientWithAuthorizedAccess.DefaultRequestHeaders.Any(h => h.Key == "Authorization"))
            {
                var login = new AuthRequest { Username = "test", Password = "test" };

                var result = await _clientWithAuthorizedAccess.LoginAsync(login);

                if (result.AccessToken != null)
                {
                    _refreshToken = result.RefreshToken;

                    _httpClientWithAuthorizedAccess.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);
                }
            }
        }
    }
}
