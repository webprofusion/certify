using System.Net.Http;
using System.Threading.Tasks;
using Certify.Server.Api.Public.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Service.Api.Tests
{
    [TestClass]
    public class AuthTests : APITestBase
    {
        [TestMethod]
        public async Task GetAuthStatus_UnauthorizedTest()
        {
            // Act
            var response = await _clientWithAnonymousAccess.GetAsync(_apiBaseUri + "/auth/status");

            // Assert
            Assert.IsFalse(response.IsSuccessStatusCode, "Auth status should not be success");

            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode, "Auth status should be Unauthorized");
        }

        [TestMethod]
        public async Task GetAuthStatus_AuthorizedTest()
        {
            // Act
            await PerformAuth();

            var response = await _clientWithAuthorizedAccess.GetAsync(_apiBaseUri + "/auth/status");

            // Assert
            Assert.IsTrue(response.IsSuccessStatusCode, "Auth Status check should respond with success");

            var responseString = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, "Auth status code should respond with OK");
        }

        [TestMethod]
        public async Task GetAuthRefreshToken_UnauthorizedTest()
        {
            // Act

            var payload = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { refreshToken = _refreshToken }),
                System.Text.Encoding.UTF8,
                "application/json"
                );

            var response = await _clientWithAnonymousAccess.PostAsync(_apiBaseUri + "/auth/refresh", payload);

            // Assert
            Assert.IsFalse(response.IsSuccessStatusCode, "Auth refresh should not be success");

            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode, "Auth refresh should be Unauthorized");
        }

        [TestMethod]
        public async Task GetAuthRefreshToken_AuthorizedTest()
        {
            // Act
            await PerformAuth();

            var payload = new StringContent(
               System.Text.Json.JsonSerializer.Serialize(new { refreshToken = _refreshToken }),
               System.Text.Encoding.UTF8,
               "application/json"
               );

            var response = await _clientWithAuthorizedAccess.PostAsync(_apiBaseUri + "/auth/refresh", payload);

            // Assert
            Assert.IsTrue(response.IsSuccessStatusCode, "Auth refresh should respond with success");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, "Auth refresh should respond with OK");

            var responseString = await response.Content.ReadAsStringAsync();
            var authResponse = System.Text.Json.JsonSerializer.Deserialize<AuthResponse>(responseString, _defaultJsonSerializerOptions);

        }
    }
}
