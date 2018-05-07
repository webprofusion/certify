using System.Threading.Tasks;
using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class DnsAPITestAzure : DnsAPITestBase
    {
        public DnsAPITestAzure()
        {
            _credStorageKey = ConfigSettings["TestCredentialsKey_Azure"];
            _zoneId = ConfigSettings["Azure_ZoneId"];
            PrimaryTestDomain = ConfigSettings["Azure_TestDomain"];
        }

        [TestInitialize]
        public async Task InitTest()
        {
            var credentialsManager = new CredentialsManager();
            _credentials = await credentialsManager.GetUnlockedCredentialsDictionary(_credStorageKey);

            _provider = new Providers.DNS.Azure.DnsProviderAzure(_credentials);
            await _provider.InitProvider();
        }

        [TestMethod, TestCategory("DNS")]
        public override async Task TestCreateRecord()
        {
            await base.TestCreateRecord();
        }

        [TestMethod, TestCategory("DNS")]
        public override async Task TestDeleteRecord()
        {
            await base.TestDeleteRecord();
        }
    }
}
