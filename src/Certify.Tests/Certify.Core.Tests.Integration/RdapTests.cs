using System;
using System.Threading.Tasks;
using Certify.Shared.Core.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class RdapTests
    {
        [TestMethod, Description("Test Rdap Query")]
        [DataTestMethod]
        [DataRow("example.com", "OK", null)]
        [DataRow("www.example.com", "OK", null)]
        [DataRow("www.example.co.uk", "Error", null)]
        [DataRow("test.musician.io", "Error", null)]
        [DataRow("webprofusion.com", "OK", null)]
        public async Task TestDomainRdapQuery(string domain, string expectedStatus, DateTimeOffset? registered)
        {
            var rdap = new RdapService();

            await rdap.Init();

            var result = await rdap.QueryRDAP(domain);

            Assert.IsNotNull(result, "Result expected");

            Assert.AreEqual(expectedStatus, result.Status, "Status expected");

            System.Diagnostics.Debug.WriteLine($"Domain: {domain}  Registered {result.DateRegistered} Updated {result.DateLastChanged} Expiration {result.DateExpiry}");
        }
    }
}
