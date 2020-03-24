using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Builder;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Certify.API.Tests
{

    public class Startup
    {
        public void Configure(IApplicationBuilder app)
        {

        }
    }

    [TestClass]
    public class APITests
    {
        Process _apiService;
        CertifyApiClient _client;

        [TestInitialize]
        public async Task InitTests()
        {
            _client = new Certify.API.CertifyApiClient();

            // TODO : create API server instance instead of invoking directly
            if (_apiService == null)
            {
                _apiService = Process.Start(@"C:\Work\GIT\certify\src\Certify.Service\bin\Debug\net462\CertifySSLManager.Service.exe");
                
                await Task.Delay(2000);
            }
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (_apiService != null)
            {
                _apiService.Kill();
                _apiService.WaitForExit();
            }
        }

        [TestMethod]
        public async Task CanCreateAndDeleteManagedCertificate()
        {
            var request = new CreateRequest("Test", new List<string> {
                    "test.com",
                    "www.test.com"
                });

            var result = await _client.CreateManagedCertificate(request);

            var createdOK = result.IsSuccess;

            if (createdOK)
            {
                // delete test
                var deleteResult = await _client.DeleteManagedCertificate(result.Result.Id);

                Assert.IsTrue(deleteResult.IsSuccess, "Could not delete item");
            }
            
            Assert.IsTrue(createdOK,"Could not create item");

        }

        [TestMethod]
        public async Task CanRenewAllManagedCertificate()
        {
            var result = await _client.RenewAll();

            Assert.IsTrue(result.IsSuccess);
        }

    }
}
