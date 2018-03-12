using Certify.Management;
using Certify.Models.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Configuration;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    [TestClass]
    public class DnsAPITest : IntegrationTestBase
    {
        private string _awsCredStorageKey = "";

        public DnsAPITest()
        {
            _awsCredStorageKey = ConfigurationManager.AppSettings["TestCredentialsKey_Route53"];
        }

        [TestMethod, TestCategory("DNS")]
        public async Task TestCreateRecord()
        {
            var credentialsManager = new CredentialsManager();

            var credArray = await credentialsManager.GetUnlockedCredentialsArray(_awsCredStorageKey);

            Certify.Providers.DNS.AWSRoute53.DnsProviderAWSRoute53 route53 = new Providers.DNS.AWSRoute53.DnsProviderAWSRoute53(credArray[0], credArray[1]);

            DnsCreateRecordRequest createRequest = new DnsCreateRecordRequest
            {
                RecordName = "dns-test." + PrimaryTestDomain,
                RecordType = "TXT",
                RecordValue = "A random test",
                TargetDomainName = PrimaryTestDomain,
                ZoneId = "Z2UTXJ6TJN4Q0M"
            };
            var createResult = await route53.CreateRecord(createRequest);

            Assert.IsNotNull(createResult);
            Assert.IsTrue(createResult.IsSuccess);

            DnsDeleteRecordRequest deleteRequest = new DnsDeleteRecordRequest
            {
                RecordName = "dns-test." + PrimaryTestDomain,
                RecordType = "TXT",
                RecordValue = "A random test",
                TargetDomainName = PrimaryTestDomain,
                ZoneId = "Z2UTXJ6TJN4Q0M"
            };
            var deleteResult = await route53.DeleteRecord(deleteRequest);
            Assert.IsTrue(createResult.IsSuccess);
        }
    }
}