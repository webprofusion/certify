using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

            var list = GetTestManagedCerts();

            foreach (var site in list)
            {
                await _client.UpdateManagedCertificate(site);
            }
        }

        private List<ManagedCertificate> GetTestManagedCerts() => new List<ManagedCertificate>
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

        [TestMethod]
        [Ignore]
        public async Task TestManagedCertificatesVersioning()
        {
            var list = GetTestManagedCerts();

            try
            {
                //create test managed certs

                foreach (var site in list)
                {
                    await _client.UpdateManagedCertificate(site);
                }

                // attempting to add twice should result in version conflict

                foreach (var site in list)
                {
                    await Assert.ThrowsExceptionAsync<Certify.Client.ServiceCommsException>(async () => await _client.UpdateManagedCertificate(site));
                }

                // get latest versions of each managed cert

                var currentVersions = new ConcurrentDictionary<string, ManagedCertificate>();

                foreach (var site in list)
                {
                    var current = await _client.GetManagedCertificate(site.Id);
                    currentVersions.TryAdd(current.Id, current);
                }

                // attempt many updates, always using current item version

                long maxVersions = 10;
                for (var passes = 0; passes < maxVersions; passes++)
                {
                    foreach (var site in list)
                    {
                        var current = await _client.GetManagedCertificate(site.Id);
                        current.Name = Guid.NewGuid().ToString();
                        current = await _client.UpdateManagedCertificate(current);

                        currentVersions[current.Id] = current;
                    }
                }

                Assert.AreEqual(maxVersions + 1, currentVersions.First().Value.Version);
            }
            finally
            {
                foreach (var site in list)
                {
                    await _client.DeleteManagedCertificate(site.Id);
                }
            }
        }
    }
}
