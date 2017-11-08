using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class ManagedSiteTests
    {
        private Client.CertifyServiceClient _client = null;

        [TestInitialize]
        public void Setup()
        {
            _client = new Certify.Client.CertifyServiceClient();
        }

        [TestMethod]
        public async Task TestGetManagedSites()
        {
            var filter = new ManagedSiteFilter();
            var result = await _client.GetManagedSites(filter);

            Assert.IsNotNull(result, $"Fetched {result.Count} managed sites");
        }
    }
}