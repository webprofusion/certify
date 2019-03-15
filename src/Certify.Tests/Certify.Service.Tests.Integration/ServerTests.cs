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
            var result = await _client.IsServerAvailable(Models.StandardServerTypes.IIS);

            Assert.IsTrue(result, "IIS is available");
        }

        [TestMethod]
        public async Task TestServerVersion()
        {
            var result = await _client.GetServerVersion(Models.StandardServerTypes.IIS);

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestServerSiteList()
        {
            var result = await _client.GetServerSiteList(Models.StandardServerTypes.IIS);

            Assert.IsNotNull(result, "Server Site List returned");

            Assert.IsTrue(result.Count > 0, "Has one or more results");
        }

        [TestMethod]
        public async Task TestServerSiteDomains()
        {
            var sites = await _client.GetServerSiteList(Models.StandardServerTypes.IIS);

            var site = sites[0];

            var result = await _client.GetServerSiteDomains(Models.StandardServerTypes.IIS, site.SiteId);

            Assert.IsNotNull(result, "Domain Options List returned");

            Assert.IsTrue(result.Count > 0, "Has one or more results");
        }
    }
}