using System.Threading.Tasks;
using Certify.SharedUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Tests.Integration
{
    public class ServiceTestBase
    {
        protected Client.CertifyServiceClient _client;

        [TestInitialize]
        public async Task InitTests()
        {
            _client = new Certify.Client.CertifyServiceClient(new ServiceConfigManager(), new Shared.ServerConnection { Host = "127.0.0.2", Port = 9000 });

            // TODO: startup instance of API service
        }

        [TestCleanup]
        public void TestCleanup()
        {

        }
    }
}
