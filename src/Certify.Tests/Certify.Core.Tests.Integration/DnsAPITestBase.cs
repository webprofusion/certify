using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Certify.Models.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    public class DnsAPITestBase : IntegrationTestBase
    {
        protected string _credStorageKey = "";
        protected Dictionary<string, string> _credentials = new Dictionary<string, string>();
        protected string _zoneId = "";
        protected IDnsProvider _provider = null;

        [TestMethod, TestCategory("DNS")]
        public async Task TestCreateRecord()
        {
            var createRequest = new DnsRecord
            {
                RecordName = "dns-test." + PrimaryTestDomain,
                RecordType = "TXT",
                RecordValue = "A random test",
                TargetDomainName = PrimaryTestDomain,
                ZoneId = _zoneId
            };

            Stopwatch stopwatch = Stopwatch.StartNew();
            var createResult = await _provider.CreateRecord(createRequest);

            Assert.IsNotNull(createResult);
            Assert.IsTrue(createResult.IsSuccess);

            stopwatch.Stop();
            System.Diagnostics.Debug.WriteLine($"Create DNS Record {createRequest.RecordName} took {stopwatch.Elapsed.TotalSeconds} seconds");
        }

        [TestMethod, TestCategory("DNS")]
        public async Task TestDeleteRecord()
        {
            var deleteRequest = new DnsRecord
            {
                RecordName = "dns-test." + PrimaryTestDomain,
                RecordType = "TXT",
                RecordValue = "A random test",
                TargetDomainName = PrimaryTestDomain,
                ZoneId = _zoneId
            };

            var stopwatch = Stopwatch.StartNew();
            var deleteResult = await _provider.DeleteRecord(deleteRequest);
            Assert.IsTrue(deleteResult.IsSuccess);

            System.Diagnostics.Debug.WriteLine($"Delete DNS Record {deleteRequest.RecordName} took {stopwatch.Elapsed.TotalSeconds} seconds");
        }
    }
}
