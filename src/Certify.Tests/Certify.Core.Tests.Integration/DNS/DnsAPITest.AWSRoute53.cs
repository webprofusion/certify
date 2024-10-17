﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Certify.Datastore.SQLite;
using Certify.Management;
using Certify.Models.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.DNS
{
    [TestClass]
    public class DnsAPITestAWSRoute53 : IntegrationTestBase
    {
        protected string _credStorageKey = "";
        protected Dictionary<string, string> _credentials = new Dictionary<string, string>();
        protected string _zoneId = "";
        protected IDnsProvider _provider;

        public DnsAPITestAWSRoute53()
        {
            _credStorageKey = ConfigSettings["TestCredentialsKey_Route53"];
            _zoneId = ConfigSettings["AWS_ZoneId"];
            PrimaryTestDomain = ConfigSettings["AWS_TestDomain"];
        }

        public async Task<DnsRecord> TestCreateRecord()
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

            _provider = new Providers.DNS.AWSRoute53.DnsProviderAWSRoute53();
            await _provider.InitProvider(_credentials, new Dictionary<string, string> { });
        }

        [TestMethod, TestCategory("DNS")]
        public async Task TestCreateRecords()
        {
            var record1 = await TestCreateRecord();

            // also create a duplicate
            var record2 = await TestCreateRecord();

            Debug.WriteLine($"Cloudflare DNS should now have record {record1.RecordName} with values {record1.RecordValue} and {record2.RecordValue}");
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

        [TestMethod, TestCategory("DNS")]
        public async Task TestProvider()
        {
            var result = await _provider.Test();

            Assert.IsTrue(result.IsSuccess);

        }
    }
}
