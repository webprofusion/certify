using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.API.Tests
{
    [TestClass]
    public class APITests
    {
        [TestMethod]
        public async Task CanCreateManagedCertificate()
        {

            // start service
            var svc = new Certify.Service.OwinService();
            svc.Start();

            var client = new Certify.API.CertifyApiClient();

            var request = new CreateRequest("Test", new List<string> {
                    "test.com",
                    "www.test.com"
                });

            var result = await client.Create(request);


            Assert.IsTrue(result.IsSuccess);

        }
    }
}
