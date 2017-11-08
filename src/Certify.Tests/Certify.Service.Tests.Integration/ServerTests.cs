using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class ServerTests
    {
        private Client.CertifyServiceClient _client = null;

        [TestInitialize]
        public void Setup()
        {
            _client = new Certify.Client.CertifyServiceClient();
        }

        [TestMethod]
        public async Task TestServerAvailable()
        {
            bool result = await _client.IsServerAvailable(Models.StandardServerTypes.IIS);

            Assert.IsTrue(result, "IIS is available");
        }

        [TestMethod]
        public async Task TestServerVersion()
        {
            var result = await _client.GetServerVersion(Models.StandardServerTypes.IIS);

            Assert.IsNotNull(result);
        }
    }
}