using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class DomainZoneMatchTests
    {
        [TestMethod, Description("Ensure identified root domain and normalized record names are correct")]
        public async Task DetermineRootDomainTests()
        {
            var mockDnsProvider = new Mock<DnsProviderBase>();
            mockDnsProvider.Setup(p => p.GetZones()).ReturnsAsync(
                new List<DnsZone> {
                    new DnsZone{ Name="test.com", ZoneId="123-test.com"},
                    new DnsZone{ Name="subdomain.test.com", ZoneId="345-subdomain-test.com"}
                }
            );

            var domainRoot = await mockDnsProvider.Object.DetermineZoneDomainRoot("www.dev.subdomain.test.com", "345-subdomain-test.com");
            Assert.IsTrue(domainRoot.ZoneId == "345-subdomain-test.com");

            domainRoot = await mockDnsProvider.Object.DetermineZoneDomainRoot("www.test.com", "123-test.com");
            Assert.IsTrue(domainRoot.ZoneId == "123-test.com");

            domainRoot = await mockDnsProvider.Object.DetermineZoneDomainRoot("www.test.com", null);
            Assert.IsTrue(domainRoot.ZoneId == "123-test.com");

            var normalisedRecordName = mockDnsProvider.Object.NormaliseRecordName(domainRoot, "www.subdomain.test.com");
            Assert.IsTrue(normalisedRecordName == "www.subdomain");

            domainRoot = await mockDnsProvider.Object.DetermineZoneDomainRoot("www.subdomain.test.com", null);
            Assert.IsTrue(domainRoot.ZoneId == "345-subdomain-test.com");

            normalisedRecordName = mockDnsProvider.Object.NormaliseRecordName(domainRoot, "www.subdomain.test.com");
            Assert.IsTrue(normalisedRecordName == "www");

            normalisedRecordName = mockDnsProvider.Object.NormaliseRecordName(domainRoot, "www.dev.subdomain.test.com");
            Assert.IsTrue(normalisedRecordName == "www.dev");

            domainRoot = await mockDnsProvider.Object.DetermineZoneDomainRoot("www.test.com", null);
            normalisedRecordName = mockDnsProvider.Object.NormaliseRecordName(domainRoot, "www.test.com");
            Assert.IsTrue(normalisedRecordName == "www");
        }
    }
}
