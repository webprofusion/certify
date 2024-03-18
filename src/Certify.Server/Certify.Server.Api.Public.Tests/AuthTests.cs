using System.Threading.Tasks;
using Certify.API.Public;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Api.Tests
{
    [TestClass]
    public class AuthTests : APITestBase
    {
        [TestMethod]
        public async Task GetAuthStatus_UnauthorizedTest()
        {
            await Assert.ThrowsExceptionAsync<ApiException>(async () => await _clientWithAnonymousAccess.CheckAuthStatusAsync());
        }

        [TestMethod]
        public async Task GetAuthStatus_AuthorizedTest()
        {
            await PerformAuth();

            try
            {
                await _clientWithAuthorizedAccess.CheckAuthStatusAsync();
            }
            catch (ApiException exp)
            {
                Assert.Fail($"Auth status check should not throw exception: {exp.Message}");
            }
        }

        [TestMethod]
        public async Task GetAuthRefreshToken_UnauthorizedTest()
        {
            await Assert.ThrowsExceptionAsync<ApiException>(async () => await _clientWithAnonymousAccess.RefreshAsync("auth123"));
        }

        [TestMethod]
        public async Task GetAuthRefreshToken_AuthorizedTest()
        {
            // Act
            await PerformAuth();

            var response = await _clientWithAuthorizedAccess.RefreshAsync(_refreshToken);

            // Assert
            Assert.IsNotNull(response.AccessToken, "Auth refresh should respond with success");

        }
    }
}
