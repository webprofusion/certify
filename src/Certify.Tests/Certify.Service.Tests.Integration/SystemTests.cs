using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class SystemTests
    {
        private Client.CertifyServiceClient _client = null;

        [TestInitialize]
        public void Setup()
        {
            _client = new Certify.Client.CertifyServiceClient();
        }

        [TestMethod]
        public async Task TestVersionCheck()
        {
            var result = await _client.GetAppVersion();

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestUpdateCheck()
        {
            var result = await _client.CheckForUpdates();

            Assert.IsNotNull(result.Version);
        }
    }
}