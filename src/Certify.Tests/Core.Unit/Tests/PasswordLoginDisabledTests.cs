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
    /// <summary>
    /// A hub which federates identity to an OIDC provider can turn off the built in username/password login, so that
    /// local credentials cannot be used to bypass the provider. These cover the API side of that switch.
    /// </summary>
    [TestClass]
    public class PasswordLoginDisabledTests
    {
        private const string TestUserId = "sp-1";

        private static IConfiguration CreateConfig(bool? enablePasswordLogin)
        {
            var values = new Dictionary<string, string?>
            {
                ["JwtSettings:secret"] = "unit-test-signing-secret-value-which-is-long-enough",
                ["JwtSettings:issuer"] = "Certify.Server.Hub.Api",
                ["JwtSettings:authTokenExpirationInMinutes"] = "20",
                ["JwtSettings:refreshTokenExpirationInMinutes"] = "600"
            };

            if (enablePasswordLogin != null)
            {
                values["AuthSettings:enablePasswordLogin"] = enablePasswordLogin.Value ? "true" : "false";
            }

            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        private static AuthController CreateController(IConfiguration config, Mock<ICertifyInternalApiClient> client)
        {
            return new AuthController(
                NullLogger<AuthController>.Instance,
                client.Object,
                config,
                new MemoryCache(new MemoryCacheOptions()))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
        }

        private static Mock<ICertifyInternalApiClient> CreateClientAcceptingCredentials()
        {
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Loose);

            client.Setup(c => c.ValidateSecurityPrincipalPassword(It.IsAny<SecurityPrincipalPasswordCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new SecurityPrincipalCheckResponse
                {
                    IsSuccess = true,
                    SecurityPrincipal = new SecurityPrincipal { Id = TestUserId, Username = "testuser" }
                });

            client.Setup(c => c.GetSecurityPrincipalRoleStatus(It.IsAny<string>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new RoleStatus());

            return client;
        }

        [TestMethod]
        public async Task Login_IsAllowed_WhenTheSettingIsAbsent()
        {
            // an existing install has no AuthSettings section, so the default must keep password login working
            var client = CreateClientAcceptingCredentials();
            var controller = CreateController(CreateConfig(enablePasswordLogin: null), client);

            var result = await controller.Login(new AuthRequest { Username = "testuser", Password = "correct-horse" });

            Assert.IsInstanceOfType(result, typeof(OkObjectResult), "Password login should be enabled by default.");
        }

        [TestMethod]
        public async Task Login_IsAllowed_WhenExplicitlyEnabled()
        {
            var client = CreateClientAcceptingCredentials();
            var controller = CreateController(CreateConfig(enablePasswordLogin: true), client);

            var result = await controller.Login(new AuthRequest { Username = "testuser", Password = "correct-horse" });

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public async Task Login_IsRejected_WhenPasswordLoginIsDisabled()
        {
            // credentials are deliberately valid here: the switch has to reject the request on its own, not rely on
            // the credential check failing
            var client = CreateClientAcceptingCredentials();
            var controller = CreateController(CreateConfig(enablePasswordLogin: false), client);

            var result = await controller.Login(new AuthRequest { Username = "testuser", Password = "correct-horse" });

            var objectResult = result as ObjectResult;
            Assert.IsNotNull(objectResult, "A problem response is expected.");
            Assert.AreEqual(StatusCodes.Status401Unauthorized, objectResult.StatusCode);

            client.Verify(
                c => c.ValidateSecurityPrincipalPassword(It.IsAny<SecurityPrincipalPasswordCheck>(), It.IsAny<AuthContext>()),
                Times.Never,
                "Credentials should not even be checked while password login is disabled.");
        }
    }
}
