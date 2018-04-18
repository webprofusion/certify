using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    [TestClass]
    public class DnsAPITestAWSRoute53 : DnsAPITestBase
    {
        public DnsAPITestAWSRoute53()
        {
            _credStorageKey = ConfigSettings["TestCredentialsKey_Route53"];
            _zoneId = ConfigSettings["AWS_ZoneId"];
            PrimaryTestDomain = ConfigSettings["AWS_TestDomain"];
        }

        [TestInitialize]
        public async Task InitTest()
        {
            var credentialsManager = new CredentialsManager();
            _credentials = await credentialsManager.GetUnlockedCredentialsDictionary(_credStorageKey);

            _provider = new Providers.DNS.AWSRoute53.DnsProviderAWSRoute53(_credentials);
            await _provider.InitProvider();
        }
    }
}