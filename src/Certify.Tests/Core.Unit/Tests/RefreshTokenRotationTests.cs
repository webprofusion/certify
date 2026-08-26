using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class RefreshTokenRotationTests
    {
        private const string TestUserId = "sp-1";

        private static IConfiguration CreateConfig()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:secret"] = "unit-test-signing-secret-value-which-is-long-enough",
                    ["JwtSettings:issuer"] = "Certify.Server.Hub.Api",
                    ["JwtSettings:authTokenExpirationInMinutes"] = "20",
                    ["JwtSettings:refreshTokenExpirationInMinutes"] = "600"
                })
                .Build();
        }

        private static AuthController CreateController(IMemoryCache cache)
        {
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Loose);

            client.Setup(c => c.GetSecurityPrincipals(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<SecurityPrincipal>
                {
                    new() { Id = TestUserId, Username = "testuser" }
                });

            client.Setup(c => c.GetSecurityPrincipalRoleStatus(It.IsAny<string>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new RoleStatus());

            return new AuthController(
                NullLogger<AuthController>.Instance,
                client.Object,
                CreateConfig(),
                cache)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
        }

        /// <summary>
        /// Seed a refresh token the way a successful login would, without going through password validation.
        /// </summary>
        private static void SeedRefreshToken(IMemoryCache cache, string refreshToken)
        {
            cache.Set("RefreshToken_" + refreshToken, TestUserId, System.TimeSpan.FromMinutes(600));
        }

        [TestMethod]
        public async Task Refresh_IssuesNewTokens_ForAValidRefreshToken()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var controller = CreateController(cache);

            SeedRefreshToken(cache, "refresh-token-1");

            var result = await controller.Refresh("refresh-token-1") as OkObjectResult;

            Assert.IsNotNull(result, "A valid refresh token should be accepted.");

            var response = result.Value as AuthResponse;
            Assert.IsNotNull(response);
            Assert.IsTrue(response.IsSuccess);
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.RefreshToken));
            Assert.AreNotEqual("refresh-token-1", response.RefreshToken, "A new refresh token should be issued.");
        }

        [TestMethod]
        public async Task Refresh_RejectsAReusedRefreshToken()
        {
            // Without invalidation on redemption a captured refresh token stays usable for its full lifetime,
            // which is the whole point of rotating it.
            var cache = new MemoryCache(new MemoryCacheOptions());
            var controller = CreateController(cache);

            SeedRefreshToken(cache, "refresh-token-1");

            var first = await controller.Refresh("refresh-token-1");
            Assert.IsInstanceOfType(first, typeof(OkObjectResult), "First use should succeed.");

            var second = await controller.Refresh("refresh-token-1");
            Assert.IsInstanceOfType(second, typeof(UnauthorizedResult), "A refresh token must not be redeemable twice.");
        }

        [TestMethod]
        public async Task Refresh_ReplacementTokenIsUsableAndAlsoSingleUse()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var controller = CreateController(cache);

            SeedRefreshToken(cache, "refresh-token-1");

            var first = await controller.Refresh("refresh-token-1") as OkObjectResult;
            var replacement = (first!.Value as AuthResponse)!.RefreshToken;

            var second = await controller.Refresh(replacement) as OkObjectResult;
            Assert.IsNotNull(second, "The replacement refresh token should be usable.");

            var third = await controller.Refresh(replacement);
            Assert.IsInstanceOfType(third, typeof(UnauthorizedResult), "The replacement must also be single use.");
        }

        [TestMethod]
        public async Task Refresh_RejectsAnUnknownRefreshToken()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var controller = CreateController(cache);

            var result = await controller.Refresh("never-issued");

            Assert.IsInstanceOfType(result, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task Refresh_DoesNotReturnPasswordMaterial()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var controller = CreateController(cache);

            SeedRefreshToken(cache, "refresh-token-1");

            var result = await controller.Refresh("refresh-token-1") as OkObjectResult;
            var response = result!.Value as AuthResponse;

            Assert.IsNotNull(response!.SecurityPrincipal);
            Assert.IsNull(response.SecurityPrincipal.Password, "A password hash must never be returned to the caller.");
        }
    }
}
