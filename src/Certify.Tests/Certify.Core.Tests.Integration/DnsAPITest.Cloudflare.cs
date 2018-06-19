using System.Threading.Tasks;
using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class DnsAPITestCloudflare : DnsAPITestBase
    {
        public DnsAPITestCloudflare()
        {
            _credStorageKey = ConfigSettings["TestCredentialsKey_Cloudflare"];
            _zoneId = ConfigSettings["Cloudflare_ZoneId"];
            PrimaryTestDomain = ConfigSettings["Cloudflare_TestDomain"];
        }

        [TestInitialize]
        public async Task InitTest()
        {
            var credentialsManager = new CredentialsManager();
            _credentials = await credentialsManager.GetUnlockedCredentialsDictionary(_credStorageKey);

            _provider = new Providers.DNS.Cloudflare.DnsProviderCloudflare(_credentials);
            await _provider.InitProvider();
        }

        [TestMethod, TestCategory("DNS")]
        public override async Task TestCreateRecord()
        {
            await base.TestCreateRecord();
            // also create a duplicate

            await base.TestCreateRecord();
        }

        [TestMethod, TestCategory("DNS")]
        public override async Task TestDeleteRecord()
        {
            await base.TestDeleteRecord();
        }
    }
}
