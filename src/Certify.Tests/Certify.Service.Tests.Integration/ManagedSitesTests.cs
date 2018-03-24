using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class ManagedCertificateTests
    {
        private Client.CertifyServiceClient _client = null;

        [TestInitialize]
        public void Setup()
        {
            _client = new Certify.Client.CertifyServiceClient();
        }

        [TestMethod]
        public async Task TestGetManagedCertificates()
        {
            var filter = new ManagedCertificateFilter();
            var result = await _client.GetManagedCertificates(filter);

            Assert.IsNotNull(result, $"Fetched {result.Count} managed sites");
        }

        [TestMethod]
        public async Task TestGetManagedCertificate()
        {
            //get full list
            var filter = new ManagedCertificateFilter { MaxResults = 10 };
            var results = await _client.GetManagedCertificates(filter);

            Assert.IsTrue(results.Count > 0, "Got one or more managed sites");

            //attempt to get single item
            var site = await _client.GetManagedCertificate(results[0].Id);
            Assert.IsNotNull(site, $"Fetched single managed site details");
        }
    }
}