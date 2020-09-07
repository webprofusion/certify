using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Certify.Models;
using System.Collections.Generic;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class ManagedCertificateTests : ServiceTestBase
    {
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


        [TestMethod]
        [Ignore]
        public async Task TestCreateManagedCertificates()
        {
            //get full list

            var list = new List<ManagedCertificate>
            {
                GetExample("msdn.webprofusion.com", 12),
                GetExample("demo.webprofusion.co.uk", 45),
                GetExample("clients.dependencymanager.com",75),
                GetExample("soundshed.com",32),
                GetExample("*.projectbids.co.uk",48),
                GetExample("exchange.projectbids.co.uk",19),
                GetExample("remote.dependencymanager.com",7),
                GetExample("git.example.com",56)

            };

            foreach (var site in list)
            {

                await _client.UpdateManagedCertificate(site);
            }
            
        }

        private ManagedCertificate GetExample(string title, int numDays)
        {
            return new ManagedCertificate()
            {
                Id = Guid.NewGuid().ToString(),
                Name = title,
                DateExpiry = DateTime.Now.AddDays(numDays),
                DateLastRenewalAttempt = DateTime.Now.AddDays(-1),
                LastRenewalStatus = RequestState.Success

            };
        }
    }
}
