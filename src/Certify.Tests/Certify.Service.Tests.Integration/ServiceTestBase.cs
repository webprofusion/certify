using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Tests.Integration
{
    public class ServiceTestBase
    {
        protected Client.CertifyServiceClient _client = null;
        private Process _apiService;

        [TestInitialize]
        public async Task InitTests()
        {
            _client = new Certify.Client.CertifyServiceClient(new SharedUtils.ServiceConfigManager());

            // TODO : create API server instance instead of invoking directly
            if (_apiService == null)
            {
                _apiService = Process.Start(@"C:\Work\GIT\certify\src\Certify.Service\bin\Debug\net462\CertifySSLManager.Service.exe");

                await Task.Delay(2000);
            }
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (_apiService != null)
            {
                _apiService.Kill();
                _apiService.WaitForExit();
            }
        }
    }
}
