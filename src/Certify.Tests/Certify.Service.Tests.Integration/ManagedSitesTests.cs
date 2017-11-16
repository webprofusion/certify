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

        [TestMethod]
        public async Task TestGetManagedSite()
        {
            //get full list
            var filter = new ManagedSiteFilter { MaxResults = 10 };
            var results = await _client.GetManagedSites(filter);

            Assert.IsTrue(results.Count > 0, "Got one or more managed sites");

            //attempt to get single item
            var site = await _client.GetManagedSite(results[0].Id);
            Assert.IsNotNull(site, $"Fetched single managed site details");
        }
    }
}