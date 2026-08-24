using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;
using ActionResultConfig = Certify.Models.Config.ActionResult;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ApiKeyAuthContextTests
    {
        [TestMethod]
        public async Task ApiKeyAuthenticationHandler_PopulatesPrincipalFromResolvedTokenContext()
        {
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.CheckApiTokenHasAccess(
                    It.IsAny<AccessToken>(),
                    It.IsAny<AccessCheck>(),
                    It.IsAny<AuthContext>()))
                .ReturnsAsync(new ActionResultConfig("OK", true)
                {
                    Result = JObject.FromObject(new AccessTokenAuthorizationContext
                    {
                        SecurityPrincipalId = "sp-123",
                        ScopedAssignedRoles = ["assigned-role-1", "assigned-role-2"]
                    })
                });

            var options = new Mock<IOptionsMonitor<ApiKeyAuthenticationOptions>>(MockBehavior.Strict);
            options.Setup(o => o.Get(It.IsAny<string>())).Returns(new ApiKeyAuthenticationOptions());

            var context = new DefaultHttpContext();
            context.Request.Headers["X-Client-ID"] = "client-id";
            context.Request.Headers["X-Client-Secret"] = "client-secret";

            var handler = new ApiKeyAuthenticationHandler(
                options.Object,
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                client.Object);

            await handler.InitializeAsync(
                new AuthenticationScheme(ApiKeyAuthenticationDefaults.AuthenticationScheme, ApiKeyAuthenticationDefaults.AuthenticationScheme, typeof(ApiKeyAuthenticationHandler)),
                context);

            var result = await handler.AuthenticateAsync();

            Assert.IsTrue(result.Succeeded);
            Assert.IsNotNull(result.Principal);
            Assert.AreEqual("sp-123", result.Principal!.FindFirstValue(ClaimTypes.Sid));

            var scopedAssignedRoles = result.Principal.FindAll(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType).Select(c => c.Value).ToList();
            CollectionAssert.AreEquivalent(new[] { "assigned-role-1", "assigned-role-2" }, scopedAssignedRoles);
        }

        [TestMethod]
        public void CurrentAuthContext_UsesAuthenticatedClaimsWithoutBearerHeader()
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.Sid, "sp-456"),
                            new Claim(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType, "assigned-role-a")
                        ],
                        ApiKeyAuthenticationDefaults.AuthenticationScheme))
            };

            var controller = new ApiControllerBase
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = context
                }
            };

            var authContextProperty = typeof(ApiControllerBase).GetProperty("CurrentAuthContext", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(authContextProperty);

            var authContext = authContextProperty!.GetValue(controller) as AuthContext;
            Assert.IsNotNull(authContext);
            Assert.AreEqual("sp-456", authContext!.UserId);
            CollectionAssert.AreEquivalent(new[] { "assigned-role-a" }, authContext.ScopedAssignedRoles);
        }
    }
}
