using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Shared.Core.Utils;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class DnsQueryTests
    {
        [TestMethod, Description("Ensure that DNS verification (CAA/DNSSEC) is correct")]
        [Ignore("Fails in CI build")]
        public async Task TestDNSTests()
        {
            var net = new NetworkUtils(enableProxyValidationAPI: true);

            var logImp = new LoggerConfiguration()
                .WriteTo.Debug()
                .CreateLogger();

            var log = new Loggy(logImp);

            // check invalid domain
            var result = await net.CheckDNS(log, "fdlsakdfoweinoijsjdfpsdkfspdf.com");
            Assert.IsFalse(result.All(r => r.IsSuccess), "Non-existant DNS does not throw an error");

            result = await net.CheckDNS(log, "cloudapp.net");
            Assert.IsFalse(result.All(r => r.IsSuccess), "Valid domain that does not resolve to an IP Address does not throw an error");

            // certifytheweb.com = no CAA records
            result = await net.CheckDNS(log, "webprofusion.com");
            Assert.IsTrue(result.All(r => r.IsSuccess), "CAA records are not required");

            // google.com = no letsencrypt.org CAA records (returns "pki.goog" only)
            result = await net.CheckDNS(log, "google.com");
            Assert.IsFalse(result.All(r => r.IsSuccess), "If CAA records are present, letsencrypt.org is returned.");

            // dnsimple.com = correctly configured letsencrypt.org CAA record
            result = await net.CheckDNS(log, "dnsimple.com");
            Assert.IsTrue(result.All(r => r.IsSuccess), "Correctly configured LE CAA entries work");

            // example.com = correctly configured DNSSEC record
            result = await net.CheckDNS(log, "example.com");
            Assert.IsTrue(result.All(r => r.IsSuccess), "correctly configured DNSSEC record should pass dns check");

            // dnssec-failed.org = incorrectly configured DNSSEC record
            result = await net.CheckDNS(log, "dnssec-failed.org");
            Assert.IsFalse(result.All(r => r.IsSuccess), "incorrectly configured DNSSEC record should fail dns check");
        }

#if NET6_0_OR_GREATER
        [TestMethod, Description("Check for a DNS TXT record")]
        public async Task TestDNS_CheckTXT()
        {
            var net = new NetworkUtils(enableProxyValidationAPI: true);

            var logImp = new LoggerConfiguration()
                .WriteTo.Debug()
                .CreateLogger();

            var log = new Loggy(logImp);

            // check invalid domain
            var result = await net.GetDNSRecordTXT(log, "_acme-challenge-test.cointelligence.io");
            Assert.IsNull(result, "Non-existant DNS TXT record does not throw an error");
        }
#endif
    }
}
