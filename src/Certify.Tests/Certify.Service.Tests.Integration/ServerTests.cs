using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class ServerTests : ServiceTestBase
    {
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

            var result = await _client.GetServerSiteDomains(Models.StandardServerTypes.IIS, site.Id);

            Assert.IsNotNull(result, "Domain Options List returned");

            Assert.IsTrue(result.Count > 0, "Has one or more results");
        }
    }
}
