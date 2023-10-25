using System;
using System.Threading.Tasks;
using Certify.Shared.Core.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class RdapTests
    {

        public RdapTests()
        {

        }

        [TestMethod, Description("Test domain TLD check")]
        [DataTestMethod]
        [DataRow("example.com","com")]
        [DataRow("www.example.com", "com")]
        [DataRow("www.example.co.uk", "uk")]
        [DataRow("www.musician.io", "io")]
        public async Task TestDomainTLD(string domain, string expectedTld)
        {
            var rdap = new RdapService();

            await rdap.Init();

            var tld = rdap.GetTLD(domain, true);

            Assert.AreEqual(expectedTld, tld, "TLD Should match");
        }

        [TestMethod, Description("Test domain normalisation")]
        [DataTestMethod]
        [DataRow("example.com", "example.com")]
        [DataRow("www.example.com", "example.com")]
        [DataRow("www.example.co.uk", "example.co.uk")]
        [DataRow("test.musician.io", "musician.io")]
        public async Task TestDomainNormalisation(string domain, string expectedDomain)
        {
            var rdap = new RdapService();

            await rdap.Init();

            var normalisedDomain = rdap.NormaliseDomain(domain);

            Assert.AreEqual(expectedDomain, normalisedDomain, "Domain Should match");
        }
    }
}
