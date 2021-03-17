using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System.Diagnostics;
using Certify.Client;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class ServiceAuthTests: ServiceTestBase
    {
        [TestMethod]
        public async Task TestAuthFlow()
        {
            // use windows auth to acquire initial auth key
            var authKey = await _client.GetAuthKeyWindows();
            Assert.IsNotNull(authKey);

            // attempt request without jwt auth being set yet
            _client.SetConnectionAuthMode("token");

            // check should throw exception
            await Assert.ThrowsExceptionAsync<ServiceCommsException>(async () =>
            {
                var noAuthResult = await _client.GetManagedCertificates(new Models.ManagedCertificateFilter { });
                Assert.IsNull(noAuthResult);
            });

            // use auth key to get JWT
            
            var jwt = await _client.GetAccessToken(authKey);
            Assert.IsNotNull(jwt);

            // attempt request with JWT set
            var authedResult = await _client.GetManagedCertificates(new Models.ManagedCertificateFilter { });
            Assert.IsNotNull(authedResult);

            // refresh JWT
            var refreshedToken = await _client.RefreshAccessToken();
            Assert.IsNotNull(jwt);
        }

        [TestMethod]
        public async Task TestUpdateCheck()
        {
            var result = await _client.CheckForUpdates();

            Assert.IsNotNull(result.Version);
        }
    }
}
