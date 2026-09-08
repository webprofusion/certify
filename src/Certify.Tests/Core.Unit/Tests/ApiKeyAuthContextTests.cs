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
            // the handler authenticates the token, it does not authorize an action: a strict mock which only
            // answers ResolveApiToken fails the test if it starts asking whether the principal may do something
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.ResolveApiToken(
                    It.IsAny<AccessToken>(),
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

        /// <summary>
        /// An API access token authorized request reaches [AllowAnonymous] endpoints without the ApiToken
        /// authentication scheme having run, so HttpContext.User carries no principal for it. The domain restriction
        /// check must still resolve the principal from the token which authorized the request, rather than failing
        /// closed and rejecting every API token call.
        /// </summary>
        [TestMethod]
        public async Task CheckIdentifiersAuthorized_UsesAccessTokenPrincipalWithoutAuthenticatedUser()
        {
            var (controller, client) = CreateAccessTokenAuthorizedController(domainRestriction: "*.example.com");

            var requestAuthorized = await InvokeCheckRequestAuthorized(controller, client);
            Assert.IsTrue(requestAuthorized.IsSuccess, requestAuthorized.Message);

            var permitted = await InvokeCheckIdentifiersAuthorized(controller, client, "www.example.com");
            Assert.IsTrue(permitted.IsSuccess, permitted.Message);

            var denied = await InvokeCheckIdentifiersAuthorized(controller, client, "www.notpermitted.com");
            Assert.IsFalse(denied.IsSuccess);
            StringAssert.Contains(denied.Message, "not permitted by the domain restrictions");
        }

        /// <summary>
        /// A principal whose authorizing roles carry no domain resources is unrestricted, so the check passes without
        /// needing to know the identifiers.
        /// </summary>
        [TestMethod]
        public async Task CheckIdentifiersAuthorized_AccessTokenPrincipalWithoutDomainRestrictionsIsUnrestricted()
        {
            var (controller, client) = CreateAccessTokenAuthorizedController(domainRestriction: null);

            var requestAuthorized = await InvokeCheckRequestAuthorized(controller, client);
            Assert.IsTrue(requestAuthorized.IsSuccess, requestAuthorized.Message);

            var result = await InvokeCheckIdentifiersAuthorized(controller, client, "anything.example.org");
            Assert.IsTrue(result.IsSuccess, result.Message);
        }

        private static (ApiControllerBase Controller, ICertifyInternalApiClient Client) CreateAccessTokenAuthorizedController(string? domainRestriction)
        {
            var authorizingRole = new AssignedRole
            {
                Id = "ar-1",
                RoleId = "cert_consumer_role",
                SecurityPrincipalId = "sp-token",
                IncludedResources = domainRestriction == null
                    ? []
                    : [new Resource { ResourceType = ResourceTypes.Domain, Identifier = domainRestriction }]
            };

            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);

            client.Setup(c => c.CheckApiTokenHasAccess(
                    It.IsAny<AccessToken>(),
                    It.IsAny<AccessCheck>(),
                    It.IsAny<AuthContext>()))
                .ReturnsAsync(new ActionResultConfig("OK", true)
                {
                    // as it arrives from a remote backend, over the internal API
                    Result = JObject.FromObject(new AccessTokenAuthorizationContext
                    {
                        SecurityPrincipalId = "sp-token",
                        ScopedAssignedRoles = ["ar-1"]
                    })
                });

            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((AccessCheck check, AuthContext _) =>
                {
                    // the scope must be evaluated for the token's principal and role scope, not for an anonymous caller
                    Assert.AreEqual("sp-token", check.SecurityPrincipalId);
                    CollectionAssert.AreEquivalent(new[] { "ar-1" }, check.ScopedAssignedRoles);

                    return new ResourceAccessScope
                    {
                        HasAccess = true,
                        IsUnrestricted = true,
                        AuthorizingRoles = [authorizingRole]
                    };
                });

            var context = new DefaultHttpContext();

            // an API token request carries no bearer token and no authenticated user
            context.Request.Headers["X-Client-ID"] = "client-id";
            context.Request.Headers["X-Client-Secret"] = "client-secret";

            var controller = new ApiControllerBase
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };

            return (controller, client.Object);
        }

        private static async Task<ActionResultConfig> InvokeCheckRequestAuthorized(ApiControllerBase controller, ICertifyInternalApiClient client)
        {
            var method = typeof(ApiControllerBase).GetMethod("CheckRequestAuthorized", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            var check = new AccessCheck(default!, ResourceTypes.Certificate, StandardResourceActions.CertificateDownload);

            return await (Task<ActionResultConfig>)method!.Invoke(controller, [client, check])!;
        }

        private static async Task<ActionResultConfig> InvokeCheckIdentifiersAuthorized(ApiControllerBase controller, ICertifyInternalApiClient client, params string?[] identifiers)
        {
            var method = typeof(ApiControllerBase).GetMethod("CheckIdentifiersAuthorized", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            return await (Task<ActionResultConfig>)method!.Invoke(
                controller,
                [client, StandardResourceActions.CertificateDownload, identifiers])!;
        }
    }
}
