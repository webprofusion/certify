using System.Threading.Tasks;
using Certify.SharedUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Tests.Integration
{
    public class ServiceTestBase
    {
        protected Client.CertifyServiceClient _client = null;
        private OwinService _svc;

        [TestInitialize]
        public async Task InitTests()
        {
            _client = new Certify.Client.CertifyServiceClient(new ServiceConfigManager());

            // create API server instance
            if (_svc == null)
            {
                _svc = new Certify.Service.OwinService();
                _svc.Start();

                await Task.Delay(2000);

                await _client.GetAppVersion();
            }
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (_svc != null)
            {
                _svc.Stop();
            }
        }
    }
}
