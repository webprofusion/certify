using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Certify.Datastore.SQLite;
using Certify.Management;
using Certify.Models.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.DNS
{
    [TestClass]
    [Ignore("Requires credential setup")]
    public class DnsAPITestAzure : IntegrationTestBase
    {
        protected string _credStorageKey = "";
        protected Dictionary<string, string> _credentials = new Dictionary<string, string>();
        protected string _zoneId = "";
        protected IDnsProvider _provider;

        public DnsAPITestAzure()
        {
            _credStorageKey = ConfigSettings["TestCredentialsKey_Azure"];
            _zoneId = ConfigSettings["Azure_ZoneId"];
            PrimaryTestDomain = ConfigSettings["Azure_TestDomain"];
        }

        private async Task<DnsRecord> TestCreateRecord()
        {
            var createRequest = new DnsRecord
            {
                RecordName = "dns-test." + PrimaryTestDomain,
                RecordType = "TXT",
                RecordValue = "A random test " + System.Guid.NewGuid().ToString(),
                TargetDomainName = PrimaryTestDomain,
                ZoneId = _zoneId
            };

            var stopwatch = Stopwatch.StartNew();
            var createResult = await _provider.CreateRecord(createRequest);

            Assert.IsNotNull(createResult);
            Assert.IsTrue(createResult.IsSuccess);

            stopwatch.Stop();
            Debug.WriteLine($"Create DNS Record {createRequest.RecordName} took {stopwatch.Elapsed.TotalSeconds} seconds");
            return createRequest;
        }

        [TestInitialize]
        public async Task InitTest()
        {
            var credentialsManager = new SQLiteCredentialStore();
            _credentials = await credentialsManager.GetUnlockedCredentialsDictionary(_credStorageKey);

            _provider = new Providers.DNS.Azure.DnsProviderAzure();
            await _provider.InitProvider(_credentials, new Dictionary<string, string> { });
        }

        [TestMethod, TestCategory("DNS")]
        public async Task TestCreateRecords()
        {
            var record1 = await TestCreateRecord();

            // also create a duplicate
            var record2 = await TestCreateRecord();

            Debug.WriteLine($"Azure DNS should now have record {record1.RecordName} with values {record1.RecordValue} and {record2.RecordValue}");
        }

        [TestMethod, TestCategory("DNS")]
        public async Task TestDeleteRecord()
        {
            var deleteRequest = new DnsRecord
            {
                RecordName = "dns-test." + PrimaryTestDomain,
                RecordType = "TXT",
                TargetDomainName = PrimaryTestDomain,
                ZoneId = _zoneId
            };

            var stopwatch = Stopwatch.StartNew();
            var deleteResult = await _provider.DeleteRecord(deleteRequest);
            Assert.IsTrue(deleteResult.IsSuccess);

            Debug.WriteLine($"Delete DNS Record {deleteRequest.RecordName} took {stopwatch.Elapsed.TotalSeconds} seconds");
        }
    }
}
