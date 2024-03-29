﻿using System.Threading.Tasks;
using Certify.SharedUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Tests.Integration
{
    public class ServiceTestBase
    {
        protected Client.CertifyServiceClient _client;
        private OwinService _svc;

        [TestInitialize]
        public async Task InitTests()
        {
            _client = new Certify.Client.CertifyServiceClient(new ServiceConfigManager());

            // create API server instance
            if (_svc == null)
            {
                _svc = new Certify.Service.OwinService();
                _svc.Start(96969);

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
