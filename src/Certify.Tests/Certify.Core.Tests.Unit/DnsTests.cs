using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class DnsTests
    {
        [TestMethod, Description("Ensure that DNS verification (CAA/DNSSEC) is correct")]
        public void TestDNSTests()
        {
            var net = new NetworkUtils(enableProxyValidationAPI: true);

            // check invalid domain
            Assert.IsFalse(net.CheckDNS("fdlsakdfoweinoijsjdfpsdkfspdf.com").Ok, "Non-existant DNS does not throw an error");

            Assert.IsFalse(net.CheckDNS("cloudapp.net").Ok, "Valid domain that does not resolve to an IP Address does not throw an error");

            // certifytheweb.com = no CAA records
            Assert.IsTrue(net.CheckDNS("certifytheweb.com").Ok, "CAA records are not required");

            // google.com = no letsencrypt.org CAA records (returns "pki.goog" only)
            Assert.IsFalse(net.CheckDNS("google.com").Ok, "If CAA records are present, letsencrypt.org is returned.");

            // dnsimple.com = correctly configured letsencrypt.org CAA record
            Assert.IsTrue(net.CheckDNS("dnsimple.com").Ok, "Correctly configured LE CAA entries work");

            // example.com = correctly configured DNSSEC record
            Assert.IsTrue(net.CheckDNS("example.com").Ok, "correctly configured DNSSEC record should pass dns check");

            // dnssec-failed.org = incorrectly configured DNSSEC record
            Assert.IsFalse(net.CheckDNS("dnssec-failed.org").Ok, "incorrectly configured DNSSEC record should fail dns check");
        }
    }
}