using System.Threading.Tasks;
using Certify.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Tests.Integration
{
    [TestClass]
    public class ServiceAuthTests : ServiceTestBase
    {
        [TestMethod]
        public async Task TestAuthFlow()
        {
            AuthContext authContext = null;

            // use windows auth to acquire initial auth key
            var authKey = await _client.GetAuthKeyWindows(authContext);
            Assert.IsNotNull(authKey);

            // attempt request without jwt auth being set yet
            _client.SetConnectionAuthMode("token");

            // check should throw exception
            await Assert.ThrowsExceptionAsync<ServiceCommsException>(async () =>
            {
                var noAuthResult = await _client.GetManagedCertificates(new Models.ManagedCertificateFilter { }, authContext);
                Assert.IsNull(noAuthResult);
            });

            // use auth key to get JWT

            var jwt = await _client.GetAccessToken(authKey, authContext);
            Assert.IsNotNull(jwt);

            // attempt request with JWT set
            var authedResult = await _client.GetManagedCertificates(new Models.ManagedCertificateFilter { }, authContext);
            Assert.IsNotNull(authedResult);

            // refresh JWT
            var refreshedToken = await _client.RefreshAccessToken(authContext);
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
