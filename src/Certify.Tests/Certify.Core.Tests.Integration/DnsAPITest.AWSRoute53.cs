using Certify.Management;
using Certify.Models.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    [TestClass]
    public class DnsAPITest : IntegrationTestBase
    {
        private string _awsCredStorageKey = "";
        private Dictionary<string, string> _credentials = new Dictionary<string, string>();

        public DnsAPITest()
        {
            _awsCredStorageKey = ConfigurationManager.AppSettings["TestCredentialsKey_Route53"];
        }

        [TestInitialize]
        public async Task Setup()
        {
            var credentialsManager = new CredentialsManager();

            _credentials = await credentialsManager.GetUnlockedCredentialsDictionary(_awsCredStorageKey);
        }

        [TestMethod, TestCategory("DNS")]
        public async Task TestCreateRecord()
        {
            var route53 = new Providers.DNS.AWSRoute53.DnsProviderAWSRoute53(_credentials["accesskey"], _credentials["secretaccesskey"]);

            DnsCreateRecordRequest createRequest = new DnsCreateRecordRequest
            {
                RecordName = "dns-test." + PrimaryTestDomain,
                RecordType = "TXT",
                RecordValue = "A random test",
                TargetDomainName = PrimaryTestDomain,
                ZoneId = "Z2UTXJ6TJN4Q0M"
            };

            Stopwatch stopwatch = Stopwatch.StartNew();
            var createResult = await route53.CreateRecord(createRequest);

            Assert.IsNotNull(createResult);
            Assert.IsTrue(createResult.IsSuccess);

            stopwatch.Stop();
            System.Diagnostics.Debug.WriteLine($"Create DNS Record {createRequest.RecordName} took {stopwatch.Elapsed.TotalSeconds} seconds");
        }

        [TestMethod, TestCategory("DNS")]
        public async Task TestDeleteRecord()
        {
            var route53 = new Providers.DNS.AWSRoute53.DnsProviderAWSRoute53(_credentials["accesskey"], _credentials["secretaccesskey"]);

            DnsDeleteRecordRequest deleteRequest = new DnsDeleteRecordRequest
            {
                RecordName = "dns-test." + PrimaryTestDomain,
                RecordType = "TXT",
                RecordValue = "A random test",
                TargetDomainName = PrimaryTestDomain,
                ZoneId = "Z2UTXJ6TJN4Q0M"
            };

            var stopwatch = Stopwatch.StartNew();
            var deleteResult = await route53.DeleteRecord(deleteRequest);
            Assert.IsTrue(deleteResult.IsSuccess);

            System.Diagnostics.Debug.WriteLine($"Delete DNS Record {deleteRequest.RecordName} took {stopwatch.Elapsed.TotalSeconds} seconds");
        }
    }
}